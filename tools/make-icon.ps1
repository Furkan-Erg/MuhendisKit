# Draws the app icon (blue tile, white set-square + flame-free "M" mark) and writes a multi-size PNG ICO.
param([string]$Out = "$PSScriptRoot\..\src\MuhendisKit\Assets\app.ico")
Add-Type -AssemblyName System.Drawing
function Draw([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
    $r = [single]($s * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $w = [single]($s - 1)
    $path.AddArc(0, 0, $r, $r, 180, 90); $path.AddArc($w - $r, 0, $r, $r, 270, 90)
    $path.AddArc($w - $r, $w - $r, $r, $r, 0, 90); $path.AddArc(0, $w - $r, $r, $r, 90, 90); $path.CloseFigure()
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $s, $s), ([System.Drawing.Color]::FromArgb(255, 47, 111, 222)), ([System.Drawing.Color]::FromArgb(255, 22, 64, 150))
    $g.FillPath($brush, $path)
    # set-square (right triangle) outline
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([single]([Math]::Max(1.5, $s * 0.075)))
    $pen.LineJoin = 'Round'
    $a = [single]($s * 0.24); $b = [single]($s * 0.76)
    $tri = [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF $a, $a), (New-Object System.Drawing.PointF $a, $b), (New-Object System.Drawing.PointF $b, $b))
    $g.DrawPolygon($pen, $tri)
    if ($s -ge 32) {
        # ruler ticks along the bottom edge
        $thin = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([single]([Math]::Max(1, $s * 0.035)))
        foreach ($i in 1..3) { $x = [single]($a + ($b - $a) * $i / 4); $g.DrawLine($thin, $x, $b, $x, [single]($b - $s * 0.08)) }
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    return ,$ms.ToArray()
}
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = $sizes | ForEach-Object { ,(Draw $_) }
$fs = [System.IO.File]::Create($Out); $bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Dispose()
"wrote $Out"
