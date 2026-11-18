using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tesseract;
using ImageFormat=System.Drawing.Imaging.ImageFormat;

class Program {
    static readonly JsonSerializerOptions Json=new(){IncludeFields=true};
    static int I(JsonNode a,string k,int d=0)=>a?[k]?.GetValue<int>()??d;
    static long W(JsonNode a)=>a["window_id"].GetValue<long>();
    static string S(JsonNode a,string k,string d=null)=>a?[k]?.GetValue<string>()??d;
    static bool B(JsonNode a,string k,bool d=false)=>a?[k]?.GetValue<bool>()??d;
    static string Encode(Bitmap image) {using var s=new MemoryStream();image.Save(s,ImageFormat.Png);return Convert.ToBase64String(s.ToArray());}
    static string ModelPath=>Path.Combine(AppContext.BaseDirectory,"tessdata");
    static bool OcrReady=>File.Exists(Path.Combine(ModelPath,"eng.traineddata"))&&File.Exists(Path.Combine(ModelPath,"chi_sim.traineddata"));
    static object Ocr(Bitmap image,int x,int y,int scale) {
        if(!OcrReady) throw new Exception("OCR 模型缺失；请安装便携包中的 eng 和 chi_sim 模型");
        using var engine=new TesseractEngine(ModelPath,"chi_sim+eng",EngineMode.LstmOnly);
        using var stream=new MemoryStream(); image.Save(stream,ImageFormat.Png);
        using var pix=Pix.LoadFromMemory(stream.ToArray()); using var page=engine.Process(pix);
        using var iterator=page.GetIterator(); var words=new System.Collections.Generic.List<object>(); iterator.Begin();
        do {
            if(iterator.TryGetBoundingBox(PageIteratorLevel.Word,out var box)) words.Add(new {text=iterator.GetText(PageIteratorLevel.Word),confidence=iterator.GetConfidence(PageIteratorLevel.Word),
                bounds=new {x=x+(double)box.X1/scale,y=y+(double)box.Y1/scale,width=(double)box.Width/scale,height=(double)box.Height/scale},source="ocr"});
        } while(iterator.Next(PageIteratorLevel.Word));
        return new {text=page.GetText(),words,language="chi_sim+eng",source="ocr"};
    }
    static object Observe(JsonNode a) {
        long window=W(a); if(DesktopFeedback.Active) DesktopFeedback.Touch();
        var before=DesktopNative.Inspect(window); var result=new JsonObject();
        result["window"]=JsonSerializer.SerializeToNode(before,Json);
        if(B(a,"include_screenshot",true)||B(a,"include_ocr")) {
            string backend=S(a,"capture_backend","auto"),used="wgc",reason=null;
            Bitmap full;
            try {if(backend=="printwindow"||backend=="screen_region") throw new NotSupportedException("显式选择 "+backend); full=Wgc.Capture(window);}
            catch(Exception ex) {
                if(backend=="wgc") throw;
                reason=ex.Message;
                if(backend=="screen_region"){used="screen_region";reason=null;full=DesktopNative.CaptureVisible(window);}
                else try {
                    used="printwindow";
                    using var input=new MemoryStream(Convert.FromBase64String(DesktopNative.Capture(window))); full=new Bitmap(input);
                }catch(Exception printError){
                    if(backend=="printwindow")throw;
                    used="screen_region";reason+="; PrintWindow: "+printError.Message;
                    try{full=DesktopNative.CaptureVisible(window);}catch(Exception screenError){throw new Exception(reason+"; screen_region: "+screenError.Message);}
                }
            }
            using(full) {
                var r=a["region"]; int x=I(r,"x"),y=I(r,"y"),width=I(r,"width",full.Width),height=I(r,"height",full.Height),scale=I(a,"scale",1);
                if(x<0||y<0||width<1||height<1||(long)x+width>full.Width||(long)y+height>full.Height||scale<1||scale>4||(long)width*height*scale*scale>24000000) throw new Exception("裁剪或缩放超出图片范围");
                using var image=new Bitmap(width*scale,height*scale);
                using(var graphics=Graphics.FromImage(image)) {graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;graphics.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.Half;graphics.DrawImage(full,new Rectangle(0,0,image.Width,image.Height),new Rectangle(x,y,width,height),GraphicsUnit.Pixel);}
                result["capture"]=JsonSerializer.SerializeToNode(new {backend=used,fallback_reason=reason});
                result["image_region"]=JsonSerializer.SerializeToNode(new{x,y,width,height,scale});
                if(B(a,"include_screenshot",true)) result["image"]=Encode(image);
                if(B(a,"include_ocr")) result["ocr"]=JsonSerializer.SerializeToNode(Ocr(image,x,y,scale));
            }
        }
        if(B(a,"include_text")) {
            var tree=JsonSerializer.SerializeToNode(Elements.Read(window,S(a,"uia_view","control")=="raw"));
            result["controls"]=tree["controls"].DeepClone(); tree.AsObject().Remove("controls"); result["uia"]=tree;
        }
        if(JsonSerializer.Serialize(before,Json)!=JsonSerializer.Serialize(DesktopNative.Inspect(window),Json)) throw new Exception("观察期间窗口已变化，请重新观察");
        return result;
    }
    static object Run(string op,JsonNode a) {
        switch(op) {
            case "doctor":return new {ready=true,platform="win32",backend="native-dotnet",version="0.3.0",uia=true,wgc=Wgc.Supported,input_status=DesktopNative.InputStatus(),ocr=new {available=OcrReady,models=new[]{"eng","chi_sim"}},visual_feedback=DesktopFeedback.Enabled,control_active=DesktopFeedback.Active,control_cancelled=DesktopFeedback.Cancelled};
            case "start_control":DesktopFeedback.Begin();return new{control_active=DesktopFeedback.Active,control_cancelled=DesktopFeedback.Cancelled};
            case "end_control":DesktopFeedback.End();Elements.Clear();return new{control_active=DesktopFeedback.Active,control_cancelled=DesktopFeedback.Cancelled};
            case "list_windows":return DesktopNative.List();
            case "launch_app":return Applications.Launch(S(a,"app"));
            case "inspect":return DesktopNative.Inspect(W(a));
            case "activate_window":DesktopNative.Activate(W(a));break;
            case "observe":Elements.Clear();return Observe(a);
            case "element_action":return Elements.Act(W(a),S(a,"element_id"),S(a,"action"),S(a,"value"));
            case "click":DesktopNative.Click(W(a),I(a,"x"),I(a,"y"),S(a,"button","left"));break;
            case "type_text":DesktopNative.Type(W(a),S(a,"text"));break;
            case "press_key":DesktopNative.Press(W(a),JsonSerializer.Deserialize<int[]>(a["keys"].ToJsonString()));break;
            case "scroll":DesktopNative.Scroll(W(a),I(a,"x"),I(a,"y"),I(a,"delta"));break;
            case "drag":DesktopNative.Drag(W(a),I(a,"x"),I(a,"y"),I(a,"to_x"),I(a,"to_y"));break;
            case "draw_path":
                var points=JsonSerializer.Deserialize<DesktopNative.PathPoint[]>(a["points"].ToJsonString(),Json);
                DesktopNative.DrawPath(W(a),points,I(a,"duration_ms",1200));break;
            default:throw new Exception("未知操作");
        }
        return new{sent=true};
    }
    [STAThread] static void Main(string[] args) {
        if(args.Length==1 && args[0]=="--fixture") {Fixture.Run();return;}
        Console.InputEncoding=new System.Text.UTF8Encoding(false);Console.OutputEncoding=new System.Text.UTF8Encoding(false);DesktopNative.Initialize();
        try { string line;while((line=Console.ReadLine())!=null) {
            string id=null;
            try {var req=JsonNode.Parse(line);id=S(req,"id");var result=Run(S(req,"op"),req["args"]);Console.WriteLine(JsonSerializer.Serialize(new{id,result},Json));}
            catch(Exception ex) {Console.WriteLine(JsonSerializer.Serialize(new{id,error=ex.Message},Json));}
        }} finally {DesktopFeedback.Close();}
    }
}
