param([Parameter(Mandatory=$true)][string]$SetupPath)
$ErrorActionPreference='Stop'
if (Test-Path -LiteralPath 'Registry::HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\LumaView.Desktop_is1') { throw 'Run installer smoke tests in a clean Windows user profile; LumaView is already installed.' }
$root=Split-Path $PSScriptRoot -Parent
$runRoot=Join-Path $root ('dist\install-test-'+[Guid]::NewGuid().ToString('N'))
$installRoot=Join-Path $env:LOCALAPPDATA ('LumaView-install-test-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot | Out-Null
$setupFull=(Resolve-Path -LiteralPath $SetupPath).Path
$extensions='jpg,jpeg,png,bmp,gif,tif,tiff,ico,webp,exr,psd,psb,tga,hdr,dpx,dds,mp4,m4v,mov,wmv,avi,mpg,mpeg,mkv,webm,obj,glb,gltf,stl,fbx'.Split(',')
# Snapshot only LumaView-owned registration trees. Never touch FileExts/UserChoice.
$trees=@('Software\LumaView\Capabilities','Software\LumaView\SelectedCapabilities','Software\Classes\Applications\DollView.exe')
$trees += $extensions | ForEach-Object { 'Software\Classes\LumaView.File.'+$_ }
$backups=@{}; $values=@{}
foreach ($tree in $trees) {
    if (Test-Path -LiteralPath ('Registry::HKEY_CURRENT_USER\'+$tree)) {
        $file=Join-Path $runRoot (([Guid]::NewGuid().ToString('N'))+'.reg')
        & reg.exe export ('HKCU\'+$tree) $file /y | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Registry snapshot failed' }
        $backups[$tree]=$file
    }
}
$valuePaths=@(@{Path='Software\RegisteredApplications';Name='LumaView'},@{Path='Software\RegisteredApplications';Name='LumaView.SelectedTypes'})
$valuePaths += $extensions | ForEach-Object { @{Path=('Software\Classes\.'+$_+'\OpenWithProgids');Name=('LumaView.File.'+$_)} }
foreach ($item in $valuePaths) {
    $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($item.Path)
    if ($key) { try { if ($key.GetValueNames() -contains $item.Name) { $values[$item.Path+'|'+$item.Name]=@{Value=$key.GetValue($item.Name);Kind=$key.GetValueKind($item.Name)} } } finally { $key.Dispose() } }
}
try {
    $process=Start-Process -FilePath $setupFull -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/TASKS="assoc_jpg,assoc_png"',('/DIR="'+$installRoot+'"'),('/LOG="'+(Join-Path $runRoot 'install.log')+'"')) -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Setup failed: $($process.ExitCode)" }
    foreach ($file in @('DollView.exe','unins000.exe','codecs\magick.exe','model-viewer\viewer.js','Microsoft.Web.WebView2.Core.dll')) { if (!(Test-Path -LiteralPath (Join-Path $installRoot $file))) { throw "Missing payload: $file" } }
    $version=(Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip=[IO.Compression.ZipFile]::OpenRead((Join-Path $root "dist\DollView-$version-win-x64.zip"))
    try {
        foreach($entry in $zip.Entries) {
            if (!$entry.Name) { continue }
            $installed=Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $installRoot $entry.FullName)
            $stream=$entry.Open()
            try { $expected=Get-FileHash -Algorithm SHA256 -InputStream $stream } finally { $stream.Dispose() }
            if ($installed.Hash -ne $expected.Hash) { throw "Installer and ZIP differ: $($entry.FullName)" }
        }
    } finally { $zip.Dispose() }
    Write-Output 'PASS installed payload matches the portable ZIP'
    $command=(Get-ItemProperty -LiteralPath 'Registry::HKEY_CURRENT_USER\Software\Classes\LumaView.File.jpg\shell\open\command').'(default)'
    if ($command -ne ('"'+$installRoot+'\DollView.exe" "%1"')) { throw 'Installed association command mismatch' }
    $selected=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\LumaView\SelectedCapabilities\FileAssociations')
    try { if (($selected.GetValueNames() | Sort-Object) -join ',' -ne '.jpg,.png') { throw 'Selected extension filter mismatch' } } finally { $selected.Dispose() }
    Write-Output 'PASS silent installation, required payload, association command and selected defaults profile'
    # Upgrade in place, without launching the app or Windows settings.
    $process=Start-Process -FilePath $setupFull -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/TASKS="assoc_jpg,assoc_png"',('/DIR="'+$installRoot+'"')) -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw 'Upgrade installation failed' }
    Write-Output 'PASS in-place reinstall'
    $uninstall=Start-Process -FilePath (Join-Path $installRoot 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -Wait -PassThru
    if ($uninstall.ExitCode -ne 0 -or (Test-Path -LiteralPath (Join-Path $installRoot 'DollView.exe'))) { throw 'Uninstall failed' }
    if (Test-Path -LiteralPath 'Registry::HKEY_CURRENT_USER\Software\Classes\LumaView.File.jpg') { throw 'Registration cleanup failed' }
    Write-Output 'PASS uninstall and registration cleanup'
} finally {
    if (Test-Path -LiteralPath (Join-Path $installRoot 'unins000.exe')) { Start-Process -FilePath (Join-Path $installRoot 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -Wait }
    foreach ($tree in $trees) { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($tree,$false) }
    foreach ($backup in $backups.Values) { & reg.exe import $backup | Out-Null; if ($LASTEXITCODE -ne 0) { Write-Warning "Restore failed: $backup" } }
    foreach ($item in $valuePaths) {
        $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($item.Path)
        try { $saved=$values[$item.Path+'|'+$item.Name]; if ($saved) { $key.SetValue($item.Name,$saved.Value,$saved.Kind) } else { $key.DeleteValue($item.Name,$false) } } finally { $key.Dispose() }
    }
    Write-Output 'Restored previous LumaView registration; Windows UserChoice was not edited.'
}
