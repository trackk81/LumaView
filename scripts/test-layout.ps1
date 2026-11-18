$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$testRoot=Join-Path $root ('dist\layout-tests-'+[Guid]::NewGuid().ToString('N'))
& (Join-Path $root 'build.ps1') -OutputDirectory $testRoot
foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination $testRoot }
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x64 "/win32manifest:$root\app.manifest" "/out:$testRoot\ModernLayoutChecks.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll (Join-Path $root 'tests\ModernLayoutChecks.cs')
if($LASTEXITCODE -ne 0) { throw 'Layout check compilation failed' }
& (Join-Path $testRoot 'ModernLayoutChecks.exe') (Join-Path $testRoot 'DollView.exe')
if($LASTEXITCODE -ne 0) { throw 'Layout checks failed' }
