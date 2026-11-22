param([string]$AppDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if (!$AppDirectory) { $AppDirectory=$root }
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $AppDirectory 'DollView.exe'))
$converter=$assembly.GetType('LumaView.ModelConversion').GetMethod('Convert',[Reflection.BindingFlags]'NonPublic,Static')
$work=Join-Path $root ('dist\conversion-check-'+[guid]::NewGuid().ToString('N'))
$inputs=New-Item -ItemType Directory -Path (Join-Path $work 'inputs') -Force
function Convert-Model($source,$destination) {
    $converter.Invoke($null,@([string]$source,[string]$destination,[Threading.CancellationToken]::None))
    if (!(Test-Path -LiteralPath $destination -PathType Leaf)) { throw "Output missing: $destination" }
}
function Must-Fail([scriptblock]$operation) {
    try { & $operation } catch { return }
    throw 'Conversion unexpectedly succeeded.'
}
$seed=Join-Path $inputs.FullName 'motion.gltf'
Copy-Item -LiteralPath (Join-Path $root 'tests\fixtures\motion.gltf') -Destination $seed
$formats=@('fbx','glb','gltf','obj','stl')
foreach ($format in $formats) {
    if ($format -ne 'gltf') { Convert-Model $seed (Join-Path $inputs.FullName "motion.$format") }
}
$original=@{}
foreach ($file in (Get-ChildItem $inputs.FullName -Recurse -File)) { $original[$file.FullName]=(Get-FileHash -LiteralPath $file.FullName).Hash }
foreach ($from in $formats) {
    foreach ($to in $formats) {
        if ($from -eq $to) { continue }
        $dir=New-Item -ItemType Directory -Path (Join-Path $work "$from-$to")
        $file=Join-Path $dir.FullName "converted sample.$to"
        Convert-Model (Join-Path $inputs.FullName "motion.$from") $file
        if ($to -eq 'obj' -and !(Test-Path -LiteralPath ([IO.Path]::ChangeExtension($file,'mtl')))) { throw 'Missing OBJ material library.' }
        # Re-import each result. GLB's declared length and glTF's external buffer must agree with disk.
        $probe=Join-Path $dir.FullName 'reopen.gltf'
        if ($to -eq 'gltf') { $probe=$file } else { Convert-Model $file $probe }
        $gltf=Get-Content -LiteralPath $probe -Raw -Encoding UTF8 | ConvertFrom-Json
        if (!$gltf.meshes.Count) { throw "No meshes in $from -> $to" }
        foreach ($buffer in $gltf.buffers) {
            $bin=Get-Item -LiteralPath (Join-Path $dir.FullName ([Uri]::UnescapeDataString($buffer.uri)))
            if ($bin.Length -lt $buffer.byteLength) { throw 'Incomplete glTF buffer.' }
        }
        if ($to -eq 'glb') {
            $bytes=[IO.File]::ReadAllBytes($file)
            if ([Text.Encoding]::ASCII.GetString($bytes,0,4) -ne 'glTF' -or [BitConverter]::ToUInt32($bytes,8) -ne $bytes.Length) { throw 'Invalid GLB header.' }
        }
        if ($from -in @('fbx','glb','gltf') -and $to -in @('fbx','glb','gltf')) {
            if ($gltf.animations.Count -ne 2) { throw "Lost clips in $from -> $to" }
            foreach ($anim in $gltf.animations) {
                $duration=($anim.samplers | ForEach-Object { $gltf.accessors[$_.input].max[0] } | Measure-Object -Maximum).Maximum
                if (($anim.name -eq 'Stretch' -and $duration -ne 2) -or ($anim.name -eq 'Move' -and $duration -ne 1)) { throw "Changed timing: $($anim.name)" }
            }
        }
        Write-Output "PASS $from -> $to"
    }
}
$collision=Join-Path $work 'existing.obj'
$material=[IO.Path]::ChangeExtension($collision,'mtl')
[IO.File]::WriteAllText($material,'existing material')
Must-Fail { Convert-Model $seed $collision }
if ((Test-Path -LiteralPath $collision) -or [IO.File]::ReadAllText($material) -ne 'existing material') { throw 'Companion file collision was not preserved.' }
Must-Fail { Convert-Model $seed $seed }
$bad=Join-Path $work 'invalid.glb';[IO.File]::WriteAllText($bad,'invalid')
Must-Fail { Convert-Model $bad (Join-Path $work 'invalid.fbx') }
if (Get-ChildItem -LiteralPath $work -Recurse -Force -Filter '.DollView-convert-*') { throw 'Staging directory left behind.' }
foreach ($file in $original.Keys) {
    if ((Get-FileHash -LiteralPath $file).Hash -ne $original[$file]) { throw "Changed source: $file" }
}
Write-Output 'PASS all 20 directions, re-import, clip timing, source preservation and sidecar collisions'
