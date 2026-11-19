# Vykreslí logo a ikonu FlatLine.
# Značka je jedna plochá čára (flatline): zleva vyskočí „A“, uprostřed se zvedne do čtyřcípé
# AI hvězdy jako tep na EKG a vpravo skončí písmenem „i“. Nad hvězdou jsou dvě tečky v barvách
# stavů: žlutá = potřebuje pozornost, modrá = hotovo. Nadpis FlatLine je pod značkou.
#
# Výstup do assets/:
#   logo-dark.png, logo-light.png  značka + nadpis, průhledné pozadí (světlá / tmavá kresba)
#   tile.png                       dlaždice ve stylu Microsoftu se značkou a nadpisem (512 px)
#   icon.png, icon.ico             dlaždice jen se značkou, ikona aplikace (16–256 px)
# Použití: powershell -NoProfile -File assets/make-logo.ps1
Add-Type -AssemblyName System.Drawing

function P([single]$x, [single]$y) { New-Object Drawing.PointF $x, $y }

$Light = [Drawing.Color]::FromArgb(0xF4, 0xF4, 0xF5)
$Dark = [Drawing.Color]::FromArgb(0x1E, 0x1F, 0x22)
$Yellow = [Drawing.Color]::FromArgb(0xEA, 0xB3, 0x08)   # potřebuje pozornost
$Blue = [Drawing.Color]::FromArgb(0x3B, 0x82, 0xF6)     # hotovo
$Gray = [Drawing.Color]::FromArgb(0xA1, 0xA1, 0xAA)     # běží
$TileTop = [Drawing.Color]::FromArgb(0x2B, 0x2D, 0x31)
$TileBottom = [Drawing.Color]::FromArgb(0x1E, 0x1F, 0x22)

# --- Značka v souřadnicích skici ---
$base = 240
$cx = 262; $cy = 165; $r = 75; $k = 14

$line = New-Object Drawing.Drawing2D.GraphicsPath
# „A“ je vysoká stříška bez příčky, špička ve výšce teček.
$line.AddLines([Drawing.PointF[]]@((P 80 $base), (P 124 82), (P 168 $base), (P 262 $base)))
$line.StartFigure()
# Dřík „i“ končí pod šedým puntíkem, který mu slouží jako tečka.
$line.AddLines([Drawing.PointF[]]@((P 262 $base), (P 400 $base), (P 400 150)))
$line.StartFigure()
$line.AddBeziers([Drawing.PointF[]]@(
    (P $cx ($cy + $r)), (P ($cx - $k) ($cy + $k)), (P ($cx - $k) ($cy + $k)), (P ($cx - $r) $cy),
    (P ($cx - $k) ($cy - $k)), (P ($cx - $k) ($cy - $k)), (P $cx ($cy - $r)),
    (P ($cx + $k) ($cy - $k)), (P ($cx + $k) ($cy - $k)), (P ($cx + $r) $cy),
    (P ($cx + $k) ($cy + $k)), (P ($cx + $k) ($cy + $k)), (P $cx ($cy + $r))
))
$line.CloseFigure()

# Kolečka: x, y, poloměr, barva (null = barva kresby).
# Tři puntíky stavů v jedné řadě; jejich vrch lícuje se špičkou hvězdy.
$dots = @(
    @(196, 104, 23, $Yellow),   # potřebuje pozornost
    @(332, 104, 23, $Blue),     # hotovo
    @(400, 104, 23, $Gray)      # běží; zároveň tečka nad „i“
)
$markBounds = New-Object Drawing.RectangleF 68, 70, 357, 179   # značka včetně tloušťky čáry

function Draw-Mark($g, [Drawing.Color]$ink, [single]$stroke) {
    $pen = New-Object Drawing.Pen $ink, $stroke
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $g.DrawPath($pen, $line)
    foreach ($d in $dots) {
        $c = if ($d[3]) { $d[3] } else { $ink }
        $g.FillEllipse((New-Object Drawing.SolidBrush $c), [single]($d[0] - $d[2]), [single]($d[1] - $d[2]), [single](2 * $d[2]), [single](2 * $d[2]))
    }
}

# --- Nadpis pod značkou, vystředěný ---
$family = New-Object Drawing.FontFamily 'Segoe UI Semibold'
$title = New-Object Drawing.Drawing2D.GraphicsPath
$title.AddString('FlatLine', $family, 0, 110, (P 0 0), [Drawing.StringFormat]::GenericTypographic)
$tb = $title.GetBounds()
$m = New-Object Drawing.Drawing2D.Matrix
$m.Translate([single]($markBounds.X + $markBounds.Width / 2 - $tb.X - $tb.Width / 2), [single]($markBounds.Bottom + 34 - $tb.Y))
$title.Transform($m)
$tb = $title.GetBounds()
$logoBounds = [Drawing.RectangleF]::Union($markBounds, $tb)

function Draw-Logo($g, [Drawing.Color]$ink) {
    Draw-Mark $g $ink 18
    $g.FillPath((New-Object Drawing.SolidBrush $ink), $title)
}

# Vykreslí obsah tak, aby obdélník $src vyplnil $fill podílu plátna a byl vystředěný.
function Fit($g, [Drawing.RectangleF]$src, [int]$size, [single]$fill) {
    $scale = $fill * $size / [Math]::Max($src.Width, $src.Height)
    $g.TranslateTransform([single]($size / 2), [single]($size / 2))
    $g.ScaleTransform($scale, $scale)
    $g.TranslateTransform([single](-$src.X - $src.Width / 2), [single](-$src.Y - $src.Height / 2))
    return $scale
}

function New-Tile([int]$size, [bool]$withTitle) {
    $bmp = New-Object Drawing.Bitmap $size, $size
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([Drawing.Color]::Transparent)

    # Plochá dlaždice s mírně zaoblenými rohy a jemným svislým přechodem (Fluent).
    $rad = [single]($size * 0.08); $dd = 2 * $rad; $e = [single]($size - 1)
    $tile = New-Object Drawing.Drawing2D.GraphicsPath
    $tile.AddArc(0, 0, $dd, $dd, 180, 90); $tile.AddArc($e - $dd, 0, $dd, $dd, 270, 90)
    $tile.AddArc($e - $dd, $e - $dd, $dd, $dd, 0, 90); $tile.AddArc(0, $e - $dd, $dd, $dd, 90, 90)
    $tile.CloseFigure()
    $bg = New-Object Drawing.Drawing2D.LinearGradientBrush (P 0 0), (P 0 $size), $TileTop, $TileBottom
    $g.FillPath($bg, $tile)

    if ($withTitle) {
        [void](Fit $g $logoBounds $size 0.72)
        Draw-Logo $g $Light
    } else {
        $scale = Fit $g $markBounds $size 0.88
        # Na malých velikostech čáru zesílit, aby nezmizela (aspoň ~1,6 px).
        Draw-Mark $g $Light ([Math]::Max(18, 1.6 / $scale))
    }
    $g.Dispose()
    return $bmp
}

function To-Png($bmp) { $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); return , $ms.ToArray() }

$dir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Logo na průhledném pozadí.
foreach ($v in @(@('logo-dark.png', $Light), @('logo-light.png', $Dark))) {
    $pad = 24
    $w = [int]($logoBounds.Width + 2 * $pad); $h = [int]($logoBounds.Height + 2 * $pad)
    $bmp = New-Object Drawing.Bitmap $w, $h
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TranslateTransform([single](-$logoBounds.X + $pad), [single](-$logoBounds.Y + $pad))
    Draw-Logo $g $v[1]
    $bmp.Save((Join-Path $dir $v[0]), [Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

# Dlaždice s nadpisem.
(New-Tile 512 $true).Save((Join-Path $dir 'tile.png'), [Drawing.Imaging.ImageFormat]::Png)

# Ikona: dlaždice bez nadpisu, ICO = hlavička, adresář položek, PNG data za sebou.
$sizes = 16, 24, 32, 48, 64, 256
$pngs = foreach ($s in $sizes) { , (To-Png (New-Tile $s $false)) }
[IO.File]::WriteAllBytes((Join-Path $dir 'icon.png'), $pngs[-1])
$out = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write([byte[]]$p) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $dir 'icon.ico'), $out.ToArray())
'hotovo'
