# Vykreslí logo FlatLine: nápis, kde tečku nad „i“ nahrazuje čtyřcípá AI hvězdička.
# Výstup: assets/logo-dark.png (světlý text pro tmavé pozadí) a assets/logo-light.png.
# Použití: powershell -NoProfile -File assets/make-logo.ps1
Add-Type -AssemblyName System.Drawing

$text = 'FlatL' + [char]0x0131 + 'ne'   # ı = i bez tečky
$iIndex = 5
$em = 200
$family = New-Object Drawing.FontFamily 'Segoe UI Semibold'
$font = New-Object Drawing.Font $family, $em, ([Drawing.FontStyle]::Regular), ([Drawing.GraphicsUnit]::Pixel)
$fmt = [Drawing.StringFormat]::GenericTypographic
$origin = New-Object Drawing.PointF 40, 40

# Tvar nápisu.
$word = New-Object Drawing.Drawing2D.GraphicsPath
$word.AddString($text, $family, 0, $em, $origin, $fmt)

# Kde leží „ı“: vodorovně podle rozsahu znaku, svisle podle horní hrany jeho glyfu.
$tmp = New-Object Drawing.Bitmap 10, 10
$tg = [Drawing.Graphics]::FromImage($tmp)
$fmtR = $fmt.Clone()
$fmtR.SetMeasurableCharacterRanges([Drawing.CharacterRange[]]@((New-Object Drawing.CharacterRange $iIndex, 1)))
$layout = New-Object Drawing.RectangleF $origin.X, $origin.Y, 4000, 1000
$iRect = $tg.MeasureCharacterRanges($text, $font, $layout, $fmtR)[0].GetBounds($tg)
$iPath = New-Object Drawing.Drawing2D.GraphicsPath
$iPath.AddString([string][char]0x0131, $family, 0, $em, $origin, $fmt)
$iTop = $iPath.GetBounds().Top

# Glyf „ı“ uvnitř svého rozsahu: střed svislého tahu.
$iGlyph = New-Object Drawing.Drawing2D.GraphicsPath
$iGlyph.AddString([string][char]0x0131, $family, 0, $em, (New-Object Drawing.PointF $iRect.X, $origin.Y), $fmt)
$gb = $iGlyph.GetBounds()
$cx = $gb.X + $gb.Width / 2

# Čtyřcípá hvězdička se zaoblenými prohlubněmi (jako u Gemini).
$r = $em * 0.17
$cy = $iTop - $r * 0.55 - $em * 0.06
$k = $r * 0.2
$star = New-Object Drawing.Drawing2D.GraphicsPath
$pts = @(
    @(($cx), ($cy - $r)), @(($cx + $k), ($cy - $k)), @(($cx + $k), ($cy - $k)), @(($cx + $r), ($cy)),
    @(($cx + $k), ($cy + $k)), @(($cx + $k), ($cy + $k)), @(($cx), ($cy + $r)),
    @(($cx - $k), ($cy + $k)), @(($cx - $k), ($cy + $k)), @(($cx - $r), ($cy)),
    @(($cx - $k), ($cy - $k)), @(($cx - $k), ($cy - $k)), @(($cx), ($cy - $r))
)
$bez = [Drawing.PointF[]]($pts | ForEach-Object { New-Object Drawing.PointF ([single]$_[0]), ([single]$_[1]) })
$star.AddBeziers($bez)
$star.CloseFigure()

# Ořez na obsah.
$b1 = $word.GetBounds(); $b2 = $star.GetBounds()
$pad = 24
$left = [Math]::Floor([Math]::Min($b1.Left, $b2.Left)) - $pad
$top = [Math]::Floor([Math]::Min($b1.Top, $b2.Top)) - $pad
$w = [int]([Math]::Ceiling([Math]::Max($b1.Right, $b2.Right)) - $left + $pad)
$h = [int]([Math]::Ceiling([Math]::Max($b1.Bottom, $b2.Bottom)) - $top + $pad)

$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
foreach ($variant in @(@('logo-dark.png', [Drawing.Color]::FromArgb(0xF4, 0xF4, 0xF5)), @('logo-light.png', [Drawing.Color]::FromArgb(0x1E, 0x1F, 0x22)))) {
    $bmp = New-Object Drawing.Bitmap $w, $h
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([Drawing.Color]::Transparent)
    $g.TranslateTransform(-$left, -$top)
    $g.FillPath((New-Object Drawing.SolidBrush $variant[1]), $word)

    # Hvězdička s přechodem modrá → fialová → růžová.
    $grad = New-Object Drawing.Drawing2D.LinearGradientBrush ((New-Object Drawing.PointF ($cx - $r), ($cy - $r))), ((New-Object Drawing.PointF ($cx + $r), ($cy + $r))), ([Drawing.Color]::FromArgb(0x42, 0x85, 0xF4)), ([Drawing.Color]::FromArgb(0xD9, 0x65, 0x70))
    $blend = New-Object Drawing.Drawing2D.ColorBlend 3
    $blend.Colors = @([Drawing.Color]::FromArgb(0x42, 0x85, 0xF4), [Drawing.Color]::FromArgb(0x9B, 0x72, 0xCB), [Drawing.Color]::FromArgb(0xD9, 0x65, 0x70))
    $blend.Positions = @(0.0, 0.5, 1.0)
    $grad.InterpolationColors = $blend
    $g.FillPath($grad, $star)

    $bmp.Save((Join-Path $dir $variant[0]), [Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}
"logo: $w x $h"
