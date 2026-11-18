using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

public static class DesktopFeedback {
    static Thread thread;
    static LayerForm dispatcher;
    static readonly List<LayerForm> layers = new List<LayerForm>();
    static readonly object gate = new object();
    static readonly Dictionary<uint,IntPtr> savedCursors = new Dictionary<uint,IntPtr>();
    static HookProc hookProc;
    static IntPtr hook;
    static Mutex ownership;
    static bool ownsDesktop;
    static EventWaitHandle watcherDone;
    static volatile bool active, cancelled;
    static long touched;
    static Point click = new Point(-1,-1);
    static long clicked;
    static string label = "Agent 正在控制电脑";
    public static bool Enabled { get { return Environment.GetEnvironmentVariable("COMPUTER_USE_FEEDBACK") != "0"; } }
    public static bool Active { get { return active; } }
    public static bool Cancelled { get { return cancelled; } }
    public static int IdleTimeoutSeconds { get { return 300; } }
    public static bool Visible { get { return active && Enabled; } }
    public static long Handle { get { return dispatcher==null?0:dispatcher.Handle.ToInt64(); } }
    delegate IntPtr HookProc(int code, IntPtr msg, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int x,y; public POINT(int x,int y){this.x=x;this.y=y;} }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int w,h; public SIZE(int w,int h){this.w=w;this.h=h;} }
    [StructLayout(LayoutKind.Sequential,Pack=1)] struct BLEND { public byte op,flags,alpha,format; }
    [StructLayout(LayoutKind.Sequential)] struct ICONINFO { [MarshalAs(UnmanagedType.Bool)] public bool icon; public uint x,y; public IntPtr mask,color; }
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int type,HookProc proc,IntPtr module,uint tid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h,int code,IntPtr msg,IntPtr data);
    [DllImport("user32.dll")] static extern bool EnableWindow(IntPtr h,bool enable);
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h,IntPtr dst,ref POINT pos,ref SIZE size,IntPtr src,ref POINT origin,uint key,ref BLEND blend,uint flags);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern IntPtr CreateBitmap(int w,int h,uint planes,uint bits,byte[] data);
    [DllImport("user32.dll")] static extern IntPtr CreateIconIndirect(ref ICONINFO info);
    [DllImport("user32.dll")] static extern IntPtr LoadCursor(IntPtr instance,IntPtr id);
    [DllImport("user32.dll")] static extern IntPtr CopyIcon(IntPtr icon);
    [DllImport("user32.dll")] static extern bool DestroyCursor(IntPtr cursor);
    [DllImport("user32.dll")] static extern bool SetSystemCursor(IntPtr cursor,uint id);

    public static void Check() {
        if(cancelled) throw new OperationCanceledException("用户已按 Esc 退出控制；后续输入已锁定，请由用户重启 MCP 服务后恢复");
    }
    public static void Touch() { Interlocked.Exchange(ref touched,DateTime.UtcNow.Ticks); }
    static void EnsureThread() {
        lock(gate) {
            if(thread!=null) return;
            ManualResetEvent ready=new ManualResetEvent(false);
            Exception error=null;
            thread=new Thread(delegate() {
                try {
                    dispatcher=new LayerForm();
                    IntPtr handle=dispatcher.Handle;
                    hookProc=KeyboardHook;
                    hook=SetWindowsHookEx(13,hookProc,GetModuleHandle(null),0);
                    if(hook==IntPtr.Zero) throw new Exception("无法安装 Esc 退出监听，拒绝启动控制");
                    System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
                    timer.Interval=100;
                    timer.Tick+=delegate {
                        if(cancelled || (active && DateTime.UtcNow.Ticks-Interlocked.Read(ref touched)>TimeSpan.FromSeconds(IdleTimeoutSeconds).Ticks)) {
                            // 客户端可能长期复用 MCP 进程，空闲时正常释放视觉与指针，不伪装成用户取消。
                            if(active) StopUI();
                            return;
                        }
                        if(active && Enabled) foreach(LayerForm layer in layers) layer.Render();
                    };
                    timer.Start(); ready.Set();
                    Application.Run();
                    timer.Dispose();
                } catch(Exception ex) { error=ex; ready.Set(); }
                finally { if(hook!=IntPtr.Zero) UnhookWindowsHookEx(hook); }
            });
            thread.IsBackground=true; thread.SetApartmentState(ApartmentState.STA); thread.Start();
            if(!ready.WaitOne(4000) || error!=null || dispatcher==null)
                throw new Exception("控制提示或 Esc 监听初始化失败",error);
        }
    }
    public static void Begin() {
        Check(); EnsureThread();
        dispatcher.Invoke((Action)delegate {
            Check();
            if(active) { Touch(); return; }
            ownership=new Mutex(false,"Local\\ComputerUseStandaloneDesktopControl");
            try { ownsDesktop=ownership.WaitOne(0); } catch(AbandonedMutexException) { ownsDesktop=true; }
            if(!ownsDesktop) { ownership.Dispose(); ownership=null; throw new Exception("其他 Agent 实例正在控制电脑"); }
            try {
                if(Enabled) {
                    StartCursorWatchdog();
                    InstallCursors();
                    foreach(Screen screen in Screen.AllScreens) {
                        LayerForm layer=new LayerForm(); layer.Bounds=screen.Bounds;
                        layers.Add(layer); layer.Render(); layer.Show();
                    }
                }
                active=true; Touch();
            } catch { StopUI(); throw; }
        });
    }
    // 只处理物理 Esc；不保存其他键，也不把 Agent 注入的 Esc 当作用户取消。
    public static bool HandleEscape(int key,uint flags,int message) {
        if(active && key==27 && (flags & 0x10)==0 && (message==0x100 || message==0x104)) {
            cancelled=true; return true;
        }
        return false;
    }
    static IntPtr KeyboardHook(int code,IntPtr msg,IntPtr data) {
        if(code>=0 && HandleEscape(Marshal.ReadInt32(data),unchecked((uint)Marshal.ReadInt32(data,8)),msg.ToInt32()))
            return new IntPtr(1);
        return CallNextHookEx(hook,code,msg,data);
    }
    public static void Pulse(int x,int y,int width,int height,int px,int py,string text) {
        Begin(); Check(); Touch();
        if(px>=0 && py>=0) { click=new Point(x+px,y+py); Interlocked.Exchange(ref clicked,DateTime.UtcNow.Ticks); }
    }
    public static void End() {
        if(dispatcher!=null && !dispatcher.IsDisposed) dispatcher.Invoke((Action)StopUI);
    }
    public static T WithoutVisuals<T>(Func<T> capture) {
        if(dispatcher==null||!active)return capture();
        dispatcher.Invoke((Action)delegate {foreach(var layer in layers)layer.Hide();});
        try {Thread.Sleep(40);return capture();}
        finally {dispatcher.Invoke((Action)delegate {if(active&&!cancelled)foreach(var layer in layers)layer.Show();});}
    }
    static void StopUI() {
        active=false;
        foreach(LayerForm layer in layers) { layer.Hide(); layer.Dispose(); }
        layers.Clear();
        foreach(KeyValuePair<uint,IntPtr> pair in savedCursors) {
            IntPtr copy=CopyIcon(pair.Value);
            if(copy!=IntPtr.Zero) SetSystemCursor(copy,pair.Key);
            DestroyCursor(pair.Value);
        }
        savedCursors.Clear();
        if(watcherDone!=null) { watcherDone.Set(); watcherDone.Dispose(); watcherDone=null; }
        if(ownsDesktop) { ownership.ReleaseMutex(); ownsDesktop=false; }
        if(ownership!=null) { ownership.Dispose(); ownership=null; }
    }
    public static void Close() {
        End();
        if(dispatcher!=null && !dispatcher.IsDisposed) {
            dispatcher.Invoke((Action)delegate { dispatcher.Close(); Application.ExitThread(); });
            thread.Join(1000);
        }
    }
    static void StartCursorWatchdog() {
        string eventName="Local\\ComputerUseCursorRestore-"+Guid.NewGuid().ToString("N");
        watcherDone=new EventWaitHandle(false,EventResetMode.ManualReset,eventName);
        EventWaitHandle ready=new EventWaitHandle(false,EventResetMode.ManualReset,eventName+"-ready");
        int pid=Process.GetCurrentProcess().Id;
        // 后端异常退出时，由独立守护进程恢复 Windows 主题指针。
        string code="$e=[Threading.EventWaitHandle]::OpenExisting('"+eventName+"');$r=[Threading.EventWaitHandle]::OpenExisting('"+eventName+"-ready');$r.Set()|Out-Null;$r.Dispose();"+
            "try{$p=[Diagnostics.Process]::GetProcessById("+pid+");while(-not $p.HasExited){if($e.WaitOne(200)){exit}}}catch{};"+
            "Add-Type 'using System;using System.Runtime.InteropServices;public class CursorRestore{[DllImport(\"user32.dll\")]public static extern bool SystemParametersInfo(uint a,uint b,IntPtr c,uint d);}';"+
            "[CursorRestore]::SystemParametersInfo(0x57,0,[IntPtr]::Zero,0)|Out-Null";
        ProcessStartInfo info=new ProcessStartInfo(Environment.GetFolderPath(Environment.SpecialFolder.System)+"\\WindowsPowerShell\\v1.0\\powershell.exe");
        info.Arguments="-NoLogo -NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(code));
        info.UseShellExecute=false; info.CreateNoWindow=true;
        Process watcher=Process.Start(info);
        if(watcher==null) throw new Exception("指针恢复守护进程启动失败");
        if(!ready.WaitOne(4000)) { try { watcher.Kill(); } catch { } ready.Dispose(); watcher.Dispose(); throw new Exception("指针恢复守护进程未就绪"); }
        ready.Dispose();
        watcher.Dispose();
    }
    public static Bitmap CursorImage() {
        Bitmap bitmap=new Bitmap(40,40,PixelFormat.Format32bppArgb);
        using(Graphics g=Graphics.FromImage(bitmap)) {
            g.SmoothingMode=SmoothingMode.AntiAlias;
            PointF[] points={new PointF(4,3),new PointF(28,12),new PointF(18,17),new PointF(13,30)};
            using(Brush fill=new SolidBrush(Color.FromArgb(18,25,35))) g.FillPolygon(fill,points);
            using(Pen outline=new Pen(Color.White,2)) g.DrawPolygon(outline,points);
            using(Pen accent=new Pen(Color.FromArgb(85,205,255),2)) g.DrawLine(accent,4,3,28,12);
        }
        return bitmap;
    }
    static IntPtr NewCursor() {
        using(Bitmap image=CursorImage()) {
            IntPtr color=image.GetHbitmap(Color.FromArgb(0));
            IntPtr mask=CreateBitmap(40,40,1,1,new byte[240]);
            try {
                ICONINFO info=new ICONINFO { icon=false,x=4,y=3,color=color,mask=mask };
                return CreateIconIndirect(ref info);
            } finally { DeleteObject(color); DeleteObject(mask); }
        }
    }
    static void InstallCursors() {
        foreach(uint id in new uint[]{32512,32513,32649}) {
            IntPtr saved=CopyIcon(LoadCursor(IntPtr.Zero,new IntPtr(id)));
            if(saved==IntPtr.Zero) throw new Exception("无法备份系统指针");
            savedCursors.Add(id,saved);
            IntPtr replacement=NewCursor();
            if(replacement==IntPtr.Zero || !SetSystemCursor(replacement,id)) {
                if(replacement!=IntPtr.Zero) DestroyCursor(replacement);
                throw new Exception("无法安装 Agent 指针");
            }
        }
    }
    // 离线预览和实际屏幕使用同一渲染函数，中心透明，只有边缘渐变与提示条。
    public static Bitmap RenderImage(int width,int height,string text) {
        Bitmap bitmap=new Bitmap(width,height,PixelFormat.Format32bppPArgb);
        using(Graphics g=Graphics.FromImage(bitmap)) {
            g.SmoothingMode=SmoothingMode.AntiAlias;
            int edge=Math.Min(28,Math.Min(width,height)/4);
            Color blue=Color.FromArgb(28,68,145,255), purple=Color.FromArgb(24,160,95,235);
            using(LinearGradientBrush b=new LinearGradientBrush(new Rectangle(0,0,width,edge),blue,Color.Transparent,90f)) { b.WrapMode=WrapMode.TileFlipXY; g.FillRectangle(b,0,0,width,edge); }
            using(LinearGradientBrush b=new LinearGradientBrush(new Rectangle(0,height-edge,width,edge),Color.Transparent,purple,90f)) { b.WrapMode=WrapMode.TileFlipXY; g.FillRectangle(b,0,height-edge,width,edge); }
            using(LinearGradientBrush b=new LinearGradientBrush(new Rectangle(0,0,edge,height),purple,Color.Transparent,0f)) { b.WrapMode=WrapMode.TileFlipXY; g.FillRectangle(b,0,0,edge,height); }
            using(LinearGradientBrush b=new LinearGradientBrush(new Rectangle(width-edge,0,edge,height),Color.Transparent,blue,0f)) { b.WrapMode=WrapMode.TileFlipXY; g.FillRectangle(b,width-edge,0,edge,height); }
            int bw=Math.Min(470,width-24), bx=(width-bw)/2;
            Rectangle pill=new Rectangle(bx,28,bw,48);
            using(GraphicsPath path=new GraphicsPath()) {
                path.AddArc(pill.X,pill.Y,20,20,180,90); path.AddArc(pill.Right-20,pill.Y,20,20,270,90);
                path.AddArc(pill.Right-20,pill.Bottom-20,20,20,0,90); path.AddArc(pill.X,pill.Bottom-20,20,20,90,90); path.CloseFigure();
                using(Brush bg=new SolidBrush(Color.FromArgb(232,27,49,83))) g.FillPath(bg,path);
            }
            using(Font font=new Font("Microsoft YaHei UI",11))
            using(Brush white=new SolidBrush(Color.White))
            using(StringFormat sf=new StringFormat { Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center })
                g.DrawString(text+"   ·   按 Esc 退出",font,white,pill,sf);
        }
        return bitmap;
    }
    class LayerForm:Form {
        long lastClick=-1;
        bool hadRing;
        bool rendered;
        public LayerForm() { FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; TopMost=true; StartPosition=FormStartPosition.Manual; }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { CreateParams p=base.CreateParams; p.ExStyle|=0x080800A0; return p; } }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); EnableWindow(Handle,false); }
        protected override void WndProc(ref Message m) {
            if(m.Msg==0x84) {m.Result=new IntPtr(-1);return;}
            if(m.Msg==0x21) {m.Result=new IntPtr(3);return;}
            base.WndProc(ref m);
        }
        public void Render() {
            long clickTime=Interlocked.Read(ref clicked);
            bool ring=DateTime.UtcNow.Ticks-clickTime<TimeSpan.FromMilliseconds(850).Ticks;
            // 静止会话不逐帧分配全屏位图，仅在点击动画或状态变化时更新。
            if(rendered && lastClick==clickTime && !ring && !hadRing) return;
            lastClick=clickTime; hadRing=ring; rendered=true;
            using(Bitmap image=RenderImage(Width,Height,label)) {
                long age=DateTime.UtcNow.Ticks-Interlocked.Read(ref clicked);
                if(age>=0 && age<TimeSpan.FromMilliseconds(850).Ticks && click.X>=Left && click.X<Right && click.Y>=Top && click.Y<Bottom) {
                    using(Graphics g=Graphics.FromImage(image))
                    using(Pen p=new Pen(Color.FromArgb(110,210,255),3)) {
                        g.SmoothingMode=SmoothingMode.AntiAlias;
                        float radius=10+22*(float)age/TimeSpan.FromMilliseconds(850).Ticks;
                        g.DrawEllipse(p,click.X-Left-radius,click.Y-Top-radius,radius*2,radius*2);
                    }
                }
                IntPtr dc=CreateCompatibleDC(IntPtr.Zero), bitmap=image.GetHbitmap(Color.FromArgb(0)), previous=SelectObject(dc,bitmap);
                try {
                    POINT pos=new POINT(Left,Top), origin=new POINT(0,0); SIZE size=new SIZE(Width,Height);
                    BLEND blend=new BLEND { alpha=255,format=1 };
                    if(!UpdateLayeredWindow(Handle,IntPtr.Zero,ref pos,ref size,dc,ref origin,0,ref blend,2))
                        throw new Exception("屏幕提示层更新失败");
                } finally { SelectObject(dc,previous); DeleteObject(bitmap); DeleteDC(dc); }
            }
        }
    }
}
