# 生成应用图标 src/Ppinpix.App/Assets/app.ico（16/20/24/32/48/256）。
# 图形严格对应根目录 app-icon.svg：
# 浅灰白圆角底 (#F5F7FB, 描边 #DEE2E6) + 蓝 (#3B82F6) / 珊瑚橙 (#FF7A59) 叠层圆角方块 + 交集金黄 (#FFD166)。

Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root 'src\Ppinpix.App\Assets'
New-Item -ItemType Directory -Force $outDir | Out-Null
$outFile = Join-Path $outDir 'app.ico'

function New-RoundedRectPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $d = $r * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # 对应 app-icon.svg viewBox="0 0 104 104"
    $k = [single]($size / 104.0)

    # 1. 外框底色：x=1 y=1 w=102 h=102 rx=23 fill=#F5F7FB stroke=#DEE2E6 stroke-width=1.2
    $bgPath = New-RoundedRectPath (1 * $k) (1 * $k) (102 * $k) (102 * $k) (23 * $k)
    $bgBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0xF5, 0xF7, 0xFB))
    $g.FillPath($bgBrush, $bgPath)
    $strokeWidth = [single][Math]::Max(1.0, 1.2 * $k)
    $bgPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 0xDE, 0xE2, 0xE6), $strokeWidth)
    $g.DrawPath($bgPen, $bgPath)

    # 2. 左上蓝色方块：x=17 y=17 w=46 h=46 rx=12 fill=#3B82F6
    $bluePath = New-RoundedRectPath (17 * $k) (17 * $k) (46 * $k) (46 * $k) (12 * $k)
    $blueBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0x3B, 0x82, 0xF6))
    $g.FillPath($blueBrush, $bluePath)

    # 3. 右下珊瑚橙方块：x=41 y=41 w=46 h=46 rx=12 fill=#FF7A59
    $orangePath = New-RoundedRectPath (41 * $k) (41 * $k) (46 * $k) (46 * $k) (12 * $k)
    $orangeBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0xFF, 0x7A, 0x59))
    $g.FillPath($orangeBrush, $orangePath)

    # 4. 交集区域：M53 41 H63 V51 A12 12 0 0 1 51 63 H41 V53 A12 12 0 0 1 53 41 Z fill=#FFD166
    $interPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $interPath.AddArc((41 * $k), (41 * $k), (24 * $k), (24 * $k), 180, 90)
    $interPath.AddLine((53 * $k), (41 * $k), (63 * $k), (41 * $k))
    $interPath.AddLine((63 * $k), (41 * $k), (63 * $k), (51 * $k))
    $interPath.AddArc((39 * $k), (39 * $k), (24 * $k), (24 * $k), 0, 90)
    $interPath.AddLine((51 * $k), (63 * $k), (41 * $k), (63 * $k))
    $interPath.AddLine((41 * $k), (63 * $k), (41 * $k), (53 * $k))
    $interPath.CloseFigure()
    $yellowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0xFF, 0xD1, 0x66))
    $g.FillPath($yellowBrush, $interPath)

    $bgBrush.Dispose(); $bgPen.Dispose(); $bgPath.Dispose()
    $blueBrush.Dispose(); $bluePath.Dispose()
    $orangeBrush.Dispose(); $orangePath.Dispose()
    $yellowBrush.Dispose(); $interPath.Dispose()
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 48, 256
$images = @()
foreach ($size in $sizes) { $images += ,(New-IconPng $size) }

$stream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($stream)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dim)
    $writer.Write([byte]$dim)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length)
    $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $writer.Write([byte[]]$img) }
$writer.Flush()

[System.IO.File]::WriteAllBytes($outFile, $stream.ToArray())
"Wrote $outFile ($($stream.Length) bytes)"
