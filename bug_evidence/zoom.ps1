Add-Type -AssemblyName System.Drawing
$src=[System.Drawing.Image]::FromFile('d:\projcet\TORNPISolution\bug_evidence\02_A_init_detail_normal.png')
$bmp=New-Object System.Drawing.Bitmap 2400,1000
$g=[System.Drawing.Graphics]::FromImage($bmp)
$g.InterpolationMode='NearestNeighbor'
$g.DrawImage($src,(New-Object System.Drawing.Rectangle 0,0,2400,1000),(New-Object System.Drawing.Rectangle 640,140,600,250),([System.Drawing.GraphicsUnit]::Pixel))
$g.Dispose()
$bmp.Save('d:\projcet\TORNPISolution\bug_evidence\zoom_detail_region.png')
$bmp.Dispose()
$src.Dispose()
