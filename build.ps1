param([string]$OutputDirectory = $PSScriptRoot, [ValidateSet('winexe','exe')][string]$Target = 'winexe', [switch]$IncludeLegacyLauncher)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VERSION') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'VERSION must contain major.minor.patch' }
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$versionFile = Join-Path $outputRoot 'AssemblyInfo.generated.cs'
@"
using System.Reflection;
[assembly: AssemblyTitle("DollView")]
[assembly: AssemblyProduct("DollView")]
[assembly: AssemblyDescription("Image, sequence, video and 3D model viewer")]
[assembly: AssemblyVersion("$version.0")]
[assembly: AssemblyFileVersion("$version.0")]
"@ | Set-Content -LiteralPath $versionFile -Encoding UTF8
$references = @('System.Windows.Forms.dll','System.Drawing.dll','System.Xaml.dll','System.Web.Extensions.dll','System.Net.Http.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll')
foreach ($name in @('WindowsBase','PresentationCore','PresentationFramework','WindowsFormsIntegration')) {
    $references += Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\WPF\$name.dll"
}
$references += Join-Path $PSScriptRoot 'Microsoft.Web.WebView2.Core.dll'
$references += Join-Path $PSScriptRoot 'Microsoft.Web.WebView2.WinForms.dll'
$names = @('LumaView','ExtendedDecoder','ExrAov','Workspace','BrowserWorkspace','ShellIcons','Filmstrip','ImageDetails','VideoViewer','MpvPlayer','FullscreenChrome','FileCommands','SequenceViewer','Shortcuts','AppSettings','FileAssociations','ModelViewer','AppUpdater','ComponentSetup','ModernUi','GpuCanvas','UpdateFiles')
$arguments = @('/nologo',"/target:$Target",'/platform:x64','/optimize+',"/out:$outputRoot\DollView.exe","/win32manifest:$PSScriptRoot\app.manifest","/win32icon:$PSScriptRoot\assets\LumaView.ico")
$arguments += $references | ForEach-Object { "/reference:$_" }
$arguments += $names | ForEach-Object { Join-Path $PSScriptRoot "$_.cs" }
$arguments += $versionFile
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Output "Built DollView $version -> $outputRoot\DollView.exe"

$workerArguments=@('/nologo','/target:winexe','/platform:x64','/optimize+',"/out:$outputRoot\DollView.Updater.exe","/win32manifest:$PSScriptRoot\app.manifest","/win32icon:$PSScriptRoot\assets\LumaView.ico",'/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll','/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll',(Join-Path $PSScriptRoot 'UpdateFiles.cs'),(Join-Path $PSScriptRoot 'UpdateWorker.cs'),$versionFile)
& $compiler @workerArguments
if ($LASTEXITCODE -ne 0) { throw 'Update worker build failed' }
if ($IncludeLegacyLauncher) {
    & $compiler /nologo /target:winexe /platform:x64 "/out:$outputRoot\LumaView.exe" "/win32manifest:$PSScriptRoot\app.manifest" "/win32icon:$PSScriptRoot\assets\LumaView.ico" /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll (Join-Path $PSScriptRoot 'LegacyLauncher.cs') (Join-Path $PSScriptRoot 'UpdateFiles.cs') $versionFile
    if ($LASTEXITCODE -ne 0) { throw 'Legacy launcher build failed' }
    Copy-Item -LiteralPath (Join-Path $outputRoot 'DollView.Updater.exe') -Destination (Join-Path $outputRoot 'LumaView.Updater.exe')
}
