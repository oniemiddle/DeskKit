<#
.SYNOPSIS
    Generates the DeskKit application icon.

.DESCRIPTION
    Writes a 32x32 32-bit ICO by hand so the repository needs no binary blob and
    no image tooling: the file is reproducible from this script alone.

    ICO layout used here:
      ICONDIR (6 bytes) + one ICONDIRENTRY (16 bytes) + BITMAPINFOHEADER (40) +
      XOR bitmap (32*32 BGRA, bottom up) + AND mask (32*32 bits, all zero).

.EXAMPLE
    pwsh -File scripts/New-AppIcon.ps1
#>
[CmdletBinding()]
param(
    [string] $OutputPath,
    [int] $Size = 32
)

$ErrorActionPreference = 'Stop'

$scriptDirectory = if ($PSScriptRoot) {
    $PSScriptRoot
}
elseif ($PSCommandPath) {
    Split-Path -Parent $PSCommandPath
}
else {
    (Get-Location).Path
}

if (-not $OutputPath) {
    $OutputPath = Join-Path $scriptDirectory '..\src\DeskKit.App\Assets\deskkit.ico'
}

function New-RoundedSquarePixels {
    param([int] $Side)

    $radius = [double] $Side * 0.22

    # Vertical gradient between the two brand colours.
    $top = @(0x25, 0x63, 0xEB)    # #2563EB
    $bottom = @(0x7C, 0x3A, 0xED) # #7C3AED

    $pixels = New-Object 'object[,]' $Side, $Side

    for ($y = 0; $y -lt $Side; $y++) {
        $t = $y / [double] ($Side - 1)
        $r = [int][Math]::Round($top[0] + (($bottom[0] - $top[0]) * $t))
        $g = [int][Math]::Round($top[1] + (($bottom[1] - $top[1]) * $t))
        $b = [int][Math]::Round($top[2] + (($bottom[2] - $top[2]) * $t))

        for ($x = 0; $x -lt $Side; $x++) {
            # Anti-aliased rounded square using a signed distance test.
            $cx = [Math]::Min([Math]::Max($x + 0.5, $radius), $Side - $radius)
            $cy = [Math]::Min([Math]::Max($y + 0.5, $radius), $Side - $radius)
            $dx = ($x + 0.5) - $cx
            $dy = ($y + 0.5) - $cy
            $distance = [Math]::Sqrt(($dx * $dx) + ($dy * $dy))
            $coverage = [Math]::Min([Math]::Max($radius - $distance + 0.5, 0.0), 1.0)

            $pixels[$x, $y] = @($r, $g, $b, [int][Math]::Round(255 * $coverage))
        }
    }

    # A white glyph: two stacked bars, so the icon reads as a widget.
    $barLeft = [int]($Side * 0.30)
    $barWidth = [int]($Side * 0.40)
    $barHeight = [int]($Side * 0.10)

    foreach ($barTop in @([int]($Side * 0.30), [int]($Side * 0.56))) {
        for ($y = $barTop; $y -lt $barTop + $barHeight; $y++) {
            for ($x = $barLeft; $x -lt $barLeft + $barWidth; $x++) {
                $alpha = $pixels[$x, $y][3]
                if ($alpha -le 0) { continue }

                $pixels[$x, $y] = @(255, 255, 255, $alpha)
            }
        }
    }

    # The comma stops PowerShell from unrolling the 2D array on the way out.
    return , $pixels
}

function Write-Ico {
    param(
        [string] $Path,
        [int] $Side,
        [object[,]] $Pixels
    )

    $xorBytes = $Side * $Side * 4
    $andBytes = [int]($Side * $Side / 8)
    $imageBytes = 40 + $xorBytes + $andBytes

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)

    # ICONDIR
    $writer.Write([UInt16] 0)
    $writer.Write([UInt16] 1)
    $writer.Write([UInt16] 1)

    # ICONDIRENTRY
    $writer.Write([Byte] $Side)
    $writer.Write([Byte] $Side)
    $writer.Write([Byte] 0)
    $writer.Write([Byte] 0)
    $writer.Write([UInt16] 1)
    $writer.Write([UInt16] 32)
    $writer.Write([UInt32] $imageBytes)
    $writer.Write([UInt32] 22)

    # BITMAPINFOHEADER; height is doubled to cover the XOR and AND masks.
    $writer.Write([UInt32] 40)
    $writer.Write([Int32] $Side)
    $writer.Write([Int32] ($Side * 2))
    $writer.Write([UInt16] 1)
    $writer.Write([UInt16] 32)
    $writer.Write([UInt32] 0)
    $writer.Write([UInt32] ($xorBytes + $andBytes))
    $writer.Write([Int32] 0)
    $writer.Write([Int32] 0)
    $writer.Write([UInt32] 0)
    $writer.Write([UInt32] 0)

    # XOR bitmap, bottom-up, BGRA.
    for ($y = $Side - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $Side; $x++) {
            $pixel = $Pixels[$x, $y]
            $writer.Write([Byte] $pixel[2])
            $writer.Write([Byte] $pixel[1])
            $writer.Write([Byte] $pixel[0])
            $writer.Write([Byte] $pixel[3])
        }
    }

    # AND mask: zero everywhere, the alpha channel carries the shape.
    $writer.Write((New-Object byte[] $andBytes))

    $writer.Flush()
    $bytes = $stream.ToArray()
    $writer.Dispose()
    $stream.Dispose()

    $directory = Split-Path -Parent $Path
    if ($directory -and -not (Test-Path $directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    [System.IO.File]::WriteAllBytes($Path, $bytes)
    Write-Host "wrote $Path ($($bytes.Length) bytes)"
}

$pixels = New-RoundedSquarePixels -Side $Size
Write-Ico -Path $OutputPath -Side $Size -Pixels $pixels
