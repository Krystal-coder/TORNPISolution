Add-Type -AssemblyName System.Drawing
$src = 'C:\Users\S9644\AppData\Roaming\QoderCN\SharedClientCache\cache\images\task-477\l7y7h6gs-19385096.png'
$outDir = 'd:\projcet\TORNPISolution\imgcrop'
$img = [System.Drawing.Image]::FromFile($src)
$w = $img.Width
$h = $img.Height
$secH = 1200
$scale = 2.0
$n = [math]::Ceiling($h / $secH)
for ($i = 0; $i -lt $n; $i++) {
    $y = $i * $secH
    $height = [math]::Min($secH, $h - $y)
    $srcRect = New-Object System.Drawing.Rectangle 0, $y, $w, $height
    $sw = [int]($w * $scale)
    $sh2 = [int]($height * $scale)
    $scaled = New-Object System.Drawing.Bitmap $sw, $sh2
    $g = [System.Drawing.Graphics]::FromImage($scaled)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $destRect = New-Object System.Drawing.Rectangle 0, 0, $sw, $sh2
    $g.DrawImage($img, $destRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
    $dest = Join-Path $outDir ('section_{0:D2}.png' -f $i)
    $scaled.Save($dest, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose()
    $scaled.Dispose()
}
$img.Dispose()
Write-Output "done $n sections"
