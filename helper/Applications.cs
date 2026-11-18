using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

static class Applications {
    public static object Launch(string app) {
        string executable=app switch {"paint"=>"mspaint.exe","notepad"=>"notepad.exe","calculator"=>"calc.exe",_=>throw new Exception("不在允许启动的应用列表中")};
        string[] names=app switch {"paint"=>new[]{"mspaint","Paint"},"notepad"=>new[]{"notepad"},_=>new[]{"CalculatorApp","Calculator","calc"}};
        var before=DesktopNative.List().Select(w=>w.id).ToHashSet();
        string path=Path.Combine(Environment.SystemDirectory,executable);
        string alias=Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA")??"", "Microsoft","WindowsApps",executable);
        if(!File.Exists(path))path=alias;
        DesktopFeedback.Check();DesktopFeedback.Begin();DesktopFeedback.Check();
        // 只启动固定的系统入口，不拼接命令、路径或用户参数，也不提权。
        // AppExecutionAlias 是重解析点，File.Exists 可能无法识别，交由 Windows 解析固定别名。
        using var process=Process.Start(new ProcessStartInfo(path){UseShellExecute=true,WindowStyle=ProcessWindowStyle.Normal});
        var clock=Stopwatch.StartNew();
        DesktopNative.WindowInfo[] windows=Array.Empty<DesktopNative.WindowInfo>();
        while(clock.ElapsedMilliseconds<10000) {
            DesktopFeedback.Check();DesktopFeedback.Touch();
            windows=DesktopNative.List().Where(w=>names.Contains(w.process,StringComparer.OrdinalIgnoreCase)).OrderBy(w=>before.Contains(w.id)).ToArray();
            if(windows.Any(w=>!before.Contains(w.id))||(windows.Length>0&&clock.ElapsedMilliseconds>=1500))break;
            Thread.Sleep(100);
        }
        return new {launch_requested=true,window_found=windows.Length>0,app,windows,reused_window=windows.Length>0&&before.Contains(windows[0].id),
            next="窗口可能包含用户已有内容；先 observe 确认，不能清空或覆盖。没有返回窗口时重新 list_windows，不要盲目重复启动。"};
    }
}
