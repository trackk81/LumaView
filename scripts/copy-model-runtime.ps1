param([Parameter(Mandatory=$true)][string]$Source, [Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference='Stop'
$sourceRoot = [IO.Path]::GetFullPath($Source).TrimEnd('\')
$destinationRoot = [IO.Path]::GetFullPath($Destination)
$seen = @{}
function Copy-Module([string]$relative) {
    $file = [IO.Path]::GetFullPath((Join-Path $sourceRoot $relative))
    if (!$file.StartsWith($sourceRoot+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Module escaped runtime root' }
    $relative = $file.Substring($sourceRoot.Length+1)
    if ($seen.ContainsKey($relative)) { return }
    $seen[$relative]=$true
    $target = Join-Path $destinationRoot $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
    Copy-Item -LiteralPath $file -Destination $target
    $sourceText=Get-Content -LiteralPath $file -Raw
    foreach ($match in [regex]::Matches($sourceText,'(?:from\s*|import\s*)["'']([^"'']+)["'']')) {
        $name=$match.Groups[1].Value
        if ($name -eq 'three') { Copy-Module 'vendor\build\three.module.js' }
        elseif ($name.StartsWith('three/addons/')) { Copy-Module ('vendor\addons\'+$name.Substring(13)) }
        elseif ($name.StartsWith('.')) {
            $parent = Split-Path $relative -Parent
            if ([string]::IsNullOrEmpty($parent)) { Copy-Module $name }
            else { Copy-Module (Join-Path $parent $name) }
        }
    }
}
Copy-Module 'viewer.js'
foreach ($name in @('index.html','THREE-LICENSE.txt','WEBVIEW2-LICENSE.txt','WEBVIEW2-NOTICE.txt')) { Copy-Item -LiteralPath (Join-Path $sourceRoot $name) -Destination $destinationRoot }
foreach ($name in @('environments','vendor\addons\libs\basis','vendor\addons\libs\draco\gltf')) {
    $target=Join-Path $destinationRoot $name
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $sourceRoot $name) -File | Copy-Item -Destination $target
}
Write-Output "Copied $($seen.Count) model modules and required decoders."
