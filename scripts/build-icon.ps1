$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskAssets = Join-Path $taskRoot 'src\TrilhaEstudos.App\Assets'
$taskSource = Join-Path $taskAssets 'TrilhaEstudos.logo.png'
$taskTarget = Join-Path $taskAssets 'TrilhaEstudos.ico'
$taskSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$taskFrames = [Collections.Generic.List[byte[]]]::new()
$taskOriginal = [Drawing.Image]::FromFile($taskSource)
try {
    foreach ($taskSize in $taskSizes) {
        $taskBitmap = [Drawing.Bitmap]::new($taskSize, $taskSize, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $taskGraphics = [Drawing.Graphics]::FromImage($taskBitmap)
        $taskAttributes = [Drawing.Imaging.ImageAttributes]::new()
        $taskMemory = [IO.MemoryStream]::new()
        try {
            $taskGraphics.Clear([Drawing.Color]::Transparent)
            $taskGraphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $taskGraphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $taskGraphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
            $taskAttributes.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)
            $taskGraphics.DrawImage($taskOriginal, [Drawing.Rectangle]::new(0, 0, $taskSize, $taskSize),
                0, 0, $taskOriginal.Width, $taskOriginal.Height, [Drawing.GraphicsUnit]::Pixel, $taskAttributes)
            $taskBitmap.Save($taskMemory, [Drawing.Imaging.ImageFormat]::Png)
            $taskFrames.Add($taskMemory.ToArray())
        } finally {
            $taskMemory.Dispose()
            $taskAttributes.Dispose()
            $taskGraphics.Dispose()
            $taskBitmap.Dispose()
        }
    }
} finally { $taskOriginal.Dispose() }
$taskOutput = [IO.File]::Create($taskTarget)
$taskWriter = [IO.BinaryWriter]::new($taskOutput)
try {
    $taskWriter.Write([UInt16]0)
    $taskWriter.Write([UInt16]1)
    $taskWriter.Write([UInt16]$taskSizes.Count)
    $taskOffset = 6 + 16 * $taskSizes.Count
    for ($taskIndex = 0; $taskIndex -lt $taskSizes.Count; $taskIndex++) {
        $taskDimension = if ($taskSizes[$taskIndex] -eq 256) { 0 } else { $taskSizes[$taskIndex] }
        $taskWriter.Write([byte]$taskDimension)
        $taskWriter.Write([byte]$taskDimension)
        $taskWriter.Write([byte]0)
        $taskWriter.Write([byte]0)
        $taskWriter.Write([UInt16]1)
        $taskWriter.Write([UInt16]32)
        $taskWriter.Write([UInt32]$taskFrames[$taskIndex].Length)
        $taskWriter.Write([UInt32]$taskOffset)
        $taskOffset += $taskFrames[$taskIndex].Length
    }
    foreach ($taskFrame in $taskFrames) { $taskWriter.Write([byte[]]$taskFrame) }
} finally {
    $taskWriter.Dispose()
    $taskOutput.Dispose()
}
Write-Output "Ícone criado: $taskTarget"
Write-Output "Resoluções: $($taskSizes -join ', ') pixels, com transparência."
