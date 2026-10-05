param(
    [string]$OutputDir = "$PSScriptRoot\..\bench",
    [int]$JpegPages = 240,
    [int]$PngPages = 80
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression.FileSystem

$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$jpgDir = Join-Path $OutputDir "big-folder"
$pngDir = Join-Path $OutputDir "png-folder"
foreach ($dir in @($jpgDir, $pngDir)) {
    if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}

$jpegCodec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' }
$jpegParams = New-Object System.Drawing.Imaging.EncoderParameters 1
$jpegParams.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter ([System.Drawing.Imaging.Encoder]::Quality), 80L

function New-TestImage {
    param(
        [string]$Path,
        [int]$Index,
        [int]$Width,
        [int]$Height,
        [bool]$AsPng
    )

    $bmp = New-Object System.Drawing.Bitmap $Width, $Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $rect = New-Object System.Drawing.RectangleF 0, 0, $Width, $Height
    $c1 = [System.Drawing.Color]::FromArgb(255, (30 + ($Index * 37) % 200), (60 + ($Index * 53) % 180), (90 + ($Index * 29) % 160))
    $c2 = [System.Drawing.Color]::FromArgb(255, (200 - ($Index * 41) % 150), (210 - ($Index * 23) % 170), (220 - ($Index * 31) % 180))
    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, $c1, $c2, 45.0
    $g.FillRectangle($gradient, $rect)
    $gradient.Dispose()

    $rnd = New-Object System.Random ($Index + 1)
    for ($i = 0; $i -lt 45; $i++) {
        $color = [System.Drawing.Color]::FromArgb(170, $rnd.Next(256), $rnd.Next(256), $rnd.Next(256))
        $brush = New-Object System.Drawing.SolidBrush $color
        $g.FillRectangle($brush, $rnd.Next($Width), $rnd.Next($Height), $rnd.Next(40, 400), $rnd.Next(40, 400))
        $brush.Dispose()
    }

    $font = New-Object System.Drawing.Font("Segoe UI", 110, [System.Drawing.FontStyle]::Bold)
    $g.DrawString("Page $Index", $font, [System.Drawing.Brushes]::White, 90, 90)
    $font.Dispose()
    $g.Dispose()

    if ($AsPng) {
        $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    else {
        $bmp.Save($Path, $jpegCodec, $jpegParams)
    }

    $bmp.Dispose()
}

Write-Output "Generating $JpegPages JPEG pages ($jpgDir) ..."
for ($i = 1; $i -le $JpegPages; $i++) {
    New-TestImage -Path (Join-Path $jpgDir ("page{0:D4}.jpg" -f $i)) -Index $i -Width 1400 -Height 2000 -AsPng $false
    if ($i % 40 -eq 0) { Write-Output "  $i/$JpegPages" }
}

Write-Output "Generating $PngPages PNG pages ($pngDir) ..."
for ($i = 1; $i -le $PngPages; $i++) {
    New-TestImage -Path (Join-Path $pngDir ("page{0:D4}.png" -f $i)) -Index $i -Width 1400 -Height 2000 -AsPng $true
    if ($i % 20 -eq 0) { Write-Output "  $i/$PngPages" }
}

$jpgComicInfo = '<?xml version="1.0" encoding="utf-8"?><ComicInfo><Title>Bench JPEG</Title><PageCount>' + $JpegPages + '</PageCount></ComicInfo>'
Set-Content -Path (Join-Path $jpgDir "ComicInfo.xml") -Value $jpgComicInfo -Encoding ASCII
$pngComicInfo = '<?xml version="1.0" encoding="utf-8"?><ComicInfo><Title>Bench PNG</Title><PageCount>' + $PngPages + '</PageCount></ComicInfo>'
Set-Content -Path (Join-Path $pngDir "ComicInfo.xml") -Value $pngComicInfo -Encoding ASCII

function New-Zip($SourceDir, $ZipPath, $Level) {
    if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }
    Write-Output "Packing $ZipPath ..."
    [System.IO.Compression.ZipFile]::CreateFromDirectory($SourceDir, $ZipPath, $Level, $false)
}

New-Zip $jpgDir (Join-Path $OutputDir "big-store.zip") ([System.IO.Compression.CompressionLevel]::NoCompression)
New-Zip $jpgDir (Join-Path $OutputDir "big.cbz") ([System.IO.Compression.CompressionLevel]::Optimal)
New-Zip $pngDir (Join-Path $OutputDir "png.cbz") ([System.IO.Compression.CompressionLevel]::Optimal)

$tarPath = Join-Path $OutputDir "big.cbt"
if (Test-Path -LiteralPath $tarPath) { Remove-Item -LiteralPath $tarPath -Force }
Write-Output "Packing $tarPath ..."
Push-Location $jpgDir
try { tar -cf $tarPath "." } finally { Pop-Location }

Write-Output ""
Get-ChildItem $OutputDir | Sort-Object Name | ForEach-Object {
    if ($_.PSIsContainer) {
        $size = (Get-ChildItem $_.FullName | Measure-Object Length -Sum).Sum
        Write-Output ("{0,-16} {1,8:N1} MB (folder)" -f $_.Name, ($size / 1MB))
    }
    else {
        Write-Output ("{0,-16} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB))
    }
}
