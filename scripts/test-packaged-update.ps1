param([string]$PreviousPackage)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$version=(Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
$checkRoot=Join-Path ([IO.Path]::GetTempPath()) ('LumaView-package-upgrade-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $checkRoot | Out-Null
if (!$PreviousPackage) {
    $PreviousPackage=Join-Path $checkRoot 'LumaView-0.2.6-win-x64.zip'
    Invoke-WebRequest -Uri 'https://github.com/trackk81/LumaView/releases/download/v0.2.6/LumaView-0.2.6-win-x64.zip' -OutFile $PreviousPackage
}
if ((Get-FileHash -LiteralPath $PreviousPackage -Algorithm SHA256).Hash -ine '3a2978578b5b1c1a4be0910a02f69d38bf79abda1e31d7b7f41065086700a7f1') { throw 'Previous release hash mismatch' }
$target=Join-Path $checkRoot 'app'
Expand-Archive -LiteralPath $PreviousPackage -DestinationPath $target
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x64 "/out:$checkRoot\PackagedUpdateCheck.exe" /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll (Join-Path $root 'UpdateFiles.cs') (Join-Path $root 'tests\PackagedUpdateCheck.cs')
if ($LASTEXITCODE -ne 0) { throw 'Packaged updater test compilation failed' }
& (Join-Path $checkRoot 'PackagedUpdateCheck.exe') $target (Join-Path $root "dist\LumaView-$version-win-x64.zip") (Join-Path $checkRoot 'work') $version
if ($LASTEXITCODE -ne 0) { throw "Packaged application upgrade failed: $checkRoot" }

$modern=Join-Path $checkRoot 'DollView-0.2.20-win-x64.zip'
Invoke-WebRequest -Uri 'https://github.com/trackk81/LumaView/releases/download/v0.2.20/DollView-0.2.20-win-x64.zip' -OutFile $modern
if ((Get-FileHash -LiteralPath $modern).Hash -ine '356f6e4f8f2d62e467e33c5da8b87b9b406bdad8c2f5fcd1c370f6c271553fec') { throw 'Previous DollView ZIP hash mismatch' }
$modernTarget=Join-Path $checkRoot 'modern'
Expand-Archive -LiteralPath $modern -DestinationPath $modernTarget
& (Join-Path $checkRoot 'PackagedUpdateCheck.exe') $modernTarget (Join-Path $root "dist\DollView-$version-win-x64.zip") (Join-Path $checkRoot 'modern-work') $version
if ($LASTEXITCODE -ne 0) { throw "DollView portable upgrade failed: $checkRoot" }
