param([string]$Destination)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if (!$Destination) { $Destination=Join-Path $root ('dist\github-source-'+[Guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $Destination) { throw 'Choose an empty new source export directory.' }
New-Item -ItemType Directory -Path $Destination | Out-Null
$sourceNames=@('LumaView','ExtendedDecoder','Workspace','BrowserWorkspace','ShellIcons','Filmstrip','ImageDetails','VideoViewer','MpvPlayer','FullscreenChrome','FileCommands','SequenceViewer','AppSettings','FileAssociations','ModelViewer','AppUpdater','ComponentSetup','ModernUi','UpdateFiles','UpdateWorker')
foreach ($name in $sourceNames) { Copy-Item -LiteralPath (Join-Path $root "$name.cs") -Destination $Destination }
foreach ($name in @('build.ps1','app.manifest','VERSION','.gitignore','README.md','RELEASE_NOTES.md','THIRD-PARTY-NOTICES.txt','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination $Destination }
foreach ($name in @('.github','scripts','tests','installer','assets','codecs','licenses')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination $Destination -Recurse }
& (Join-Path $PSScriptRoot 'copy-model-runtime.ps1') -Source (Join-Path $root 'model-viewer') -Destination (Join-Path $Destination 'model-viewer')
New-Item -ItemType Directory -Force -Path (Join-Path $Destination 'video-runtime') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'video-runtime\SOURCE.json') -Destination (Join-Path $Destination 'video-runtime')
Write-Output "GitHub source prepared: $Destination"
