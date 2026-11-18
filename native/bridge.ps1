$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
try {
    Add-Type -AssemblyName System.Drawing
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    Add-Type -Path @((Join-Path $PSScriptRoot 'Windows.cs'), (Join-Path $PSScriptRoot 'Feedback.cs')) -ReferencedAssemblies System.Drawing,System.Windows.Forms
    [DesktopNative]::Initialize()
} catch { [Console]::Error.WriteLine($_.ToString()); exit 1 }

function Read-Controls([long]$WindowId) {
    $root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$WindowId)
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $queue = New-Object System.Collections.Queue
    $queue.Enqueue(@{ element = $root; depth = 0 })
    $result = New-Object System.Collections.ArrayList
    while ($queue.Count -gt 0 -and $result.Count -lt 300) {
        $entry = $queue.Dequeue()
        try {
            $c = $entry.element.Current
            # 密码控件不读取名称或值。
            if ($c.IsPassword) { continue }
            $null = $result.Add(@{ name = $c.Name; type = $c.ControlType.ProgrammaticName; focused = $c.HasKeyboardFocus; depth = $entry.depth })
            if ($entry.depth -lt 8) {
                $child = $walker.GetFirstChild($entry.element)
                while ($null -ne $child -and $queue.Count -lt 600) {
                    $queue.Enqueue(@{ element = $child; depth = $entry.depth + 1 })
                    $child = $walker.GetNextSibling($child)
                }
            }
        } catch { }
    }
    return ,$result.ToArray()
}

while ($null -ne ($line = [Console]::ReadLine())) {
    try {
        $req = ConvertFrom-Json -InputObject $line
        $a = $req.args
        $value = switch ($req.op) {
            'doctor' { @{ platform = 'win32'; backend = 'PrintWindow + UIAutomation + SendInput'; inputSize = 'native'; ready = $true; visual_feedback = [DesktopFeedback]::Enabled; control_active = [DesktopFeedback]::Active; control_cancelled = [DesktopFeedback]::Cancelled } }
            'end_control' { [DesktopFeedback]::End(); @{ control_active = [DesktopFeedback]::Active; control_cancelled = [DesktopFeedback]::Cancelled } }
            'start_control' { [DesktopFeedback]::Begin(); @{ control_active = [DesktopFeedback]::Active; control_cancelled = [DesktopFeedback]::Cancelled } }
            'list_windows' { @([DesktopNative]::List()) }
            'inspect' { [DesktopNative]::Inspect([long]$a.window_id) }
            'observe' {
                if ([DesktopFeedback]::Active) { [DesktopFeedback]::Touch() }
                $before = [DesktopNative]::Inspect([long]$a.window_id)
                $v = @{ window = $before }
                if ($a.include_screenshot) {
                    $region = if ($null -ne $a.region) { $a.region } else { @{ x=0; y=0; width=$before.width; height=$before.height } }
                    $scale = if ($null -ne $a.scale) { [int]$a.scale } else { 1 }
                    if ($scale -eq 1 -and $region.x -eq 0 -and $region.y -eq 0 -and $region.width -eq $before.width -and $region.height -eq $before.height) {
                        $v.image = [DesktopNative]::Capture([long]$a.window_id)
                    } else {
                        $v.image = [DesktopNative]::CaptureRegion([long]$a.window_id,$region.x,$region.y,$region.width,$region.height,$scale)
                    }
                    $v.image_region = @{ x=[int]$region.x; y=[int]$region.y; width=[int]$region.width; height=[int]$region.height; scale=$scale }
                }
                if ($a.include_text) { $v.controls = Read-Controls ([long]$a.window_id) }
                $after = [DesktopNative]::Inspect([long]$a.window_id)
                if (($before | ConvertTo-Json -Compress) -ne ($after | ConvertTo-Json -Compress)) { throw '观察期间窗口发生变化，请重新观察' }
                $v
            }
            'click' { [DesktopNative]::Click($a.window_id,$a.x,$a.y,$a.button); @{ sent = $true } }
            'type_text' { [DesktopNative]::Type($a.window_id,$a.text); @{ sent = $true } }
            'press_key' { [DesktopNative]::Press($a.window_id,[int[]]$a.keys); @{ sent = $true } }
            'scroll' { [DesktopNative]::Scroll($a.window_id,$a.x,$a.y,$a.delta); @{ sent = $true } }
            'drag' { [DesktopNative]::Drag($a.window_id,$a.x,$a.y,$a.to_x,$a.to_y); @{ sent = $true } }
            default { throw '未知操作' }
        }
        @{ id = $req.id; result = $value } | ConvertTo-Json -Depth 15 -Compress | ForEach-Object { [Console]::WriteLine($_) }
    } catch {
        @{ id = $req.id; error = $_.Exception.Message } | ConvertTo-Json -Compress | ForEach-Object { [Console]::WriteLine($_) }
    }
}
[DesktopFeedback]::Close()
