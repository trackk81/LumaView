param([string]$InnoCompiler, [switch]$IncludeInstaller, [switch]$CleanPortable)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!(Test-Path -LiteralPath (Join-Path $root 'video-runtime\mpv.exe'))) { throw 'Missing bundled video runtime: video-runtime/mpv.exe' }
$version = (Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
$dist = Join-Path $root 'dist'
$payload = Join-Path $dist ("payload-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $payload | Out-Null
& (Join-Path $root 'build.ps1') -OutputDirectory $payload -IncludeLegacyLauncher:(!$CleanPortable)
Remove-Item -LiteralPath (Join-Path $payload 'AssemblyInfo.generated.cs')
$files = @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll','README.md','THIRD-PARTY-NOTICES.txt','VERSION')
foreach ($name in $files) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination $payload }
foreach ($name in @('assets','codecs','licenses')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination $payload -Recurse }
$videoTarget=Join-Path $payload 'video-runtime'
New-Item -ItemType Directory -Force -Path (Join-Path $videoTarget 'mpv') | Out-Null
foreach ($name in @('mpv.exe','d3dcompiler_43.dll','SOURCE.json','mpv\fonts.conf')) { Copy-Item -LiteralPath (Join-Path $root ('video-runtime\'+$name)) -Destination (Join-Path $videoTarget $name) }
& (Join-Path $PSScriptRoot 'copy-model-runtime.ps1') -Source (Join-Path $root 'model-viewer') -Destination (Join-Path $payload 'model-viewer')
$archiveName=if($CleanPortable){"DollView-$version-win-x64.zip"}else{"LumaView-$version-win-x64.zip"}
$portable=Join-Path $dist $archiveName
Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $portable -Force
if ($IncludeInstaller) {
    if (!$InnoCompiler) {
        $candidates = @((Join-Path $root 'dependencies\inno\ISCC.exe'), "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 7\ISCC.exe")
        $InnoCompiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
    if (!$InnoCompiler) { throw 'Install Inno Setup 6.5+ or pass -InnoCompiler.' }
    $bootstrap = Join-Path $root 'installer\redist\MicrosoftEdgeWebview2Setup.exe'
    $sig = Get-AuthenticodeSignature -LiteralPath $bootstrap
    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'Microsoft Corporation') { throw 'Invalid Microsoft WebView2 bootstrapper signature.' }
    & $InnoCompiler "/DAppVersion=$version" "/DPayloadDir=$payload" "/DOutputPath=$dist" (Join-Path $root 'installer\LumaView.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }
}
$releaseFiles = @($portable)
if ($IncludeInstaller) { $releaseFiles += Join-Path $dist "DollView-Setup-$version-x64.exe" }
$checksumName=if($CleanPortable){'DollView-SHA256SUMS.txt'}else{'SHA256SUMS.txt'}
$releaseFiles | ForEach-Object { $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256; $hash.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($_) } | Set-Content -LiteralPath (Join-Path $dist $checksumName) -Encoding ASCII
$payload | Set-Content -LiteralPath (Join-Path $dist 'latest-payload.txt') -Encoding UTF8
Write-Output "Release files: $dist"
