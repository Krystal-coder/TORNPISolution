Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Media.Ocr.OcrEngine, Windows.Media, Version=255.255.255.255, Culture=neutral, PublicKeyToken=null]
$engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
if ($engine -eq $null) { throw "OCR engine failed" }

function Load-Bitmap {
    param($path)
    $file = [System.IO.File]::OpenRead($path)
    $ms = New-Object System.IO.MemoryStream
    $file.CopyTo($ms)
    $file.Dispose()
    $ms.Position = 0
    $bmp = [Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($ms).AsTask().Result
    return $bmp.GetSoftwareBitmapAsync().AsTask().Result
}

$bitmap = Load-Bitmap 'C:\Users\S9644\AppData\Roaming\QoderCN\SharedClientCache\cache\images\task-477\l7y7h6gs-19385096.png'
$result = $engine.RecognizeAsync($bitmap).AsTask().Result
foreach ($line in $result.Lines) {
    Write-Output $line.Text
}
