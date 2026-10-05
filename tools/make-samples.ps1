param(
    [string]$OutputDir = "$PSScriptRoot\..\samples"
)

Add-Type -AssemblyName System.Drawing

$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
$workDir = Join-Path $OutputDir "pages"
New-Item -ItemType Directory -Force -Path $workDir | Out-Null

$titles = @("ComicReader Sample")

1..12 | ForEach-Object {
    $bmp = New-Object System.Drawing.Bitmap 800, 1200
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb(255, 245, 245, 250))
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 90, 120, 200)), 6
    $g.DrawRectangle($pen, 20, 20, 760, 1160)
    $fontBig = New-Object System.Drawing.Font("Segoe UI", 72, [System.Drawing.FontStyle]::Bold)
    $fontSmall = New-Object System.Drawing.Font("Segoe UI", 28)
    $g.DrawString("Page $_", $fontBig, [System.Drawing.Brushes]::Black, 260, 520)
    $g.DrawString("ComicReader sample page", $fontSmall, [System.Drawing.Brushes]::Gray, 240, 660)
    $g.Dispose()
    $pen.Dispose()
    $fontBig.Dispose()
    $fontSmall.Dispose()
    $bmp.Save((Join-Path $workDir "page$_.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$comicInfo = @"
<?xml version="1.0" encoding="utf-8"?>
<ComicInfo>
  <Title>ComicReader Sample</Title>
  <Series>Sample</Series>
  <Writer>ComicReader</Writer>
  <PageCount>12</PageCount>
  <Manga>No</Manga>
</ComicInfo>
"@
Set-Content -Path (Join-Path $workDir "ComicInfo.xml") -Value $comicInfo -Encoding UTF8

$cbz = Join-Path $OutputDir "sample.cbz"
$zipTemp = Join-Path $OutputDir "sample.zip"
if (Test-Path -LiteralPath $zipTemp) { Remove-Item -LiteralPath $zipTemp -Force }
if (Test-Path -LiteralPath $cbz) { Remove-Item -LiteralPath $cbz -Force }
Compress-Archive -Path (Join-Path $workDir "*") -DestinationPath $zipTemp -Force
Move-Item -LiteralPath $zipTemp -Destination $cbz -Force

$tar = Join-Path $OutputDir "sample.cbt"
if (Test-Path -LiteralPath $tar) { Remove-Item -LiteralPath $tar -Force }
Push-Location $workDir
try {
    tar -cf $tar "."
}
finally {
    Pop-Location
}

Remove-Item -LiteralPath $workDir -Recurse -Force

Write-Output "Generated:"
Write-Output "  $cbz"
Write-Output "  $tar"
