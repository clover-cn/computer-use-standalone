$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path @((Join-Path $TestRoot '../native/Windows.cs'), (Join-Path $TestRoot '../native/Feedback.cs')) -ReferencedAssemblies System.Drawing,System.Windows.Forms
[DesktopNative]::Initialize()
# 离线检查真实渲染函数，不读取用户桌面。
$image = [DesktopFeedback]::RenderImage(1280,720,'Agent 正在控制电脑')
try {
    if ($image.GetPixel(640,360).A -ne 0) { throw '屏幕中心必须透明' }
    if ($image.GetPixel(2,360).A -le $image.GetPixel(85,360).A) { throw '边缘未向内部渐隐' }
    if ($image.GetPixel(2,360).A -gt 35 -or $image.GetPixel(30,360).A -ne 0) { throw '渐变太深或超出 28 像素边缘' }
    if ($image.GetPixel(640,30).A -lt 200) { throw '居中提示条未绘制' }
    $artifactDir = Join-Path $TestRoot '../artifacts'
    $null = New-Item -ItemType Directory -Path $artifactDir -Force
    $image.Save((Join-Path $artifactDir 'session-overlay.png'),[System.Drawing.Imaging.ImageFormat]::Png)
    # 合成预览只使用虚构桌面背景，避免暴露用户画面。
    $preview = New-Object System.Drawing.Bitmap(1280,720)
    $g = [System.Drawing.Graphics]::FromImage($preview)
    try {
        $g.Clear([System.Drawing.Color]::FromArgb(237,242,248))
        $panel = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
        $g.FillRectangle($panel,190,150,900,460)
        $panel.Dispose()
        $font = New-Object System.Drawing.Font('Microsoft YaHei UI',20)
        $g.DrawString('Agent 控制界面预览',$font,[System.Drawing.Brushes]::DimGray,235,200)
        $font.Dispose()
        $g.DrawImage($image,0,0)
        $cursor = [DesktopFeedback]::CursorImage()
        try {
            if ($cursor.GetPixel(30,25).A -ne 0) { throw '指针外不应有底圈' }
            $g.DrawImage($cursor,590,350); $cursor.Save((Join-Path $artifactDir 'agent-cursor.png'))
        } finally { $cursor.Dispose() }
        $preview.Save((Join-Path $artifactDir 'session-preview.png'),[System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $g.Dispose(); $preview.Dispose() }
} finally { $image.Dispose() }
# 取消测试关闭视觉与系统指针改动，仍安装真实键盘监听；不发送输入。
$env:COMPUTER_USE_FEEDBACK = '0'
try {
    [DesktopFeedback]::Begin()
    Start-Sleep -Milliseconds 1100
    if (-not [DesktopFeedback]::Active) { throw '控制会话不应在 0.9 秒后消失' }
    if ([DesktopFeedback]::HandleEscape(27,0x10,0x100)) { throw 'Agent 注入的 Esc 不应取消用户会话' }
    if (-not [DesktopFeedback]::HandleEscape(27,0,0x100)) { throw '物理 Esc 未锁定输入' }
    try { [DesktopNative]::Click(1,0,0,'left'); throw '取消后仍允许点击' } catch {
        if ($_.Exception.Message -notmatch 'Esc') { throw }
    }
    try { [DesktopNative]::Type(1,'不应发送'); throw '取消后仍允许输入' } catch {
        if ($_.Exception.Message -notmatch 'Esc') { throw }
    }
    [DesktopFeedback]::End()
    try { [DesktopFeedback]::Begin(); throw '不应自动恢复控制' } catch {
        if ($_.Exception.Message -notmatch 'Esc') { throw }
    }
    if ([DesktopFeedback]::Active) { throw '退出后仍处于控制状态' }
    [Console]::WriteLine('PASS: 渐变、居中提示、指针预览、持续会话、物理 Esc 与注入区分、取消后输入锁定')
} finally { [DesktopFeedback]::Close() }
