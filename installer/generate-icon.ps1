# Generates installer/assets/NexaRun.ico (PNG entries — valid for Inno Setup + Windows)
$ErrorActionPreference = "Stop"

# PowerShell 7+: Bitmap/Icon live in System.Drawing.Common (System.Drawing is a facade).
Add-Type -AssemblyName System.Drawing.Common

function Save-PngIcon {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][System.Drawing.Bitmap[]]$Images
    )

    $pngs = [System.Collections.Generic.List[byte[]]]::new($Images.Length)
    foreach ($img in $Images) {
        $ms = [System.IO.MemoryStream]::new()
        try {
            $img.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
            $pngs.Add($ms.ToArray())
        }
        finally {
            $ms.Dispose()
        }
    }

    $fs = [System.IO.File]::Create($Path)
    $bw = [System.IO.BinaryWriter]::new($fs)
    try {
        $bw.Write([int16]0)
        $bw.Write([int16]1)
        $bw.Write([int16]$pngs.Count)

        $offset = 6 + (16 * $pngs.Count)
        for ($i = 0; $i -lt $Images.Length; $i++) {
            $img = $Images[$i]
            $w = $img.Width
            $h = $img.Height
            $bw.Write([byte]($(if ($w -ge 256) { 0 } else { $w })))
            $bw.Write([byte]($(if ($h -ge 256) { 0 } else { $h })))
            $bw.Write([byte]0)
            $bw.Write([byte]0)
            $bw.Write([int16]1)
            $bw.Write([int16]32)
            $bw.Write([int]$pngs[$i].Length)
            $bw.Write([int]$offset)
            $offset += $pngs[$i].Length
        }

        foreach ($png in $pngs) {
            $bw.Write($png)
        }
    }
    finally {
        $bw.Dispose()
        $fs.Dispose()
    }
}

function New-NexaRunBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::FromArgb(255, 30, 30, 30))

    $green = [System.Drawing.Color]::FromArgb(255, 0, 220, 110)
    $pen = New-Object System.Drawing.Pen $green, ([Math]::Max(1.0, $size / 21.0))
    $x1 = $size * 0.19; $x2 = $size * 0.41
    $y1 = $size * 0.31; $ym = $size * 0.50; $y2 = $size * 0.69
    $g.DrawLine($pen, $x1, $y1, $x2, $ym)
    $g.DrawLine($pen, $x1, $y2, $x2, $ym)
    $g.DrawLine($pen, ($size * 0.50), ($size * 0.69), ($size * 0.75), ($size * 0.69))
    $g.Dispose()
    $pen.Dispose()
    return $bmp
}

$out = Join-Path $PSScriptRoot "assets\NexaRun.ico"
$dir = Split-Path $out -Parent
if (-not (Test-Path $dir)) { New-Item $dir -ItemType Directory | Out-Null }

$bitmaps = [System.Collections.Generic.List[System.Drawing.Bitmap]]::new()
try {
    foreach ($s in @(16, 32, 48, 64, 256)) {
        $bitmaps.Add((New-NexaRunBitmap $s))
    }
    Save-PngIcon -Path $out -Images $bitmaps.ToArray()
}
finally {
    foreach ($bmp in $bitmaps) { $bmp.Dispose() }
}

if ((Get-Item $out).Length -lt 500) { throw "Generated icon looks invalid: $out" }

# Validate Windows can load it
$icon = [System.Drawing.Icon]::new($out)
$icon.Dispose()

Write-Host "Wrote $out ($((Get-Item $out).Length) bytes)"
