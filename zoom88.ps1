Add-Type -AssemblyName System.Drawing
$src = 'C:\Users\S9644\AppData\Roaming\QoderCN\SharedClientCache\cache\images\task-477\l7y7h6gs-19385096.png'
$img = [System.Drawing.Image]::FromFile($src)
# Row 65 is roughly between y=?? in original (1652x6485)
# We'll crop a large vertical region around where 0x88 appears and scale 4x.
# Based on section_04 (y=4800 in original ~ 0-1200*4=4800-6000). Row 65 near y~5200.
$y = 5100
$height = 400
$srcRect = New-Object System.Drawing.Rectangle 0, $y, $img.Width, $height
$scale = 4
$scaled = New-Object System.Drawing.Bitmap ($img.Width * $scale), ($height * $scale)
$g = [System.Drawing.Graphics]::FromImage($scaled)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$destRect = New-Object System.Drawing.Rectangle 0, 0, $scaled.Width, $scaled.Height
$g.DrawImage($img, $destRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
$dest = 'd:\projcet\TORNPISolution\imgcrop\row88_4x.png'
$scaled.Save($dest, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $scaled.Dispose(); $img.Dispose()
Write-Output saved
