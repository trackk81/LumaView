param([string]$OutputDirectory = $PSScriptRoot, [ValidateSet('winexe','exe')][string]$Target = 'winexe')
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VERSION') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'VERSION must contain major.minor.patch' }
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$versionFile = Join-Path $outputRoot 'AssemblyInfo.generated.cs'
@"
using System.Reflection;
[assembly: AssemblyTitle("LumaView")]
[assembly: AssemblyProduct("LumaView")]
[assembly: AssemblyDescription("Image, sequence, video and 3D model viewer")]
[assembly: AssemblyVersion("$version.0")]
[assembly: AssemblyFileVersion("$version.0")]
"@ | Set-Content -LiteralPath $versionFile -Encoding UTF8
$references = @('System.Windows.Forms.dll','System.Drawing.dll','System.Xaml.dll','System.Web.Extensions.dll','System.Net.Http.dll')
foreach ($name in @('WindowsBase','PresentationCore','PresentationFramework','WindowsFormsIntegration')) {
    $references += Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\WPF\$name.dll"
}
$references += Join-Path $PSScriptRoot 'Microsoft.Web.WebView2.Core.dll'
$references += Join-Path $PSScriptRoot 'Microsoft.Web.WebView2.WinForms.dll'
$names = @('LumaView','ExtendedDecoder','Workspace','BrowserWorkspace','ShellIcons','Filmstrip','ImageDetails','VideoViewer','MpvPlayer','FullscreenChrome','FileCommands','SequenceViewer','AppSettings','FileAssociations','ModelViewer','AppUpdater','ComponentSetup')
$arguments = @('/nologo',"/target:$Target",'/platform:x64','/optimize+',"/out:$outputRoot\LumaView.exe","/win32manifest:$PSScriptRoot\app.manifest","/win32icon:$PSScriptRoot\assets\LumaView.ico")
$arguments += $references | ForEach-Object { "/reference:$_" }
$arguments += $names | ForEach-Object { Join-Path $PSScriptRoot "$_.cs" }
$arguments += $versionFile
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Output "Built LumaView $version -> $outputRoot\LumaView.exe"
