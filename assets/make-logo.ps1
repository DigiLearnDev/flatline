# Vykreslí logo FlatLine: značku „A✦i“ a vedle ní nápis.
# Značka je jedna plochá čára (flatline): zleva vyskočí „A“, uprostřed se zvedne
# do čtyřcípé AI hvězdy jako tep na EKG a vpravo skončí písmenem „i“.
# Výstup: assets/logo-dark.png (světlé pro tmavé pozadí), assets/logo-light.png
# a samotná značka assets/mark-dark.png, assets/mark-light.png.
# Použití: powershell -NoProfile -File assets/make-logo.ps1
Add-Type -AssemblyName System.Drawing

function P([single]$x, [single]$y) { New-Object Drawing.PointF $x, $y }

$base = 240        # výška ploché čáry
$stroke = 18

# --- Značka (souřadnice podle skici) ---

# Čára: A, rovně k hvězdě, za hvězdou rovně k „i“ a nahoru do jeho dříku.
$line = New-Object Drawing.Drawing2D.GraphicsPath
$line.AddLines([Drawing.PointF[]]@((P 70 $base), (P 118 172), (P 166 $base), (P 262 $base)))
$line.StartFigure()
$line.AddLines([Drawing.PointF[]]@((P 262 $base), (P 400 $base), (P 400 178)))
$crossbar = New-Object Drawing.Drawing2D.GraphicsPath
$crossbar.AddLine((P 96 210), (P 140 210))

# Hvězda: dolní cíp sedí na čáře. Prohnuté strany = Bézierovy křivky s řídicími body u středu.
$cx = 262; $cy = 165; $r = 75; $k = 14
$star = New-Object Drawing.Drawing2D.GraphicsPath
$star.AddBeziers([Drawing.PointF[]]@(
    (P $cx ($cy + $r)), (P ($cx - $k) ($cy + $k)), (P ($cx - $k) ($cy + $k)), (P ($cx - $r) $cy),
    (P ($cx - $k) ($cy - $k)), (P ($cx - $k) ($cy - $k)), (P $cx ($cy - $r)),
    (P ($cx + $k) ($cy - $k)), (P ($cx + $k) ($cy - $k)), (P ($cx + $r) $cy),
    (P ($cx + $k) ($cy + $k)), (P ($cx + $k) ($cy + $k)), (P $cx ($cy + $r))
))
$star.CloseFigure()

$dot = @(400, 138, 13)   # tečka nad „i“: x, y, poloměr

function Draw-Mark($g, [Drawing.Color]$ink) {
    $pen = New-Object Drawing.Pen $ink, $stroke
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $g.DrawPath($pen, $line)
    $g.DrawPath($pen, $crossbar)
    $g.FillEllipse((New-Object Drawing.SolidBrush $ink), [single]($dot[0] - $dot[2]), [single]($dot[1] - $dot[2]), [single](2 * $dot[2]), [single](2 * $dot[2]))

    $grad = New-Object Drawing.Drawing2D.LinearGradientBrush (P ($cx - $r) ($cy - $r)), (P ($cx + $r) ($cy + $r)), ([Drawing.Color]::Blue), ([Drawing.Color]::Red)
    $blend = New-Object Drawing.Drawing2D.ColorBlend 3
    $blend.Colors = @([Drawing.Color]::FromArgb(0x42, 0x85, 0xF4), [Drawing.Color]::FromArgb(0x9B, 0x72, 0xCB), [Drawing.Color]::FromArgb(0xD9, 0x65, 0x70))
    $blend.Positions = @(0.0, 0.5, 1.0)
    $grad.InterpolationColors = $blend
    $spen = New-Object Drawing.Pen $grad, $stroke
    $spen.LineJoin = 'Round'
    $g.DrawPath($spen, $star)
}

# --- Nápis, účaří zarovnané s plochou čárou ---
$em = 190
$family = New-Object Drawing.FontFamily 'Segoe UI Semibold'
$ascent = $family.GetCellAscent([Drawing.FontStyle]::Regular) / $family.GetEmHeight([Drawing.FontStyle]::Regular) * $em
$textX = 470
$word = New-Object Drawing.Drawing2D.GraphicsPath
$word.AddString('FlatLine', $family, 0, $em, (P $textX ($base - $ascent + $stroke / 2)), [Drawing.StringFormat]::GenericTypographic)

$pad = 30
$markLeft = 70 - $stroke; $markTop = $cy - $r - $stroke; $markRight = 400 + $stroke; $markBottom = $base + $stroke
$wb = $word.GetBounds()

function Save-Png([string]$file, [single]$left, [single]$top, [single]$right, [single]$bottom, [Drawing.Color]$ink, [bool]$withText) {
    $w = [int]($right - $left + 2 * $pad); $h = [int]($bottom - $top + 2 * $pad)
    $bmp = New-Object Drawing.Bitmap $w, $h
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([Drawing.Color]::Transparent)
    $g.TranslateTransform(-$left + $pad, -$top + $pad)
    Draw-Mark $g $ink
    if ($withText) { $g.FillPath((New-Object Drawing.SolidBrush $ink), $word) }
    $bmp.Save((Join-Path $script:dir $file), [Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

$script:dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$light = [Drawing.Color]::FromArgb(0xF4, 0xF4, 0xF5)
$dark = [Drawing.Color]::FromArgb(0x1E, 0x1F, 0x22)
$allTop = [Math]::Min($markTop, $wb.Top); $allBottom = [Math]::Max($markBottom, $wb.Bottom)
Save-Png 'logo-dark.png'  $markLeft $allTop $wb.Right $allBottom $light $true
Save-Png 'logo-light.png' $markLeft $allTop $wb.Right $allBottom $dark  $true
Save-Png 'mark-dark.png'  $markLeft $markTop $markRight $markBottom $light $false
Save-Png 'mark-light.png' $markLeft $markTop $markRight $markBottom $dark  $false
'hotovo'
