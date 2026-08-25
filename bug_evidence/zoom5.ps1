Add-Type -AssemblyName System.Drawing
$src=[System.Drawing.Image]::FromFile('d:\projcet\TORNPISolution\bug_evidence\05_maximized_layout_normal.png')
$bmp=New-Object System.Drawing.Bitmap 1998,600
$g=[System.Drawing.Graphics]::FromImage($bmp)
$g.InterpolationMode='NearestNeighbor'
$g.DrawImage($src,(New-Object System.Drawing.Rectangle 0,0,1998,600),(New-Object System.Drawing.Rectangle 690,60,999,300),([System.Drawing.GraphicsUnit]::Pixel))
$g.Dispose()
$bmp.Save('d:\projcet\TORNPISolution\bug_evidence\zoom_max_detail2.png')
$bmp.Dispose()
$src.Dispose()
