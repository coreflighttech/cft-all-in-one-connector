param(
    [Parameter(Mandatory = $true)][string]$SourcePng,
    [Parameter(Mandatory = $true)][string]$OutputIco
)

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$source = [System.Drawing.Bitmap]::FromFile($SourcePng)
$images = [System.Collections.Generic.List[byte[]]]::new()

try {
    $left = $source.Width
    $top = $source.Height
    $right = -1
    $bottom = -1
    for ($y = 0; $y -lt $source.Height; $y++) {
        for ($x = 0; $x -lt $source.Width; $x++) {
            if ($source.GetPixel($x, $y).A -eq 0) { continue }
            if ($x -lt $left) { $left = $x }
            if ($x -gt $right) { $right = $x }
            if ($y -lt $top) { $top = $y }
            if ($y -gt $bottom) { $bottom = $y }
        }
    }
    if ($right -lt $left -or $bottom -lt $top) { throw "The source image has no visible pixels." }
    $sourceRect = [System.Drawing.Rectangle]::new($left, $top, $right - $left + 1, $bottom - $top + 1)

    foreach ($size in $sizes) {
        $canvas = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($canvas)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

                $padding = [Math]::Max(1, [Math]::Round($size * 0.07))
                $available = $size - (2 * $padding)
                $scale = [Math]::Min($available / $sourceRect.Width, $available / $sourceRect.Height)
                $width = [Math]::Max(1, [Math]::Round($sourceRect.Width * $scale))
                $height = [Math]::Max(1, [Math]::Round($sourceRect.Height * $scale))
                $x = [Math]::Floor(($size - $width) / 2)
                $y = [Math]::Floor(($size - $height) / 2)
                $destinationRect = [System.Drawing.Rectangle]::new($x, $y, $width, $height)
                $graphics.DrawImage($source, $destinationRect, $sourceRect, [System.Drawing.GraphicsUnit]::Pixel)
            }
            finally { $graphics.Dispose() }

            $memory = [System.IO.MemoryStream]::new()
            try {
                $canvas.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
                $images.Add($memory.ToArray())
            }
            finally { $memory.Dispose() }
        }
        finally { $canvas.Dispose() }
    }

    $parent = Split-Path -Parent $OutputIco
    if ($parent) { [System.IO.Directory]::CreateDirectory($parent) | Out-Null }
    $stream = [System.IO.File]::Open($OutputIco, [System.IO.FileMode]::Create)
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$sizes.Count)

        $offset = 6 + (16 * $sizes.Count)
        for ($index = 0; $index -lt $sizes.Count; $index++) {
            $size = $sizes[$index]
            $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
            $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$images[$index].Length)
            $writer.Write([uint32]$offset)
            $offset += $images[$index].Length
        }

        foreach ($image in $images) { $writer.Write($image) }
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}
finally { $source.Dispose() }
