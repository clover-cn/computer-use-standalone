using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Automation;

static class Elements {
    sealed record Entry(AutomationElement Element,string Identity,long Window);
    static readonly Dictionary<string,Entry> entries=new();
    static string Identity(AutomationElement e) {
        var c=e.Current;
        string state="";
        if(e.TryGetCurrentPattern(ValuePattern.Pattern,out var value))state+="|value:"+((ValuePattern)value).Current.Value;
        if(e.TryGetCurrentPattern(TogglePattern.Pattern,out var toggle))state+="|toggle:"+((TogglePattern)toggle).Current.ToggleState;
        if(e.TryGetCurrentPattern(SelectionItemPattern.Pattern,out var selection))state+="|selected:"+((SelectionItemPattern)selection).Current.IsSelected;
        if(e.TryGetCurrentPattern(ExpandCollapsePattern.Pattern,out var expand))state+="|expanded:"+((ExpandCollapsePattern)expand).Current.ExpandCollapseState;
        return string.Join(",",e.GetRuntimeId())+"|"+c.Name+"|"+c.AutomationId+"|"+c.ControlType.Id+"|"+c.ProcessId+"|"+c.BoundingRectangle+"|"+c.IsEnabled+"|"+c.IsOffscreen+state;
    }
    public static object Read(long window,bool raw) {
        entries.Clear();
        var root=AutomationElement.FromHandle(new IntPtr(window));
        var walker=raw?TreeWalker.RawViewWalker:TreeWalker.ControlViewWalker;
        var queue=new Queue<(AutomationElement Element,string Parent,int Depth)>();
        queue.Enqueue((root,null,0));
        var result=new List<object>(); var errors=new List<string>(); bool truncated=false;
        while(queue.Count>0 && result.Count<300) {
            var (element,parent,depth)=queue.Dequeue();
            try {
                var c=element.Current;
                if(c.IsPassword) continue;
                string id=Guid.NewGuid().ToString("N");
                var rect=c.BoundingRectangle; var w=DesktopNative.Inspect(window);
                string[] actions=element.GetSupportedPatterns().Select(p=>p.ProgrammaticName.Replace("PatternIdentifiers.Pattern","")).ToArray();
                entries[id]=new Entry(element,Identity(element),window);
                object value=null;
                if(element.TryGetCurrentPattern(ValuePattern.Pattern,out var v)) value=((ValuePattern)v).Current.Value;
                object selected=null,toggleState=null,expandState=null;
                if(element.TryGetCurrentPattern(SelectionItemPattern.Pattern,out var s))selected=((SelectionItemPattern)s).Current.IsSelected;
                if(element.TryGetCurrentPattern(TogglePattern.Pattern,out var t))toggleState=((TogglePattern)t).Current.ToggleState.ToString();
                if(element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern,out var a))expandState=((ExpandCollapsePattern)a).Current.ExpandCollapseState.ToString();
                result.Add(new {element_id=id,parent_id=parent,name=c.Name,type=c.ControlType.ProgrammaticName,automation_id=c.AutomationId,
                    bounds=rect.IsEmpty?null:(object)new {x=rect.X-w.x,y=rect.Y-w.y,width=rect.Width,height=rect.Height},enabled=c.IsEnabled,offscreen=c.IsOffscreen,
                    focused=c.HasKeyboardFocus,selected,toggle_state=toggleState,expand_state=expandState,depth,patterns=actions,value,source="uia"});
                var child=walker.GetFirstChild(element);
                if(depth>=8) {if(child!=null)truncated=true;}
                else for(;child!=null;child=walker.GetNextSibling(child)) {
                    if(queue.Count>=600){truncated=true;break;}
                    queue.Enqueue((child,id,depth+1));
                }
            } catch(Exception ex) { if(errors.Count<10) errors.Add(ex.Message); }
        }
        return new {controls=result,truncated=truncated||queue.Count>0,errors,view=raw?"raw":"control"};
    }
    public static object Act(long window,string id,string action,string value) {
        if(!entries.Remove(id,out var entry) || entry.Window!=window) throw new Exception("控件引用失效，请重新 observe");
        var e=entry.Element;
        if(Identity(e)!=entry.Identity || !e.Current.IsEnabled || e.Current.IsOffscreen || e.Current.IsPassword) throw new Exception("控件身份、位置或状态已变化，请重新观察");
        DesktopNative.PrepareControl(window);
        DesktopFeedback.Check();
        if(Identity(e)!=entry.Identity)throw new Exception("激活窗口期间控件发生变化，请重新观察");
        bool verified=false; object state=null;
        switch(action) {
            case "invoke": ((InvokePattern)e.GetCurrentPattern(InvokePattern.Pattern)).Invoke(); break;
            case "select":
                var selection=(SelectionItemPattern)e.GetCurrentPattern(SelectionItemPattern.Pattern); selection.Select();
                state=selection.Current.IsSelected; verified=(bool)state; break;
            case "set_value":
                var edit=(ValuePattern)e.GetCurrentPattern(ValuePattern.Pattern);
                if(edit.Current.IsReadOnly) throw new Exception("控件为只读");
                edit.SetValue(value??""); state=edit.Current.Value; verified=(string)state==(value??""); break;
            case "toggle":
                var toggle=(TogglePattern)e.GetCurrentPattern(TogglePattern.Pattern);var previous=toggle.Current.ToggleState;toggle.Toggle(); state=toggle.Current.ToggleState.ToString(); verified=toggle.Current.ToggleState!=previous; break;
            case "expand": case "collapse":
                var expand=(ExpandCollapsePattern)e.GetCurrentPattern(ExpandCollapsePattern.Pattern);
                if(action=="expand") expand.Expand(); else expand.Collapse();
                state=expand.Current.ExpandCollapseState.ToString(); verified=(string)state==(action=="expand"?"Expanded":"Collapsed"); break;
            case "focus": e.SetFocus(); state=e.Current.HasKeyboardFocus; verified=(bool)state; break;
            default: throw new Exception("不支持的控件操作");
        }
        entries.Clear();
        return new {sent=true,verified,state,verification_scope="control-state-only",next="重新观察以核对业务结果；invoke 不保证任务完成"};
    }
    public static void Clear() {entries.Clear();}
}
