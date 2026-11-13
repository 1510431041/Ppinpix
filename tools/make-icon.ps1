# 生成应用图标 src/Ppinpix.App/Assets/app.ico（16/20/24/32/48/256）。
# 图形：品牌蓝 #2F80FF 圆角方块 + 白色取景框四角 + 中心点，见 docs/UI_VISUAL.md。
# 图标是提交进仓库的产物，只有改图形时才需要重新运行本脚本。

Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root 'src\Ppinpix.App\Assets'
New-Item -ItemType Directory -Force $outDir | Out-Null
$outFile = Join-Path $outDir 'app.ico'

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # 圆角方块
    $s = [single]$size
    $radius = [single]($s * 0.22)
    $d = $radius * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($s - $d, 0, $d, $d, 270, 90)
    $path.AddArc($s - $d, $s - $d, $d, $d, 0, 90)
    $path.AddArc(0, $s - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $accent = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0x2F, 0x80, 0xFF))
    $g.FillPath($accent, $path)

    # 取景框四角
    $stroke = [single][Math]::Max(1.5, $s * 0.09)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $stroke)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $m = [single]($s * 0.26)      # 边距
    $l = [single]($s * 0.17)      # 角臂长
    $a = $m
    $b = $s - $m

    $corners = @(
        @(@($a, ($a + $l)), @($a, $a), @(($a + $l), $a)),
        @(@(($b - $l), $a), @($b, $a), @($b, ($a + $l))),
        @(@($b, ($b - $l)), @($b, $b), @(($b - $l), $b)),
        @(@(($a + $l), $b), @($a, $b), @($a, ($b - $l)))
    )
    foreach ($c in $corners) {
        $pts = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF([single]$c[0][0], [single]$c[0][1])),
            (New-Object System.Drawing.PointF([single]$c[1][0], [single]$c[1][1])),
            (New-Object System.Drawing.PointF([single]$c[2][0], [single]$c[2][1]))
        )
        $g.DrawLines($pen, $pts)
    }

    # 中心点（24px 以下太挤，省略）
    if ($size -ge 24) {
        $r = [single]($s * 0.07)
        $g.FillEllipse([System.Drawing.Brushes]::White, ($s / 2 - $r), ($s / 2 - $r), ($r * 2), ($r * 2))
    }

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
