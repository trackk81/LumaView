param([Parameter(Mandatory=$true)][string]$AppDirectory)
$ErrorActionPreference='Stop'
$folder=(Resolve-Path -LiteralPath $AppDirectory).Path
$class='Registry::HKEY_CURRENT_USER\Software\Classes\CLSID\{4B64E153-E9B5-409A-A8F7-02D07D6D1BDD}\InProcServer32'
if (!(Test-Path -LiteralPath $class) -or (Get-Item -LiteralPath $class).GetValue('') -ine (Join-Path $folder 'DollView.Thumbnail.dll')) {
    throw 'Enable Windows Explorer PDF thumbnails for the copy under test first.'
}
$file=Join-Path $env:TEMP ('DollView-first-page-'+[guid]::NewGuid().ToString('N')+'.pdf')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\tests\fixtures\three-color-pages.pdf') -Destination $file
try {
    Add-Type -AssemblyName System.Drawing
    $app=[Reflection.Assembly]::LoadFrom((Join-Path $folder 'DollView.exe'))
    $method=$app.GetType('LumaView.MediaFiles').GetMethod('ReadShellThumbnail',[Reflection.BindingFlags]'Static,NonPublic')
    $image=$method.Invoke($null,[object[]]@($file.ToString()))
    if (!$image){throw 'Windows Shell returned no PDF thumbnail.'}
    try {
        $color=$image.GetPixel([int]($image.Width/2),[int]($image.Height/2))
        if ($image.Width -lt 32 -or [math]::Abs($image.Width/$image.Height-1.6) -gt .12 -or
            $color.R -lt 200 -or $color.G -gt 50 -or $color.B -gt 50){throw 'Expected the red landscape first page.'}
        Write-Output "PASS Windows Shell PDF first page ($($image.Width) x $($image.Height))"
    }finally{$image.Dispose()}
}finally{Remove-Item -LiteralPath $file}
