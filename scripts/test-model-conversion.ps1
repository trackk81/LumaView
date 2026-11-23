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
$sourceDir=New-Item -ItemType Directory -Path (Join-Path $work 'package\source') -Force
$textureDir=New-Item -ItemType Directory -Path (Join-Path $work 'package\textures') -Force
$external=Join-Path $sourceDir.FullName 'wing.fbx'
Copy-Item -LiteralPath (Join-Path $root 'tests\fixtures\external-texture.fbx') -Destination $external
$wing=Join-Path $work 'wing.glb'
function Read-Glb($file) {
    $bytes=[IO.File]::ReadAllBytes($file)
    [Text.Encoding]::UTF8.GetString($bytes,20,[BitConverter]::ToInt32($bytes,12)) | ConvertFrom-Json
}
$missing=Join-Path $work 'missing.glb'
$omitted=@(Convert-Model $external $missing)
$doc=Read-Glb $missing
if($omitted.Count -ne 1 -or $omitted[0] -ne 'wing.tga' -or !$doc.meshes.Count -or $doc.images.Count -or
   $doc.materials[0].pbrMetallicRoughness.baseColorTexture){throw 'Missing map should omit only its bindings and report the name once.'}
$png='iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAAEUlEQVR4nGP4z8DAwPBf4T8ADD0DHqmHNC8AAAAASUVORK5CYII='
$export=Join-Path $textureDir.FullName 'wing.tga.png'
[IO.File]::WriteAllBytes($export,[Convert]::FromBase64String($png))
if(@(Convert-Model $external $wing).Count){throw 'Unexpected warning for a resolved texture.'}
$bytes=[IO.File]::ReadAllBytes($wing)
$jsonLength=[BitConverter]::ToInt32($bytes,12)
$doc=[Text.Encoding]::UTF8.GetString($bytes,20,$jsonLength) | ConvertFrom-Json
$mat=$doc.materials[0]
if($null -eq $mat.pbrMetallicRoughness.baseColorTexture -or $mat.alphaMode -ne 'BLEND'){throw 'Lost FBX color or opacity binding.'}
$image=$doc.images[$doc.textures[$mat.pbrMetallicRoughness.baseColorTexture.index].source]
$view=$doc.bufferViews[$image.bufferView]
if($image.mimeType -ne 'image/png' -or $null -eq $image.bufferView -or $image.uri -or
   [Convert]::ToBase64String($bytes,(28+$jsonLength+$view.byteOffset),$view.byteLength) -ne $png){throw 'GLB did not embed the original exported PNG.'}
$other=Join-Path $textureDir.FullName 'wing.png'
Copy-Item -LiteralPath $export -Destination $other
$ambiguous=Join-Path $work 'ambiguous.glb'
$omitted=@(Convert-Model $external $ambiguous)
if($omitted.Count -ne 1 -or $omitted[0] -ne 'wing.tga' -or (Read-Glb $ambiguous).images.Count){throw 'Ambiguous images should be skipped.'}
Write-Output 'PASS FBX keeps geometry with missing or ambiguous maps and embeds available maps with alpha'

$mixed=Join-Path $sourceDir.FullName 'diffuse.obj'
Copy-Item -LiteralPath (Join-Path $root 'tests\fixtures\diffuse.obj') -Destination $mixed
$mtl=Join-Path $sourceDir.FullName 'diffuse.mtl'
Copy-Item -LiteralPath (Join-Path $root 'tests\fixtures\diffuse.mtl') -Destination $mtl
Add-Content -LiteralPath $mtl -Encoding Ascii -Value @('map_Kd color.png','map_Ks LM_Final.tga','bump broken.tga')
[IO.File]::WriteAllBytes((Join-Path $sourceDir.FullName 'broken.tga'),[byte[]](1,2,3))
[IO.File]::WriteAllBytes((Join-Path $textureDir.FullName 'color.png'),[Convert]::FromBase64String($png))
$output=Join-Path $work 'mixed.glb'
$omitted=@(Convert-Model $mixed $output)
$doc=Read-Glb $output
if($omitted.Count -ne 2 -or $omitted -notcontains 'LM_Final.tga' -or $omitted -notcontains 'broken.tga' -or
   $doc.images.Count -ne 1 -or !$doc.meshes.Count -or !($doc.materials | Where-Object { $null -ne $_.pbrMetallicRoughness.baseColorTexture })){
    throw 'OBJ must preserve its valid color texture and identify missing and unreadable maps.'
}
Write-Output 'PASS OBJ keeps the valid map when an unrelated map is missing or cannot be decoded'

$partial=Get-Content -LiteralPath $seed -Raw -Encoding UTF8 | ConvertFrom-Json
$partial | Add-Member -NotePropertyName images -NotePropertyValue @(@{uri='not-supplied.png'})
$partial | Add-Member -NotePropertyName textures -NotePropertyValue @(@{source=0})
$partial.materials[0].pbrMetallicRoughness | Add-Member -NotePropertyName baseColorTexture -NotePropertyValue @{index=0}
$gltfFile=Join-Path $sourceDir.FullName 'partial.gltf'
[IO.File]::WriteAllText($gltfFile,($partial | ConvertTo-Json -Depth 24))
foreach($format in 'glb','fbx','obj'){
    $file=Join-Path $work ("partial."+$format)
    $omitted=@(Convert-Model $gltfFile $file)
    if($omitted.Count -ne 1 -or $omitted[0] -ne 'not-supplied.png'){throw "Missing glTF texture was not reported for $format"}
    $probe=Join-Path $work ("reopen-partial-"+$format+".gltf")
    if(@(Convert-Model $file $probe).Count){throw "Dangling texture in $format"}
    $doc=Get-Content -LiteralPath $probe -Raw -Encoding UTF8 | ConvertFrom-Json
    if(!$doc.meshes.Count -or $doc.images.Count -or ($format -ne 'obj' -and $doc.animations.Count -ne 2)){throw "Incomplete $format after skipping a glTF texture"}
}
Write-Output 'PASS animated glTF with an absent image exports and reopens as GLB, FBX and OBJ'

$setName='finish_'+[Guid]::NewGuid().ToString('N')
$asset=New-Item -ItemType Directory (Join-Path $work 'autofind\source') -Force
$maps=New-Item -ItemType Directory (Join-Path $work 'autofind\paint library\surfaces\cloth') -Force
$fbx=Join-Path $asset.FullName 'untitled.fbx'
$sourceText=Get-Content -LiteralPath (Join-Path $root 'tests\fixtures\external-texture.fbx') -Raw
[IO.File]::WriteAllText($fbx,$sourceText.Replace('wing.tga',($setName+'_Mixed_AO.tga')).Replace('    C: "OP",4,3,"TransparentColor"',''))
foreach($suffix in 'Mixed_AO','Base_Color_v1','Base_Color_v4','Normal_OpenGL','Roughness','Metallic','Emissive'){
    [IO.File]::WriteAllBytes((Join-Path $maps.FullName ($setName+'_'+$suffix+'.png')),[Convert]::FromBase64String($png))
}
$output=Join-Path $work 'inferred.glb'
if(@(Convert-Model $fbx $output).Count){throw 'Failed to resolve a relocated image by its stem.'}
$mat=(Read-Glb $output).materials[0]
if(!$mat.normalTexture -or !$mat.emissiveTexture -or !$mat.occlusionTexture -or !$mat.pbrMetallicRoughness.metallicRoughnessTexture -or
   $mat.pbrMetallicRoughness.baseColorTexture.index -eq $mat.occlusionTexture.index){throw 'Incomplete inferred PBR bindings.'}
$preview=Join-Path $work 'preview.glb'
& (Join-Path $AppDirectory 'model-converter\DollView.Convert.exe') --texture-preview $fbx $preview
if($LASTEXITCODE -ne 0 -or !(Read-Glb $preview).materials[0].normalTexture){throw 'Viewer preview failed to recover nested textures.'}
$unnamed=Get-Content -LiteralPath (Join-Path $root 'tests\fixtures\opacity.gltf') -Raw | ConvertFrom-Json
$unnamed.materials=@($unnamed.materials[0]);$unnamed.meshes[0].primitives=@($unnamed.meshes[0].primitives[0])
$unnamed.materials[0].name=$setName
$unnamed.materials[0].pbrMetallicRoughness.PSObject.Properties.Remove('baseColorTexture')
$unnamed.PSObject.Properties.Remove('images');$unnamed.PSObject.Properties.Remove('textures')
$gltf=Join-Path $asset.FullName 'no-references.gltf';[IO.File]::WriteAllText($gltf,($unnamed | ConvertTo-Json -Depth 32))
$output=Join-Path $work 'no-references.glb'
if(@(Convert-Model $gltf $output).Count -or !(Read-Glb $output).materials[0].pbrMetallicRoughness.baseColorTexture){throw 'Missing inference for a material without any image references.'}
Write-Output 'PASS nested PBR sets for arbitrary model names, misplaced AO and materials with no image references'
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
