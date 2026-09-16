Add-Type -AssemblyName System.Drawing
$src = 'C:\Users\S9644\AppData\Roaming\QoderCN\SharedClientCache\cache\images\task-477\l7y7h6gs-19385096.png'
$img = [System.Drawing.Image]::FromFile($src)
$y = 5550
$height = 350
$srcRect = New-Object System.Drawing.Rectangle 0, $y, $img.Width, $height
$scale = 4
$scaled = New-Object System.Drawing.Bitmap ($img.Width * $scale), ($height * $scale)
$g = [System.Drawing.Graphics]::FromImage($scaled)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$destRect = New-Object System.Drawing.Rectangle 0, 0, $scaled.Width, $scaled.Height
$g.DrawImage($img, $destRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
$dest = 'd:\projcet\TORNPISolution\imgcrop\row88_4x_b.png'
$scaled.Save($dest, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $scaled.Dispose(); $img.Dispose()
Write-Output saved
