param([switch]$NoUI)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$testRoot=Join-Path $root ('dist\tests-'+[Guid]::NewGuid().ToString('N'))
& (Join-Path $root 'build.ps1') -OutputDirectory $testRoot -Target exe
$converterDir=New-Item -ItemType Directory -Path (Join-Path $testRoot 'model-converter')
Copy-Item -LiteralPath (Join-Path $root 'model-converter\DollView.Convert.exe') -Destination $converterDir.FullName
foreach ($file in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll')) { Copy-Item -LiteralPath (Join-Path $root $file) -Destination $testRoot }
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x64 "/win32manifest:$root\app.manifest" "/out:$testRoot\ReleaseChecks.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll (Join-Path $root 'tests\ReleaseChecks.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
$arguments=@();if ($NoUI) { $arguments+='--no-ui' }
Copy-Item -LiteralPath (Join-Path $root 'tests\fixtures\animated.gif') -Destination $testRoot
& (Join-Path $testRoot 'ReleaseChecks.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw 'Release checks failed' }
