$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path @((Join-Path $TestRoot '../native/Windows.cs'), (Join-Path $TestRoot '../native/Feedback.cs')) -ReferencedAssemblies System.Drawing,System.Windows.Forms
[DesktopNative]::Initialize()
$form = New-Object System.Windows.Forms.Form
try {
    $form.Text = '独立截图测试'
    $form.StartPosition = 'Manual'
    $form.Location = New-Object System.Drawing.Point(-10000, -10000)
    $form.ClientSize = New-Object System.Drawing.Size(180, 100)
    $form.BackColor = [System.Drawing.Color]::FromArgb(20, 100, 200)
    $form.ShowInTaskbar = $false
    $form.Show()
    [System.Windows.Forms.Application]::DoEvents()
    $info = [DesktopNative]::Inspect($form.Handle.ToInt64())
    $bytes = [Convert]::FromBase64String([DesktopNative]::Capture($form.Handle.ToInt64()))
    $stream = New-Object System.IO.MemoryStream(,$bytes)
    $bitmap = [System.Drawing.Bitmap]::FromStream($stream)
    try {
        if ($bitmap.Width -ne 180 -or $bitmap.Height -ne 100) { throw '客户区尺寸不匹配' }
        $pixel = $bitmap.GetPixel(90,50)
        if ($pixel.R -ne 20 -or $pixel.G -ne 100 -or $pixel.B -ne 200) { throw '截图颜色不匹配，可能为空白图' }
        if ($info.width -ne 180 -or $info.height -ne 100) { throw '窗口坐标尺寸不匹配' }
        $cropBytes = [Convert]::FromBase64String([DesktopNative]::CaptureRegion($form.Handle.ToInt64(),10,20,30,25,3))
        $cropStream = New-Object System.IO.MemoryStream(,$cropBytes)
        $crop = [System.Drawing.Bitmap]::FromStream($cropStream)
        try {
            if ($crop.Width -ne 90 -or $crop.Height -ne 75 -or $crop.GetPixel(45,30).B -ne 200) { throw '局部放大截图不匹配' }
        } finally { $crop.Dispose(); $cropStream.Dispose() }
        [Console]::WriteLine('PASS: 180x100 客户区 PNG 与预期像素匹配；没有捕获用户窗口或发送输入')
    } finally { $bitmap.Dispose(); $stream.Dispose() }
} finally { $form.Close(); $form.Dispose() }
