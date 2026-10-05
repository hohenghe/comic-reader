param(
    [string]$OutFile = "$PSScriptRoot\..\src\ComicReader.App\Assets\app.ico"
)

Add-Type -AssemblyName System.Drawing

$OutFile = [System.IO.Path]::GetFullPath($OutFile)
$assetsDir = Split-Path -Parent $OutFile
New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null

$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)

$bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 30, 32, 42))
$pageLeft = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 236, 238, 245))
$pageRight = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 91, 141, 239))
$line = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(120, 30, 32, 42))

$g.FillRectangle($bg, 0, 0, $size, $size)
$g.FillRectangle($pageLeft, 46, 62, 78, 134)
$g.FillRectangle($pageRight, 132, 62, 78, 134)

# page lines
for ($i = 0; $i -lt 5; $i++) {
    $g.FillRectangle($line, 58, 82 + ($i * 22), 54, 6)
    $g.FillRectangle($line, 144, 82 + ($i * 22), 54, 6)
}

# spine highlight
$spinePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 255, 255, 255)), 4
$g.DrawLine($spinePen, 128, 58, 128, 200)

$g.Dispose()
$bg.Dispose(); $pageLeft.Dispose(); $pageRight.Dispose(); $line.Dispose(); $spinePen.Dispose()

$tmpPng = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "comicreader_icon_$PID.png")
$bmp.Save($tmpPng, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

$png = [System.IO.File]::ReadAllBytes($tmpPng)
Remove-Item -LiteralPath $tmpPng -Force

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([UInt16]0)          # reserved
$bw.Write([UInt16]1)          # type: icon
$bw.Write([UInt16]1)          # image count
$bw.Write([Byte]0)            # width 256
$bw.Write([Byte]0)            # height 256
$bw.Write([Byte]0)            # palette count
$bw.Write([Byte]0)            # reserved
$bw.Write([UInt16]1)          # color planes
$bw.Write([UInt16]32)         # bits per pixel
$bw.Write([UInt32]$png.Length)
$bw.Write([UInt32]22)         # offset
$bw.Write($png)
$bw.Flush()
[System.IO.File]::WriteAllBytes($OutFile, $ms.ToArray())
$bw.Dispose(); $ms.Dispose()

Write-Output "Icon written: $OutFile ($((Get-Item $OutFile).Length) bytes)"
