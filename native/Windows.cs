using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public static class DesktopNative {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint data, flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] struct UNION { [FieldOffset(0)] public MOUSEINPUT mouse; [FieldOffset(0)] public KEYBDINPUT key; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public UNION value; }
    public class WindowInfo { public long id; public uint pid; public string title, process; public int x, y, width, height; }
    delegate bool EnumProc(IntPtr h, IntPtr param);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr param);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder text, int count);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT rect);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT point);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] input, int size);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out RECT rect);
    [DllImport("user32.dll")] static extern bool GetWindowDisplayAffinity(IntPtr h,out uint affinity);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h,int attribute,out int value,int size);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool GetTokenInformation(IntPtr token,int kind,IntPtr info,int size,out int needed);
    [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
    [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthority(IntPtr sid,uint index);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
    public static int Integrity(int pid) {
        IntPtr process=OpenProcess(0x1000,false,pid);
        if(process==IntPtr.Zero)throw new Exception("无法查询目标输入权限级别");
        try {
            IntPtr token;
            if(!OpenProcessToken(process,8,out token))throw new Exception("无法读取进程输入权限级别");
            try {
                int size;GetTokenInformation(token,25,IntPtr.Zero,0,out size);IntPtr buffer=Marshal.AllocHGlobal(size);
                try {
                    if(!GetTokenInformation(token,25,buffer,size,out size))throw new Exception("无法读取进程输入权限级别");
                    IntPtr sid=Marshal.ReadIntPtr(buffer);byte count=Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                    return Marshal.ReadInt32(GetSidSubAuthority(sid,(uint)count-1));
                }finally{Marshal.FreeHGlobal(buffer);}
            }finally{CloseHandle(token);}
        }finally{CloseHandle(process);}
    }
    public static object InputStatus() {
        int level=Integrity(Process.GetCurrentProcess().Id);
        return new {integrity_level=level,integrity_name=level<8192?"low":level<12288?"medium":"high",normal_desktop_input=level>=8192,
            note=level<8192?"宿主以低完整性启动后端，无法向普通中完整性应用输入；需在宿主允许的正常桌面上下文启动 MCP。工具不会自动提权或绕过沙箱。":"仍受目标完整性、窗口授权和 Esc 限制"};
    }
    public static void Initialize() { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
    public static WindowInfo Inspect(long id) {
        IntPtr h = new IntPtr(id);
        if (!IsWindow(h) || !IsWindowVisible(h) || IsIconic(h)) throw new Exception("窗口不可见、已关闭或已最小化");
        RECT r; POINT p = new POINT(); uint pid;
        if (!GetClientRect(h, out r) || !ClientToScreen(h, ref p)) throw new Exception("无法读取窗口坐标");
        GetWindowThreadProcessId(h, out pid);
        StringBuilder title = new StringBuilder(2048); GetWindowText(h, title, title.Capacity);
        string process = "unknown";
        try { process = Process.GetProcessById((int)pid).ProcessName; } catch { }
        return new WindowInfo { id=id, pid=pid, title=title.ToString(), process=process, x=p.X, y=p.Y, width=r.Right, height=r.Bottom };
    }
    public static WindowInfo[] List() {
        List<WindowInfo> windows = new List<WindowInfo>();
        EnumWindows(delegate(IntPtr h, IntPtr p) {
            try { WindowInfo w = Inspect(h.ToInt64()); if (w.title.Length > 0 && w.width > 0 && w.height > 0) windows.Add(w); } catch { }
            return true;
        }, IntPtr.Zero);
        return windows.ToArray();
    }
    // 截取目标客户区，不回退为整屏截图；应用不支持时向调用方报告。
    public static string Capture(long id) {
        WindowInfo w = Inspect(id);
        if (w.width < 1 || w.height < 1 || (long)w.width*w.height > 24000000) throw new Exception("截图尺寸不合法或超过 2400 万像素");
        using (Bitmap bitmap = new Bitmap(w.width, w.height, PixelFormat.Format32bppArgb)) {
            using (Graphics graphics = Graphics.FromImage(bitmap)) {
                IntPtr dc = graphics.GetHdc();
                try { if (!PrintWindow(new IntPtr(id), dc, 3)) throw new Exception("PrintWindow 失败；该应用可能不支持此截图后端"); }
                finally { graphics.ReleaseHdc(dc); }
            }
            using (MemoryStream stream = new MemoryStream()) {
                bitmap.Save(stream, ImageFormat.Png); return Convert.ToBase64String(stream.ToArray());
            }
        }
    }
    static void Validate(long id) {
        DesktopFeedback.Check();
        WindowInfo w = Inspect(id);
        int source=Integrity(Process.GetCurrentProcess().Id),target=Integrity((int)w.pid);
        if(source<target)throw new Exception("Windows 输入权限不匹配：后端完整性="+source+"，目标完整性="+target+"。请在宿主允许的正常桌面上下文启动 MCP；不会自动提权或绕过沙箱。");
        if (System.Text.RegularExpressions.Regex.IsMatch(w.process, "^(powershell|pwsh|cmd|WindowsTerminal|OpenConsole|conhost|CredentialUIBroker|LogonUI|LockApp|SecHealthUI|1Password|KeePass|Bitwarden|ChatGPT|Codex)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new Exception("禁止向此类窗口发送输入");
    }
    static IntPtr Ready(long id) {
        Validate(id);
        IntPtr h = new IntPtr(id);
        DesktopFeedback.Begin();
        SetForegroundWindow(h); Thread.Sleep(100);
        DesktopFeedback.Check();
        if (GetForegroundWindow() != h) throw new Exception("无法确认目标窗口拥有前台焦点");
        return h;
    }
    public static void PrepareControl(long id) {
        // UIA Pattern 直接操作已验证控件，不向全局输入流发送事件，无需抢占前台。
        Validate(id); DesktopFeedback.Begin(); DesktopFeedback.Check(); DesktopFeedback.Touch();
    }
    public static void Activate(long id) {Ready(id);}
    public static Bitmap CaptureVisible(long id) {
        return DesktopFeedback.WithoutVisuals(()=>{
            var w=Inspect(id);IntPtr target=new IntPtr(id);
            if(GetForegroundWindow()!=target)throw new Exception("可见区域截图要求目标在前台，请先 activate_window");
            uint affinity;
            if(!GetWindowDisplayAffinity(target,out affinity)||affinity!=0)throw new Exception("无法确认目标允许捕获，拒绝可见区域截图");
            var area=new Rectangle(w.x,w.y,w.width,w.height);
            bool onScreen=false;foreach(var screen in System.Windows.Forms.Screen.AllScreens)if(screen.Bounds.Contains(area))onScreen=true;
            if(w.width<1||w.height<1||(long)w.width*w.height>24000000||!onScreen)throw new Exception("客户区超出单个屏幕或截图尺寸上限");
            bool blocked=false,found=false;
            EnumWindows(delegate(IntPtr h,IntPtr unused){
                if(h==target){found=true;return false;}
                int cloaked;RECT r;
                if(IsWindowVisible(h)&&!IsIconic(h)&&DwmGetWindowAttribute(h,14,out cloaked,4)==0&&cloaked==0&&GetWindowRect(h,out r)&&area.IntersectsWith(Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom))){blocked=true;return false;}
                return true;
            },IntPtr.Zero);
            if(blocked||!found)throw new Exception("目标客户区被其他窗口遮挡，拒绝可见区域截图");
            var image=new Bitmap(w.width,w.height,PixelFormat.Format32bppArgb);
            try {using(var graphics=Graphics.FromImage(image))graphics.CopyFromScreen(w.x,w.y,0,0,new Size(w.width,w.height),CopyPixelOperation.SourceCopy);return image;}
            catch{image.Dispose();throw;}
        });
    }
    public static string CaptureRegion(long id,int x,int y,int width,int height,int scale) {
        WindowInfo w=Inspect(id);
        if(x<0 || y<0 || width<1 || height<1 || (long)x+width>w.width || (long)y+height>w.height || scale<1 || scale>4 || (long)width*height*scale*scale>24000000)
            throw new Exception("裁剪区域或放大倍数不合法，必须位于目标客户区内");
        using(MemoryStream input=new MemoryStream(Convert.FromBase64String(Capture(id))))
        using(Bitmap source=new Bitmap(input))
        using(Bitmap result=new Bitmap(width*scale,height*scale)) {
            using(Graphics graphics=Graphics.FromImage(result)) {
                graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.Half;
                graphics.DrawImage(source,new Rectangle(0,0,result.Width,result.Height),new Rectangle(x,y,width,height),GraphicsUnit.Pixel);
            }
            using(MemoryStream output=new MemoryStream()) { result.Save(output,ImageFormat.Png); return Convert.ToBase64String(output.ToArray()); }
        }
    }
    static void Emit(INPUT i) {
        // 取消后只允许松开已按下的键或鼠标，防止拖拽或修饰键卡住。
        bool release=i.type==1?(i.value.key.flags & 2)!=0:(i.value.mouse.flags & (4u|16u|64u))!=0;
        if(!release) { DesktopFeedback.Check(); DesktopFeedback.Touch(); }
        if (SendInput(1, new INPUT[]{i}, Marshal.SizeOf(typeof(INPUT))) != 1) throw new Exception("SendInput 失败；可能受到 Windows 权限隔离限制");
    }
    static void Mouse(uint flags, uint data) { INPUT i = new INPUT(); i.value.mouse.flags=flags; i.value.mouse.data=data; Emit(i); }
    static void Key(ushort vk, ushort scan, uint flags) { INPUT i = new INPUT(); i.type=1; i.value.key.vk=vk; i.value.key.scan=scan; i.value.key.flags=flags; Emit(i); }
    static void Move(long id, int x, int y) {
        DesktopFeedback.Check();
        WindowInfo w=Inspect(id);
        if (x<0 || y<0 || x>=w.width || y>=w.height) throw new Exception("坐标超出目标客户区");
        POINT p = new POINT { X=w.x+x, Y=w.y+y };
        if (GetAncestor(WindowFromPoint(p), 2) != new IntPtr(id)) throw new Exception("目标位置被其他窗口遮挡");
        // 绝对坐标输入支持整个虚拟桌面，统一使用 SendInput 的权限和取消检查。
        Rectangle desktop=System.Windows.Forms.SystemInformation.VirtualScreen;
        INPUT move=new INPUT();move.value.mouse.dx=(int)Math.Round((double)(p.X-desktop.X)*65535/Math.Max(1,desktop.Width-1));
        move.value.mouse.dy=(int)Math.Round((double)(p.Y-desktop.Y)*65535/Math.Max(1,desktop.Height-1));
        move.value.mouse.flags=0xC001;Emit(move);
        POINT actual=new POINT();bool reached=false;
        for(int retry=0;retry<20;retry++) {
            DesktopFeedback.Check();
            if(GetCursorPos(out actual)&&Math.Abs(actual.X-p.X)<=2&&Math.Abs(actual.Y-p.Y)<=2){reached=true;break;}
            Thread.Sleep(2);
        }
        if(!reached)throw new Exception("鼠标移动未到达目标位置，已停止输入；目标="+p.X+","+p.Y+"，实际="+actual.X+","+actual.Y);
    }
    public static void Click(long id,int x,int y,string button) {
        Ready(id); Move(id,x,y); uint down=button=="right"?8u:2u, up=button=="right"?16u:4u;
        Feedback(id,x,y,button=="right"?"AI 正在右键点击":"AI 正在点击");
        Mouse(down,0); try { Thread.Sleep(30); } finally { Mouse(up,0); }
    }
    public static void Type(long id,string text) {
        Ready(id);
        if (text.Length>10000) throw new Exception("单次输入过长");
        Feedback(id,-1,-1,"AI 正在输入");
        foreach(char c in text) { Key(0,c,4); Key(0,c,6); }
    }
    public static void Press(long id,int[] keys) {
        foreach(int k in keys) if (k<1 || k>255 || k==91 || k==92) throw new Exception("不允许的按键");
        Ready(id); int held=0;
        Feedback(id,-1,-1,"AI 正在发送快捷键");
        try { foreach(int k in keys) { Key((ushort)k,0,0); held++; } }
        finally { for(int i=held-1;i>=0;i--) Key((ushort)keys[i],0,2); }
    }
    static void Feedback(long id,int x,int y,string label) {
        WindowInfo w=Inspect(id);
        DesktopFeedback.Pulse(w.x,w.y,w.width,w.height,x,y,label);
    }
    public static void Scroll(long id,int x,int y,int delta) { Ready(id); Move(id,x,y); Feedback(id,x,y,"AI 正在滚动"); Mouse(0x800,unchecked((uint)delta)); }
    public static void Drag(long id,int x,int y,int tx,int ty) {
        Ready(id); Move(id,x,y); Feedback(id,x,y,"AI 正在拖拽"); Mouse(2,0);
        try { for(int n=1;n<=20;n++) { Move(id,x+(tx-x)*n/20,y+(ty-y)*n/20); Thread.Sleep(15); } }
        finally { Mouse(4,0); }
        Feedback(id,tx,ty,"AI 拖拽终点");
    }
    public class PathPoint {public int x; public int y;}
    public static void DrawPath(long id,PathPoint[] points,int duration) {
        if(points==null||points.Length<2||points.Length>512||duration<200||duration>8000)throw new Exception("轨迹点数或时长不合法");
        var original=Inspect(id); double length=0;
        foreach(var p in points)if(p==null||p.x<0||p.y<0||p.x>=original.width||p.y>=original.height)throw new Exception("轨迹点超出客户区");
        for(int i=1;i<points.Length;i++){double dx=points[i].x-points[i-1].x,dy=points[i].y-points[i-1].y;length+=Math.Sqrt(dx*dx+dy*dy);}
        if(length<=0||length>30000)throw new Exception("轨迹长度必须大于零且不超过 30000 像素");
        Ready(id); Move(id,points[0].x,points[0].y);Feedback(id,points[0].x,points[0].y,"AI 正在绘制轨迹");
        Mouse(2,0); var clock=Stopwatch.StartNew();double travelled=0;
        try {
            for(int i=1;i<points.Length;i++) {
                var a=points[i-1];var b=points[i];double dx=b.x-a.x,dy=b.y-a.y,distance=Math.Sqrt(dx*dx+dy*dy);
                int steps=Math.Max(1,(int)Math.Ceiling(distance/4));
                for(int n=1;n<=steps;n++) {
                    if(clock.ElapsedMilliseconds>12000)throw new Exception("轨迹执行超过 12 秒，已停止并松键；请重新观察，勿自动重试整笔");
                    double due=duration*(travelled+distance*n/steps)/length;
                    while(clock.ElapsedMilliseconds<due){DesktopFeedback.Check();Thread.Sleep(Math.Min(10,Math.Max(1,(int)(due-clock.ElapsedMilliseconds))));}
                    var current=Inspect(id);
                    // 每个采样点都检查焦点、窗口身份及布局，避免拖到其他应用。
                    if(GetForegroundWindow()!=new IntPtr(id)||current.pid!=original.pid||current.title!=original.title||current.x!=original.x||current.y!=original.y||current.width!=original.width||current.height!=original.height)
                        throw new Exception("绘画期间窗口或焦点发生变化，已中止，请重新观察");
                    Move(id,a.x+(int)Math.Round(dx*n/steps),a.y+(int)Math.Round(dy*n/steps));
                }
                travelled+=distance;
            }
        } finally {Mouse(4,0);}
    }
}
