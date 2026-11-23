param([Parameter(Mandatory=$true)][string]$AppDirectory)
$ErrorActionPreference='Stop'
$folder=(Resolve-Path -LiteralPath $AppDirectory).Path
$exe=Join-Path $folder 'DollView.exe'
$class='{4B64E150-E9B5-409A-A8F7-02D07D6D1BDD}'
$classes='Registry::HKEY_CURRENT_USER\Software\Classes'
if(Test-Path -LiteralPath "$classes\CLSID\$class"){throw 'Run this registration test in a profile without an active DollView thumbnail provider.'}
$formats='exr','psd','hdr','tga'
$slot='\ShellEx\{E357FCCD-A995-4576-B01F-234630154E96}'
$fixtures=Join-Path $env:TEMP ('DollView-shell-test-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtures | Out-Null
function Set-Provider([string]$option){
    $process=Start-Process -FilePath $exe -ArgumentList $option -WindowStyle Hidden -Wait -PassThru
    if($process.ExitCode -ne 0){throw "Thumbnail registration failed: $option"}
}
function Read-Value([string]$path,[string]$name){
    $key=Get-Item -LiteralPath $path -ErrorAction SilentlyContinue
    if($key){try{return $key.GetValue($name)}finally{$key.Dispose()}}
}
$before=@{};$defaults=@{}
foreach($ext in $formats){
    foreach($prefix in @('','SystemFileAssociations\')){
        $path="$classes\$prefix.$ext$slot"
        $before[$path]=Read-Value $path ''
    }
    $defaults[$ext]=Read-Value "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.$ext\UserChoice" 'ProgId'
    & (Join-Path $folder 'codecs\magick.exe') -size 128x96 gradient:red-blue (Join-Path $fixtures "gradient.$ext")
    if($LASTEXITCODE -ne 0){throw "Cannot create $ext fixture"}
}
try{
    Set-Provider '--enable-explorer-thumbnails'
    Set-Provider '--enable-explorer-thumbnails'
    foreach($path in $before.Keys){if((Read-Value $path '') -ne $class){throw "No provider at $path"}}
    Add-Type -AssemblyName System.Drawing
    $media=[Reflection.Assembly]::LoadFrom($exe).GetType('LumaView.MediaFiles')
    $thumbnail=$media.GetMethod('ReadShellThumbnail',[Reflection.BindingFlags]'Static,NonPublic')
    foreach($ext in $formats){
        $file=[IO.Path]::Combine($fixtures,"gradient.$ext")
        $bmp=$thumbnail.Invoke($null,[object[]]@($file))
        if(!$bmp){throw "Windows returned no thumbnail for .$ext"}
        try{
            if($bmp.Width -lt 32 -or $bmp.Width*3 -ne $bmp.Height*4){throw "Incorrect .$ext dimensions"}
            $x=[int]($bmp.Width/2)
            $first=$bmp.GetPixel($x,[int]($bmp.Height/8));$last=$bmp.GetPixel($x,[int]($bmp.Height*7/8))
            if([math]::Abs($first.R-$last.R)+[math]::Abs($first.B-$last.B) -lt 60){throw "Missing .$ext image content"}
            Write-Output "PASS Windows Explorer thumbnail .$ext ($($bmp.Width) x $($bmp.Height))"
        }finally{$bmp.Dispose()}
    }
}finally{Set-Provider '--disable-explorer-thumbnails'}
foreach($path in $before.Keys){if((Read-Value $path '') -cne $before[$path]){throw "Previous provider not restored: $path"}}
foreach($ext in $formats){
    if((Read-Value "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.$ext\UserChoice" 'ProgId') -cne $defaults[$ext]){throw "Default app changed for $ext"}
}
Write-Output 'PASS previous handlers and default apps preserved'
