$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path @((Join-Path $TestRoot '../native/Windows.cs'), (Join-Path $TestRoot '../native/Feedback.cs')) -ReferencedAssemblies System.Drawing,System.Windows.Forms
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
public static class SessionTest {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr LoadCursor(IntPtr instance,IntPtr id);
    [DllImport("user32.dll")] static extern bool DrawIconEx(IntPtr dc,int x,int y,IntPtr icon,int w,int h,uint step,IntPtr brush,uint flags);
    public static string Fingerprint(int id) {
        using(Bitmap image=new Bitmap(40,40)) {
            using(Graphics graphics=Graphics.FromImage(image)) {
                IntPtr dc=graphics.GetHdc();
                try { DrawIconEx(dc,0,0,LoadCursor(IntPtr.Zero,new IntPtr(id)),40,40,0,IntPtr.Zero,3); }
                finally { graphics.ReleaseHdc(dc); }
            }
            using(MemoryStream stream=new MemoryStream()) { image.Save(stream,System.Drawing.Imaging.ImageFormat.Png); return Convert.ToBase64String(stream.ToArray()); }
        }
    }
}
'@
[DesktopNative]::Initialize()
$env:COMPUTER_USE_FEEDBACK = '1'
$foreground = [SessionTest]::GetForegroundWindow()
$before = @{}
foreach ($id in @(32512,32513,32649)) { $before[$id] = [SessionTest]::Fingerprint($id) }
try {
    [DesktopFeedback]::Begin()
    if (-not [DesktopFeedback]::Active -or -not [DesktopFeedback]::Visible) { throw '持续会话未启动' }
    if ([SessionTest]::GetForegroundWindow() -ne $foreground) { throw '提示层抢占焦点' }
    foreach ($id in @(32512,32513,32649)) {
        if ([SessionTest]::Fingerprint($id) -eq $before[$id]) { throw '系统指针没有切换为 Agent 样式' }
    }
    Start-Sleep -Milliseconds 1100
    if (-not [DesktopFeedback]::Active) { throw '提示层过早结束' }
    [DesktopFeedback]::End()
    foreach ($id in @(32512,32513,32649)) {
        if ([SessionTest]::Fingerprint($id) -ne $before[$id]) { throw '系统指针未恢复原样' }
    }
    [DesktopFeedback]::Begin()
    if ([DesktopFeedback]::IdleTimeoutSeconds -ne 300) { throw '空闲兜底必须保持 5 分钟' }
    # 仅在测试中模拟超过 5 分钟的时间，避免实际占用桌面等待。
    $touchedField = [DesktopFeedback].GetField('touched',[System.Reflection.BindingFlags]'NonPublic,Static')
    $touchedField.SetValue($null,[DateTime]::UtcNow.AddSeconds(-301).Ticks)
    Start-Sleep -Milliseconds 300
    if ([DesktopFeedback]::Active) { throw '客户端漏收尾时，空闲兜底未结束会话' }
    if ([DesktopFeedback]::Cancelled) { throw '空闲清理不应冒充用户 Esc 取消' }
    foreach ($id in @(32512,32513,32649)) {
        if ([SessionTest]::Fingerprint($id) -ne $before[$id]) { throw '空闲兜底未恢复指针' }
    }
    $node = (Get-Command node).Source
    $fixture = Join-Path $TestRoot 'disconnect-fixture.mjs'
    & $node $fixture
    if ($LASTEXITCODE -ne 0) { throw '真实 MCP 断连测试进程失败' }
    $restored = $false
    for ($n=0; $n -lt 20; $n++) {
        $restored = $true
        foreach ($id in @(32512,32513,32649)) {
            if ([SessionTest]::Fingerprint($id) -ne $before[$id]) { $restored = $false }
        }
        if ($restored) { break }
        Start-Sleep -Milliseconds 100
    }
    if (-not $restored) { throw '客户端未调用 end_control 就断连时，指针仍未恢复' }
    [Console]::WriteLine('PASS: 真实会话、焦点保持、指针恢复、5 分钟兜底（模拟时间）及真实 MCP 未收尾断连清理；无鼠标键盘输入')
} finally { [DesktopFeedback]::Close() }
