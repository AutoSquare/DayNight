param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$sourcePath = Join-Path $RepositoryRoot 'DayNight/design/daynight-logo.png'
$assetPath = Join-Path $RepositoryRoot 'DayNight/Assets'
$source = [System.Drawing.Bitmap]::new($sourcePath)
# 原图主体的边界；移除顶部、左侧主体以外的抠图残留，不改变日月图形。
$crop = [System.Drawing.Rectangle]::new(56, 70, 1140, 1132)
$sizes = @(16, 20, 24, 25, 30, 32, 35, 40, 48, 50, 60, 64, 128, 256)
$iconSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = @{}
try {
    foreach ($size in $sizes) {
        $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $destination = [System.Drawing.RectangleF]::new(0, [single](4 * $size / 1140), $size, [single](1132 * $size / 1140))
            $graphics.DrawImage($source, $destination, $crop, [System.Drawing.GraphicsUnit]::Pixel)
            $stream = [System.IO.MemoryStream]::new()
            try {
                $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
                $frames[$size] = $stream.ToArray()
                [System.IO.File]::WriteAllBytes((Join-Path $assetPath "daynight-$size.png"), $frames[$size])
            } finally { $stream.Dispose() }
        } finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
    $output = [System.IO.File]::Create((Join-Path $assetPath 'daynight.ico'))
    $writer = [System.IO.BinaryWriter]::new($output)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$iconSizes.Count)
        $offset = 6 + 16 * $iconSizes.Count
        foreach ($size in $iconSizes) {
            $dimension = if ($size -eq 256) { 0 } else { $size }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$size].Length); $writer.Write([uint32]$offset)
            $offset += $frames[$size].Length
        }
        foreach ($size in $iconSizes) { $writer.Write([byte[]]$frames[$size]) }
    } finally { $writer.Dispose(); $output.Dispose() }
} finally { $source.Dispose() }
Write-Output "已生成多尺寸 PNG 与 ICO：$assetPath"
