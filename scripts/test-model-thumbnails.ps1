param([Parameter(Mandatory=$true)][string]$AppDirectory)
$ErrorActionPreference='Stop'
$folder=(Resolve-Path -LiteralPath $AppDirectory).Path
$exe=Join-Path $folder 'DollView.exe'
$converter=Join-Path $folder 'model-converter\DollView.Convert.exe'
$classes='HKCU:\Software\Classes'
$modelClass='{4B64E151-E9B5-409A-A8F7-02D07D6D1BDD}'
$gltfClass='{4B64E152-E9B5-409A-A8F7-02D07D6D1BDD}'
if((Test-Path "$classes\CLSID\$modelClass") -or (Test-Path "$classes\CLSID\$gltfClass")){
    throw 'Use a test profile without an active DollView 3D thumbnail registration.'
}
$inputs=New-Item -ItemType Directory -Path (Join-Path $env:TEMP ('DollView-3D-'+[guid]::NewGuid().ToString('N')))
$seed=Join-Path $inputs.FullName 'offset.obj'
@'
v 10000 10000 10000
v 10005 10000 10000
v 10005 10003 10000
v 10000 10000 10002
f 1 2 3
f 3 2 4
f 1 3 4
f 1 4 2
'@ | Set-Content -LiteralPath $seed -Encoding ASCII
$formats='obj','fbx','glb','stl','gltf'
foreach($format in $formats | Where-Object {$_ -ne 'obj'}){
    & $converter $seed (Join-Path $inputs.FullName "offset.$format")
    if($LASTEXITCODE){throw "Fixture conversion failed: $format"}
}
if(!(Get-ChildItem $inputs.FullName -Filter '*.bin')){throw 'Expected an external glTF buffer'}
$broken=New-Item -ItemType Directory -Path (Join-Path $inputs.FullName 'missing-buffer')
Copy-Item -LiteralPath (Join-Path $inputs.FullName 'offset.gltf') -Destination $broken.FullName
function Set-Models([string]$option){
    $p=Start-Process -FilePath $exe -ArgumentList $option -WindowStyle Hidden -Wait -PassThru
    if($p.ExitCode){throw "Registration failed: $option"}
}
function Read-Default([string]$path){
    $key=Get-Item -LiteralPath $path -ErrorAction SilentlyContinue
    if($key){try{return $key.GetValue('')}finally{$key.Dispose()}}
}
$before=@{};$choice=@{}
foreach($ext in $formats){
    foreach($name in ".$ext","SystemFileAssociations\.$ext"){
        $path="$classes\$name\ShellEx\{E357FCCD-A995-4576-B01F-234630154E96}"
        $before[$path]=Read-Default $path
    }
    $choice[$ext]=(Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.$ext\UserChoice" -ErrorAction SilentlyContinue).ProgId
}
try{
    Set-Models '--enable-model-thumbnails'
    Set-Models '--enable-model-thumbnails'
    Add-Type -AssemblyName System.Drawing
    $assembly=[Reflection.Assembly]::LoadFrom($exe)
    $shell=$assembly.GetType('LumaView.MediaFiles').GetMethod('ReadShellThumbnail',[Reflection.BindingFlags]'NonPublic,Static')
    $gallery=$assembly.GetType('LumaView.ModelFiles').GetMethod('RenderThumbnail',[Reflection.BindingFlags]'NonPublic,Static')
    foreach($ext in $formats){
        $file=[IO.Path]::Combine($inputs.FullName,"offset.$ext")
        $bitmap=$shell.Invoke($null,[object[]]@($file))
        if(!$bitmap){throw "Missing Explorer thumbnail for $ext"}
        try{
            $side=$bitmap.Width
            if($bitmap.Height -ne $side -or $side -le 16){throw "Unexpected thumbnail size for $ext"}
            $expected=$gallery.Invoke($null,[object[]]@($file,$side))
            if(!$expected){throw "Gallery could not render $ext"}
            try{
                $matches=0;$background=$bitmap.GetPixel(0,0).ToArgb()
                for($y=2;$y -lt $side;$y+=5){for($x=2;$x -lt $side;$x+=5){
                    $sample=$expected.GetPixel($x,$y)
                    if(!$sample.A){continue}
                    $actual=$bitmap.GetPixel($x,$y)
                    if($actual.ToArgb() -eq $background -or [Math]::Abs([int]$actual.R-[int]$sample.R) -gt 8){throw "Geometry mismatch for $ext"}
                    $matches++
                }}
                if($matches -lt ($side*$side/25)*0.12){throw "Small or empty silhouette for $ext"}
                Write-Output "PASS $ext geometry, centering, and Explorer/gallery agreement"
            }finally{$expected.Dispose()}
        }finally{$bitmap.Dispose()}
    }
    $path=[IO.Path]::Combine($broken.FullName,'offset.gltf')
    if($gallery.Invoke($null,[object[]]@($path,120))){throw 'Expected a missing-buffer failure'}
}finally{Set-Models '--disable-model-thumbnails'}
foreach($path in $before.Keys){if((Read-Default $path) -cne $before[$path]){throw "Previous handler not restored: $path"}}
foreach($ext in $formats){
    $after=(Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.$ext\UserChoice" -ErrorAction SilentlyContinue).ProgId
    if($choice[$ext] -cne $after){throw "Default app changed: $ext"}
}
Write-Output 'PASS missing external geometry and registration cleanup'
