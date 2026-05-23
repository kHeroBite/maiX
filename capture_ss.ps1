[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
$tmpPath = $env:TEMP + '\ss_20260509_234841.png'
$img = $null
try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    $null = [Windows.ApplicationModel.DataTransfer.Clipboard,Windows.ApplicationModel.DataTransfer,ContentType=WindowsRuntime]
    $op = [Windows.ApplicationModel.DataTransfer.Clipboard]::GetHistoryItemsAsync()
    $task = [System.WindowsRuntimeSystemExtensions]::AsTask($op)
    $task.Wait(3000) | Out-Null
    if ($task.IsCompleted -and $task.Result.Status -eq 0) {
        foreach ($item in $task.Result.Items) {
            $dp = $item.Content
            if ($dp.Contains('image/png') -or $dp.Contains('image/jpeg') -or $dp.Contains([Windows.ApplicationModel.DataTransfer.StandardDataFormats,Windows.ApplicationModel.DataTransfer,ContentType=WindowsRuntime]::Bitmap)) {
                $bmpOp = $dp.GetBitmapAsync()
                $bmpTask = [System.WindowsRuntimeSystemExtensions]::AsTask($bmpOp)
                $bmpTask.Wait(3000) | Out-Null
                if ($bmpTask.IsCompleted) {
                    $stream = $bmpTask.Result.OpenReadAsync()
                    $stTask = [System.WindowsRuntimeSystemExtensions]::AsTask($stream)
                    $stTask.Wait(3000) | Out-Null
                    $netStream = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($stTask.Result)
                    $img = [System.Drawing.Image]::FromStream($netStream)
                    break
                }
            }
        }
    }
} catch { }
if ($img -eq $null) {
    $data = [System.Windows.Forms.Clipboard]::GetDataObject()
    if ($data -ne $null) {
        if ($data.GetDataPresent([System.Drawing.Bitmap])) {
            $img = $data.GetData([System.Drawing.Bitmap])
        } elseif ($data.GetDataPresent([System.Windows.Forms.DataFormats]::Bitmap)) {
            $img = $data.GetData([System.Windows.Forms.DataFormats]::Bitmap)
        } elseif ($data.GetDataPresent([System.Windows.Forms.DataFormats]::Dib)) {
            $img = $data.GetData([System.Windows.Forms.DataFormats]::Dib)
        }
    }
}
if ($img -ne $null) {
    $img.Save($tmpPath, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host ('CLIPBOARD:' + $img.Width + 'x' + $img.Height + ':' + $tmpPath)
} else {
    Write-Host 'ERROR:NO_IMAGE'
}
