using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

static class Wgc {
    public static bool Supported {get {try{return GraphicsCaptureSession.IsSupported();}catch{return false;}}}
    [DllImport("combase.dll",CharSet=CharSet.Unicode)] static extern int WindowsCreateString(string text,int length,out IntPtr value);
    [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr value);
    [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr name,ref Guid iid,out IntPtr factory);
    [DllImport("d3d11.dll")] static extern int D3D11CreateDevice(IntPtr adapter,int driver,IntPtr software,uint flags,IntPtr levels,uint count,uint sdk,out IntPtr device,out int level,out IntPtr context);
    [DllImport("d3d11.dll")] static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgi,out IntPtr device);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h,int attribute,out RECT value,int size);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out RECT rect);
    [StructLayout(LayoutKind.Sequential)] struct RECT {public int left,top,right,bottom;}
    [StructLayout(LayoutKind.Sequential)] struct DESC {public uint width,height,mips,array,format,sampleCount,sampleQuality,usage,bind,cpu,misc;}
    [StructLayout(LayoutKind.Sequential)] struct MAP {public IntPtr data;public uint pitch,depth;}
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateItem(IntPtr self,IntPtr hwnd,ref Guid iid,out IntPtr item);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetInterface(IntPtr self,ref Guid iid,out IntPtr obj);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void GetDesc(IntPtr self,out DESC desc);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateTexture(IntPtr self,ref DESC desc,IntPtr initial,out IntPtr texture);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void CopyResource(IntPtr self,IntPtr destination,IntPtr source);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int MapResource(IntPtr self,IntPtr texture,uint subresource,uint mode,uint flags,out MAP mapped);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void UnmapResource(IntPtr self,IntPtr texture,uint subresource);
    static T Method<T>(IntPtr obj,int slot) where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj),slot*IntPtr.Size));
    static void Check(int code)=>Marshal.ThrowExceptionForHR(code);
    public static Bitmap Capture(long window) {
        if(!Supported)throw new NotSupportedException("Windows.Graphics.Capture 不可用");
        IntPtr name=IntPtr.Zero,factory=IntPtr.Zero,itemPtr=IntPtr.Zero,device=IntPtr.Zero,context=IntPtr.Zero,dxgi=IntPtr.Zero,winDevice=IntPtr.Zero;
        string stage="activation";
        try {
            string type="Windows.Graphics.Capture.GraphicsCaptureItem";
            Check(WindowsCreateString(type,type.Length,out name));
            Guid factoryId=new("3628e81b-3cac-4c60-b7f4-23ce0e0c3356"),itemId=new("79c3f95b-31f7-4ec2-a464-632ef5d30760");
            Check(RoGetActivationFactory(name,ref factoryId,out factory));
            stage="CreateForWindow";Check(Method<CreateItem>(factory,3)(factory,new IntPtr(window),ref itemId,out itemPtr));
            var item=MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr);
            int code=D3D11CreateDevice(IntPtr.Zero,1,IntPtr.Zero,0x20,IntPtr.Zero,0,7,out device,out _,out context);
            if(code<0)Check(D3D11CreateDevice(IntPtr.Zero,5,IntPtr.Zero,0x20,IntPtr.Zero,0,7,out device,out _,out context));
            Guid dxgiId=new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");Check(Marshal.QueryInterface(device,ref dxgiId,out dxgi));
            Check(CreateDirect3D11DeviceFromDXGIDevice(dxgi,out winDevice));
            var projected=MarshalInterface<IDirect3DDevice>.FromAbi(winDevice);
            stage="CreateFreeThreaded";using var pool=Direct3D11CaptureFramePool.CreateFreeThreaded(projected,DirectXPixelFormat.B8G8R8A8UIntNormalized,1,item.Size);
            stage="StartCapture";using var session=pool.CreateCaptureSession(item);session.IsCursorCaptureEnabled=false;session.StartCapture();
            var clock=System.Diagnostics.Stopwatch.StartNew();
            while(clock.ElapsedMilliseconds<3000) {
                using var frame=pool.TryGetNextFrame();
                if(frame==null){Thread.Sleep(10);continue;}
                if(frame.ContentSize.Width!=item.Size.Width||frame.ContentSize.Height!=item.Size.Height)throw new Exception("捕获期间窗口尺寸变化，请重新截图");
                stage="ReadFrame";using var full=ReadFrame(frame,device,context);
                var client=DesktopNative.Inspect(window); RECT bounds;
                DwmGetWindowAttribute(new IntPtr(window),9,out bounds,16);
                if(bounds.right-bounds.left!=frame.ContentSize.Width||bounds.bottom-bounds.top!=frame.ContentSize.Height) GetWindowRect(new IntPtr(window),out bounds);
                int x=client.x-bounds.left,y=client.y-bounds.top;
                if(x<0||y<0||x+client.width>full.Width||y+client.height>full.Height)throw new Exception("WGC 客户区坐标与捕获帧不匹配");
                return full.Clone(new Rectangle(x,y,client.width,client.height),PixelFormat.Format32bppArgb);
            }
            throw new TimeoutException("WGC 未在 3 秒内返回新帧");
        } catch(Exception ex) {throw new Exception($"WGC {stage}: {ex.Message} (0x{ex.HResult:X8})",ex);} finally {
            foreach(var p in new[]{winDevice,dxgi,context,device,itemPtr,factory})if(p!=IntPtr.Zero)Marshal.Release(p);
            if(name!=IntPtr.Zero)WindowsDeleteString(name);
        }
    }
    static Bitmap ReadFrame(Direct3D11CaptureFrame frame,IntPtr device,IntPtr context) {
        IntPtr access=IntPtr.Zero,texture=IntPtr.Zero,staging=IntPtr.Zero;
        Guid accessId=new("a9b3d012-3df2-4ee3-b8d1-8695f457d3c1"),textureId=new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
        try {
            Check(Marshal.QueryInterface(((IWinRTObject)frame.Surface).NativeObject.ThisPtr,ref accessId,out access));
            Check(Method<GetInterface>(access,3)(access,ref textureId,out texture));
            Method<GetDesc>(texture,10)(texture,out var desc);
            desc.usage=3;desc.bind=0;desc.cpu=0x20000;desc.misc=0;
            Check(Method<CreateTexture>(device,5)(device,ref desc,IntPtr.Zero,out staging));
            Method<CopyResource>(context,47)(context,staging,texture);
            Check(Method<MapResource>(context,14)(context,staging,0,1,0,out var mapped));
            try {
                var image=new Bitmap(frame.ContentSize.Width,frame.ContentSize.Height,PixelFormat.Format32bppArgb);
                var bits=image.LockBits(new Rectangle(0,0,image.Width,image.Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
                try {var row=new byte[image.Width*4];for(int y=0;y<image.Height;y++){Marshal.Copy(IntPtr.Add(mapped.data,checked((int)(y*mapped.pitch))),row,0,row.Length);Marshal.Copy(row,0,IntPtr.Add(bits.Scan0,y*bits.Stride),row.Length);}}
                finally{image.UnlockBits(bits);}return image;
            } finally {Method<UnmapResource>(context,15)(context,staging,0);}
        } finally {foreach(var p in new[]{staging,texture,access})if(p!=IntPtr.Zero)Marshal.Release(p);}
    }
}
