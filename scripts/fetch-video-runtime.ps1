$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$runtime=Join-Path $root 'video-runtime'
$source=Get-Content -LiteralPath (Join-Path $runtime 'SOURCE.json') -Raw | ConvertFrom-Json
if (Test-Path -LiteralPath (Join-Path $runtime 'mpv.exe')) {
    if ((Get-FileHash -LiteralPath (Join-Path $runtime 'mpv.exe') -Algorithm SHA256).Hash -ieq $source.binary_sha256 -and (Test-Path -LiteralPath (Join-Path $runtime 'mpv\fonts.conf')) -and (Test-Path -LiteralPath (Join-Path $runtime 'd3dcompiler_43.dll'))) { Write-Output 'Pinned video runtime already present.'; return }
}
$work=Join-Path $root ('dist\video-download-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work | Out-Null
$archive=Join-Path $work 'mpv.7z'
Invoke-WebRequest -Uri $source.asset -OutFile $archive
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ine $source.asset_sha256) { throw 'Downloaded video runtime checksum mismatch' }
$sevenZip=(Get-Command 7z -ErrorAction SilentlyContinue).Source
if (!$sevenZip) { $candidate=Join-Path $env:ProgramFiles '7-Zip\7z.exe'; if (Test-Path -LiteralPath $candidate) { $sevenZip=$candidate } }
if ($sevenZip) { & $sevenZip x "-o$work" -y $archive | Out-Null }
else { & tar -xf $archive -C $work }
if ($LASTEXITCODE -ne 0) { throw 'Unable to extract the video runtime' }
New-Item -ItemType Directory -Force -Path (Join-Path $runtime 'mpv') | Out-Null
foreach ($name in @('mpv.exe','d3dcompiler_43.dll','mpv\fonts.conf')) { Copy-Item -LiteralPath (Join-Path $work $name) -Destination (Join-Path $runtime $name) -Force }
if ((Get-FileHash -LiteralPath (Join-Path $runtime 'mpv.exe') -Algorithm SHA256).Hash -ine $source.binary_sha256) { throw 'Extracted video runtime checksum mismatch' }
Write-Output 'Downloaded and verified pinned mpv/FFmpeg video runtime.'
