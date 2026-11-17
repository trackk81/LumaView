param()
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$output=Join-Path $root ('dist\updater-checks-'+[Guid]::NewGuid().ToString('N'))
& (Join-Path $root 'build.ps1') -OutputDirectory $output
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
foreach ($fixture in @('old','new','fail')) {
    $argsList=@('/nologo','/target:exe','/platform:x64',"/out:$output\$fixture.exe",(Join-Path $root 'tests\UpdateFixture.cs'))
    if ($fixture -ne 'new') { $argsList+=('/define:'+$fixture.ToUpperInvariant()) }
    & $compiler @argsList
    if ($LASTEXITCODE -ne 0) { throw 'Fixture build failed' }
}
& $compiler /nologo /target:exe /platform:x64 "/out:$output\UpdateChecks.exe" /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll (Join-Path $root 'UpdateFiles.cs') (Join-Path $root 'tests\UpdateChecks.cs')
if ($LASTEXITCODE -ne 0) { throw 'Updater tests build failed' }
& (Join-Path $output 'UpdateChecks.exe') ([IO.Path]::GetTempPath()) $output (Join-Path $output 'LumaView.Updater.exe')
if ($LASTEXITCODE -ne 0) { throw 'Automatic updater checks failed' }
