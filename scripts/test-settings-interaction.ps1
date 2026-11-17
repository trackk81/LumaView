param([string]$AppPath)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$testRoot=Join-Path $root ('dist\settings-interaction-'+[Guid]::NewGuid().ToString('N'))
if (!$AppPath) {
    & (Join-Path $root 'build.ps1') -OutputDirectory $testRoot
    foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination $testRoot }
    $AppPath=Join-Path $testRoot 'LumaView.exe'
} else { New-Item -ItemType Directory -Path $testRoot | Out-Null }
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x64 "/win32manifest:$root\app.manifest" "/out:$testRoot\SettingsInteractionChecks.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll (Join-Path $root 'tests\SettingsInteractionChecks.cs')
if ($LASTEXITCODE -ne 0) { throw 'Settings interaction test compilation failed' }
& (Join-Path $testRoot 'SettingsInteractionChecks.exe') $AppPath
if ($LASTEXITCODE -ne 0) { throw 'Settings interaction checks failed' }
