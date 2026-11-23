$ErrorActionPreference='Stop'
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$tools=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$tools){throw 'Visual Studio x64 C++ build tools are required to rebuild the thumbnail provider.'}
$root=Split-Path $PSScriptRoot -Parent
$build=Join-Path $root ('dist\thumbnail-build-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $build | Out-Null
$dev=Join-Path $tools 'Common7\Tools\VsDevCmd.bat'
$src=Join-Path $PSScriptRoot 'provider.cpp'
$exports=Join-Path $PSScriptRoot 'provider.def'
Push-Location $build
try{
    $command='call "'+$dev+'" -no_logo -arch=x64 -host_arch=x64 && cl.exe /nologo /std:c++17 /O2 /W4 /EHsc /MT /LD /DUNICODE /D_UNICODE "'+$src+'" /link /Brepro /DEF:"'+$exports+'" /OUT:DollView.Thumbnail.dll ole32.lib uuid.lib gdi32.lib'
    & $env:ComSpec /d /c $command
    if($LASTEXITCODE -ne 0){throw 'Thumbnail provider build failed'}
    Copy-Item -LiteralPath (Join-Path $build 'DollView.Thumbnail.dll') -Destination $PSScriptRoot
}finally{Pop-Location}
