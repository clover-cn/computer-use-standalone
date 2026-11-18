using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

static class Fixture {
    public static void Run() {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var form=new Form {Text="ComputerUse.TestFixture",ClientSize=new Size(680,420),StartPosition=FormStartPosition.CenterScreen};
        var status=new Label {Text="Ready",Name="status",Bounds=new Rectangle(20,20,200,30)};
        TextBox edit=null;
        Action replace=()=>{if(edit!=null){form.Controls.Remove(edit);edit.Dispose();}edit=new TextBox{Name="edit",Text="Initial",Bounds=new Rectangle(20,65,250,30)};form.Controls.Add(edit);};
        var button=new Button {Text="Invoke",Name="invoke",Bounds=new Rectangle(300,65,120,35)};
        button.Click+=(_,_)=>status.Text="Clicked";
        var check=new CheckBox {Text="Toggle",Name="toggle",Bounds=new Rectangle(20,110,150,35)};
        var list=new ListBox{Name="list",Bounds=new Rectangle(20,160,200,80)};list.Items.AddRange(new object[]{"First","Second"});
        var tree=new TreeView{Name="tree",Bounds=new Rectangle(300,150,200,90)};var parent=tree.Nodes.Add("Parent");parent.Nodes.Add("Child");
        var text=new Label{Text="中国 测试 HELLO 123",Font=new Font("Microsoft YaHei UI",25),Bounds=new Rectangle(20,280,630,60)};
        var canvas=new Panel{Name="canvas",BackColor=Color.White,Bounds=new Rectangle(20,350,620,60)};
        bool down=false;int moves=0;Point last=Point.Empty;
        canvas.MouseDown+=(_,e)=>{down=true;moves=0;last=e.Location;};
        canvas.MouseMove+=(_,e)=>{if(down){moves++;using var g=canvas.CreateGraphics();g.DrawLine(Pens.Black,last,e.Location);last=e.Location;}};
        canvas.MouseUp+=(_,_)=>{down=false;status.Text="Stroke:"+moves;};
        form.Controls.AddRange(new Control[]{status,button,check,list,tree,text,canvas});replace();
        form.Shown+=(_,_)=>Console.WriteLine("fixture-ready");
        _=Task.Run(()=>{string line;while((line=Console.ReadLine())!=null){if(line=="replace")form.BeginInvoke(replace);if(line=="close"){form.BeginInvoke((Action)form.Close);break;}}});
        Application.Run(form);
    }
}
