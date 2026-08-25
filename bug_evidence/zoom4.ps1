Add-Type -AssemblyName System.Drawing
$src=[System.Drawing.Image]::FromFile('d:\projcet\TORNPISolution\bug_evidence\00_initial_library_expanded.png')
$bmp=New-Object System.Drawing.Bitmap 1280,1440
$g=[System.Drawing.Graphics]::FromImage($bmp)
$g.InterpolationMode='NearestNeighbor'
$g.DrawImage($src,(New-Object System.Drawing.Rectangle 0,0,1280,1440),(New-Object System.Drawing.Rectangle 640,0,640,720),([System.Drawing.GraphicsUnit]::Pixel))
$g.Dispose()
$bmp.Save('d:\projcet\TORNPISolution\bug_evidence\zoom_00_R.png')
$bmp.Dispose()
$src.Dispose()
