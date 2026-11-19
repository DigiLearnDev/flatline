# Vykreslí ikonu FlatLine (ležatý semafor: šedá = běží, žlutá = čeká, zelená = hotovo)
# do assets/icon.ico ve velikostech 16, 24, 32, 48, 64 a 256 px.
# Použití: powershell -NoProfile -File assets/make-icon.ps1
Add-Type -AssemblyName System.Drawing

function New-RoundRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconPng([int]$size) {
    $bmp = New-Object Drawing.Bitmap $size, $size
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([Drawing.Color]::Transparent)
    $s = $size / 256.0

    # Pozadí: tmavý zaoblený čtverec jako lišta.
    $bg = New-RoundRect (8 * $s) (8 * $s) (240 * $s) (240 * $s) (52 * $s)
    $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(0x1E, 0x1F, 0x22))), $bg)

    # Tělo semaforu.
    $body = New-RoundRect (24 * $s) (84 * $s) (208 * $s) (88 * $s) (44 * $s)
    $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(0x3A, 0x3C, 0x42))), $body)

    # Tři světla.
    $colors = @(
        [Drawing.Color]::FromArgb(0xA1, 0xA1, 0xAA),  # běží
        [Drawing.Color]::FromArgb(0xEA, 0xB3, 0x08),  # potřebuje pozornost
        [Drawing.Color]::FromArgb(0x22, 0xC5, 0x5E)   # hotovo
    )
    $d = 50 * $s
    for ($i = 0; $i -lt 3; $i++) {
        $cx = (68 + $i * 60) * $s
        $g.FillEllipse((New-Object Drawing.SolidBrush $colors[$i]), [single]($cx - $d / 2), [single](128 * $s - $d / 2), [single]$d, [single]$d)
    }

    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 256
$pngs = foreach ($sz in $sizes) { , (New-IconPng $sz) }

# Soubor ICO: hlavička, adresář položek, pak PNG data za sebou.
$out = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $len = $pngs[$i].Length
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write([byte[]]$p) }
$w.Flush()

$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
[IO.File]::WriteAllBytes((Join-Path $dir 'icon.ico'), $out.ToArray())
[IO.File]::WriteAllBytes((Join-Path $dir 'icon.png'), $pngs[-1])
"icon.ico: $($out.Length) B"
