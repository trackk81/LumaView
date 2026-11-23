$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$version=(Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
$zipPath=Join-Path $root "dist\LumaView-$version-win-x64.zip"
$sumLine=Get-Content -LiteralPath (Join-Path $root 'dist\SHA256SUMS.txt') | Where-Object { $_ -match ('  '+[regex]::Escape([IO.Path]::GetFileName($zipPath))+'$') }
if (!$sumLine -or (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash -ine ($sumLine -split '\s+',2)[0]) { throw 'Portable checksum mismatch' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $names=@($archive.Entries | ForEach-Object { $_.FullName.Replace('\','/') })
    foreach ($name in $names) { if ($name.StartsWith('/') -or $name.Contains(':') -or $name -match '(^|/)\.\.(/|$)' -or $name -match '(?i)(setup.*\.exe$|Check\.exe$|before-.*\.exe$|^evidence/)') { throw "Unexpected archive entry: $name" } }
    foreach ($required in @('DollView.exe','DollView.Updater.exe','DollView.Thumbnail.dll','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll','assets/LumaView.ico','assets/DollView-3D.ico','codecs/magick.exe','model-converter/DollView.Convert.exe','model-viewer/index.html','model-viewer/viewer.js','model-viewer/vendor/build/three.module.js','README.md','VERSION','model-viewer/effects.js','model-viewer/materials.js','model-viewer/animation.js','model-viewer/environment.js','model-viewer/environments/studio_small_09.hdr','model-viewer/environments/studio_small_03.hdr','model-viewer/environments/venice_sunset.hdr','model-viewer/environments/forest_slope.hdr','video-runtime/mpv.exe','video-runtime/d3dcompiler_43.dll','video-runtime/SOURCE.json','licenses/mpv-LICENSE.GPL.txt')) { if ($names -notcontains $required) { throw "Missing archive entry: $required" } }
    if (@($names | Select-Object -Unique).Count -ne $names.Count) { throw 'Duplicate archive entries' }
} finally { $archive.Dispose() }
$extractRoot=Join-Path $root ('dist\portable-check-'+[Guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $zipPath -DestinationPath $extractRoot
$info=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $extractRoot 'DollView.exe'))
if ($info.FileVersion -ne "$version.0") { throw 'Portable executable version mismatch' }
$runtime=Get-Content -LiteralPath (Join-Path $extractRoot 'video-runtime\SOURCE.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath (Join-Path $extractRoot 'video-runtime\mpv.exe') -Algorithm SHA256).Hash -ine $runtime.binary_sha256) { throw 'Video runtime hash mismatch' }
$extractRoot | Set-Content -LiteralPath (Join-Path $root 'dist\last-portable-check.txt') -Encoding UTF8
Write-Output "PASS portable ZIP checksum, required runtime files, safe archive paths, extraction and version $version"
