Add-Type -AssemblyName System.Drawing
$src=[System.Drawing.Image]::FromFile('d:\projcet\TORNPISolution\bug_evidence\04_C_recovered_after_reselect.png')
$bmp=New-Object System.Drawing.Bitmap 2320,1600
$g=[System.Drawing.Graphics]::FromImage($bmp)
$g.InterpolationMode='NearestNeighbor'
$g.DrawImage($src,(New-Object System.Drawing.Rectangle 0,0,2320,1600),(New-Object System.Drawing.Rectangle 700,130,580,400),([System.Drawing.GraphicsUnit]::Pixel))
$g.Dispose()
$bmp.Save('d:\projcet\TORNPISolution\bug_evidence\zoom_detail_region2.png')
$bmp.Dispose()
$src.Dispose()
