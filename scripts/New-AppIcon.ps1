<#
.SYNOPSIS
    Builds the DeskKit application icons from the two master images.

.DESCRIPTION
    The artwork lives as two square PNG masters next to this script — one for the
    light theme, one for the dark one. This script turns each into a multi-size
    ICO: it rounds the master to the Windows 11 squircle, resamples it to every
    size Windows asks a task bar or a title bar for, and writes the frames into a
    single file. The 256px frame is stored PNG-compressed and the rest as classic
    DIB frames, which is what the shell reads without complaint.

    The name changed, so the two files differ from the single 32x32 one the first
    version of this script drew by hand: the app now wears a themed pair, and
    ShellAssets picks between them.

.EXAMPLE
    pwsh -File scripts/New-AppIcon.ps1

.EXAMPLE
    pwsh -File scripts/New-AppIcon.ps1 -OutputDirectory C:\tmp\icons
#>
[CmdletBinding()]
param(
    [string] $LightMaster,
    [string] $DarkMaster,
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$scriptDirectory = if ($PSScriptRoot) {
    $PSScriptRoot
}
elseif ($PSCommandPath) {
    Split-Path -Parent $PSCommandPath
}
else {
    (Get-Location).Path
}

if (-not $LightMaster) {
    $LightMaster = Join-Path $scriptDirectory 'AppIcon\deskkit-light.png'
}
if (-not $DarkMaster) {
    $DarkMaster = Join-Path $scriptDirectory 'AppIcon\deskkit-dark.png'
}
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $scriptDirectory '..\src\DeskKit.App\Assets'
}

# Everything the shell may scale an icon to. 256 is asked for by the task bar's
# large-icon views, 16 by the title bar and the notification area.
$sizes = @(16, 24, 32, 48, 64, 128, 256)

# Windows 11 draws an app tile as a squircle; this is its corner radius as a
# fraction of the tile's side, which is close enough to read as one.
$cornerRatio = 0.2237

function New-RoundedPath {
    param([int] $Side, [double] $Radius)

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = 2 * $Radius
    $path.AddArc(0, 0, $diameter, $diameter, 180, 90)
    $path.AddArc($Side - $diameter, 0, $diameter, $diameter, 270, 90)
    $path.AddArc($Side - $diameter, $Side - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc(0, $Side - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

# Clips the square master to the squircle, so everything outside it is left
# transparent rather than carrying the master's own corners.
function Get-MaskedMaster {
    param([string] $Path)

    $source = [System.Drawing.Image]::FromFile((Resolve-Path $Path))
    $side = [Math]::Min($source.Width, $source.Height)

    $masked = New-Object System.Drawing.Bitmap -ArgumentList $side, $side,
        ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($masked)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    [System.Drawing.Drawing2D.GraphicsPath] $path =
        New-RoundedPath -Side $side -Radius ([double] $side * $cornerRatio)
    $graphics.SetClip($path)
    $graphics.DrawImage($source, 0, 0, $side, $side)
    $graphics.ResetClip()
    $graphics.Dispose()
    $source.Dispose()
    return $masked
}

function Get-ScaledFrame {
    param([System.Drawing.Bitmap] $Master, [int] $Size)

    $frame = New-Object System.Drawing.Bitmap -ArgumentList $Size, $Size,
        ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($frame)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.DrawImage($Master, 0, 0, $Size, $Size)
    $graphics.Dispose()
    return $frame
}

# A classic ICO frame: BITMAPINFOHEADER, then the pixels bottom-up as BGRA, then
# a 1bpp AND mask. The alpha channel carries the shape, so the mask stays zero.
function Get-DibBytes {
    param([System.Drawing.Bitmap] $Frame)

    $width = $Frame.Width
    $height = $Frame.Height
    $rect = New-Object System.Drawing.Rectangle 0, 0, $width, $height
    $data = $Frame.LockBits(
        $rect,
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $pixels = New-Object byte[] ($stride * $height)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $Frame.UnlockBits($data)

    $andStride = [int]([Math]::Floor(($width + 31) / 32)) * 4
    $xorSize = $stride * $height
    $andSize = $andStride * $height

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)
    $writer.Write([int] 40)
    $writer.Write([int] $width)
    $writer.Write([int] ($height * 2))
    $writer.Write([int16] 1)
    $writer.Write([int16] 32)
    $writer.Write([int] 0)
    $writer.Write([int] ($xorSize + $andSize))
    $writer.Write([int] 0)
    $writer.Write([int] 0)
    $writer.Write([int] 0)
    $writer.Write([int] 0)
    for ($y = $height - 1; $y -ge 0; $y--) {
        $writer.Write($pixels, $y * $stride, $width * 4)
    }
    $writer.Write((New-Object byte[] $andSize))
    $writer.Flush()

    $bytes = $stream.ToArray()
    $writer.Dispose()
    $stream.Dispose()
    return , $bytes
}

function Get-PngBytes {
    param([System.Drawing.Bitmap] $Frame)

    $stream = New-Object System.IO.MemoryStream
    $Frame.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    return , $bytes
}

function Write-Ico {
    param([System.Drawing.Bitmap] $Master, [string] $Path)

    $frames = foreach ($size in $sizes) {
        $frame = Get-ScaledFrame -Master $Master -Size $size
        # 256 is kept as PNG, the rest as DIB: that is the split the shell expects.
        $bytes = if ($size -ge 256) { Get-PngBytes -Frame $frame } else { Get-DibBytes -Frame $frame }
        $frame.Dispose()
        [pscustomobject]@{ Size = $size; Bytes = $bytes }
    }

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)

    # ICONDIR
    $writer.Write([int16] 0)
    $writer.Write([int16] 1)
    $writer.Write([int16] $frames.Count)

    # ICONDIRENTRY for each frame; a 256px dimension is written as 0.
    $offset = 6 + (16 * $frames.Count)
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -ge 256) { 0 } else { $frame.Size }
        $writer.Write([byte] $dimension)
        $writer.Write([byte] $dimension)
        $writer.Write([byte] 0)
        $writer.Write([byte] 0)
        $writer.Write([int16] 1)
        $writer.Write([int16] 32)
        $writer.Write([int] $frame.Bytes.Length)
        $writer.Write([int] $offset)
        $offset += $frame.Bytes.Length
    }

    foreach ($frame in $frames) {
        $writer.Write($frame.Bytes, 0, $frame.Bytes.Length)
    }

    $writer.Flush()
    $bytes = $stream.ToArray()
    $writer.Dispose()
    $stream.Dispose()

    if (-not (Test-Path $OutputDirectory)) {
        New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    }

    [System.IO.File]::WriteAllBytes($Path, $bytes)
    Write-Host "wrote $Path ($($bytes.Length) bytes, $($frames.Count) frames)"
}

foreach ($variant in @(
        @{ Master = $LightMaster; Name = 'deskkit-light.ico' },
        @{ Master = $DarkMaster; Name = 'deskkit-dark.ico' })) {
    $masked = Get-MaskedMaster -Path $variant.Master
    Write-Ico -Master $masked -Path (Join-Path $OutputDirectory $variant.Name)
    $masked.Dispose()
}
