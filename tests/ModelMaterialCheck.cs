using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

class ModelMaterialCheck {
    static void Wait(Func<bool> condition,string description){
        var timer=Stopwatch.StartNew();
        while(!condition()){
            if(timer.ElapsedMilliseconds>20000)throw new Exception("Timeout: "+description);
            Application.DoEvents();Thread.Sleep(5);
        }
    }
    static void Pump(int ms){var t=Stopwatch.StartNew();Wait(()=>t.ElapsedMilliseconds>=ms,"render");}
    static T Finish<T>(Task<T> task){Wait(()=>task.IsCompleted,"WebView2");return task.GetAwaiter().GetResult();}
    WebView2 web;
    ToolStrip tools;
    string JS(string code){return Finish(web.CoreWebView2.ExecuteScriptAsync(code));}
    void Check(string expression){if(JS(expression)!="true")throw new Exception("Failed: "+expression);}
    void Button(string ko,string en){
        tools.Items.Cast<ToolStripItem>().Single(i=>i.Text==ko||i.Text==en).PerformClick();Pump(120);
    }
    void Panel(bool open){
        if(JS("!panel.hidden")!=(open?"true":"false"))Button("재질","Materials");
        Check(open?"!panel.hidden":"panel.hidden");
    }
    void Sample(bool left,bool right,string label){
        Panel(false);Pump(180);
        using(var stream=new MemoryStream()){
            var t=web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
            Wait(()=>t.IsCompleted,"capture");t.GetAwaiter().GetResult();stream.Position=0;
            using(var b=new Bitmap(stream)){
                var bg=b.GetPixel(10,b.Height/2);
                foreach(var sample in new[]{new{X=.4,Visible=left},new{X=.6,Visible=right}}){
                    var c=b.GetPixel((int)(sample.X*b.Width),b.Height/2);
                    int delta=Math.Abs(c.R-bg.R)+Math.Abs(c.G-bg.G)+Math.Abs(c.B-bg.B);
                    if((delta>22)!=sample.Visible)throw new Exception(label+" x="+sample.X+" pixel="+c+" background="+bg);
                }
            }
        }
        Console.WriteLine("PASS "+label);
    }
    void Replace(string slot,Color color,string name){
        using(var b=new Bitmap(2,2))using(var stream=new MemoryStream()){
            using(var graphics=Graphics.FromImage(b))graphics.Clear(color);
            b.Save(stream,ImageFormat.Png);
            var data=Convert.ToBase64String(stream.ToArray());
            JS("(()=>{const inp=panel.querySelector('[data-slot="+slot+"] input');const transfer=new DataTransfer();"+
               "transfer.items.add(new File([Uint8Array.from(atob('"+data+"'),c=>c.charCodeAt(0))],'"+name+"',{type:'image/png'}));"+
               "inp.files=transfer.files;inp.dispatchEvent(new Event('change'));})()");
        }
        Wait(()=>JS("panel.querySelector('[data-slot="+slot+"] .material-filename').textContent")=="\""+name+"\"","relinked "+name);
    }
    void Run(string model){
        var assembly=Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"DollView.exe"));
        var type=assembly.GetType("LumaView.ModelViewer");
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using(var form=(Form)Activator.CreateInstance(type,new object[]{model,new Rectangle(20,20,1000,680)})){
            form.Show();Wait(()=>(bool)type.GetField("ready",flags).GetValue(form),"model");
            web=(WebView2)type.GetField("web",flags).GetValue(form);tools=(ToolStrip)type.GetField("tools",flags).GetValue(form);
            JS("window.panel=document.querySelector('#materials');window.list=panel.querySelector('select');"+
               "window.cutoff=panel.querySelector('[type=range]');window.opaque=panel.querySelector('[type=checkbox]');"+
               "window.resetMat=panel.querySelector('#material-reset');modelCommand('front');if(modelState().grid)modelCommand('grid');");
            Sample(false,false,"authored mask and zero opacity");
            foreach(var mode in new[]{"standard","flat","toon"}){
                if(mode=="flat")Button("플랫 컬러","Flat color");
                if(mode=="toon")JS("setRenderEffects({toon:true})");
                Button("텍스처","Textures");Sample(true,true,mode+" textures off");
                Button("텍스처","Textures");Sample(false,false,mode+" textures restored");
                if(mode=="flat")Button("플랫 컬러","Flat color");
                if(mode=="toon")JS("setRenderEffects({toon:false})");
            }
            Panel(true);Check("list.options.length===2 && list.options[0].text==='Feather'");
            JS("list.selectedIndex=1;list.dispatchEvent(new Event('change'));opaque.click()");
            Sample(false,true,"opaque preview is per material");
            Panel(true);JS("resetMat.click()");Sample(false,false,"restore original opacity");
            Button("텍스처","Textures");Panel(true);
            var x=JS("Math.round(innerWidth*.40)");var y=JS("Math.round(innerHeight*.5)");
            foreach(string mode in new[]{"mousePressed","mouseReleased"}){
                Finish(web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent","{\"type\":\""+mode+"\",\"x\":"+x+",\"y\":"+y+",\"button\":\"left\",\"buttons\":"+(mode=="mousePressed"?1:0)+",\"clickCount\":1}"));
            }
            Wait(()=>JS("list.value")=="\"0\"","pick first material on visible geometry");
            Button("텍스처","Textures");
            Replace("map",Color.Red,"new-color.png");Sample(true,false,"replacement color image supplies visible alpha");
            Panel(true);Replace("alphaMap",Color.FromArgb(110,255,0,0),"new-alpha.png");
            Sample(false,false,"replacement opacity image uses alpha channel");
            Panel(true);JS("cutoff.value=.2;cutoff.dispatchEvent(new Event('input'))");
            Sample(true,false,"lower cutoff reveals feathers");
            Panel(true);JS("resetMat.click()");Sample(false,false,"restore original maps and cutoff");
            form.Close();
        }
    }
    [STAThread]static int Main(string[] args){
        try{Application.EnableVisualStyles();new ModelMaterialCheck().Run(Path.GetFullPath(args.Length==0?@"tests\fixtures\opacity.gltf":args[0]));return 0;}
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
