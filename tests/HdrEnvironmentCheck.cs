using System;using System.IO;using System.Linq;using System.Reflection;using System.Windows.Forms;using System.Drawing;using System.Diagnostics;using System.Web.Script.Serialization;using System.Collections.Generic;using Microsoft.Web.WebView2.WinForms;using Microsoft.Web.WebView2.Core;
class HdrEnvironmentCheck{
static BindingFlags f=BindingFlags.Instance|BindingFlags.NonPublic;static JavaScriptSerializer json=new JavaScriptSerializer();static object Get(object o,string n){return o.GetType().GetField(n,f).GetValue(o);}static void Pump(int ms=10){var c=Stopwatch.StartNew();while(c.ElapsedMilliseconds<ms){Application.DoEvents();System.Threading.Thread.Sleep(2);}}static string JS(WebView2 w,string s){var task=w.CoreWebView2.ExecuteScriptAsync(s);while(!task.IsCompleted)Pump();return task.GetAwaiter().GetResult();}static Dictionary<string,object> State(WebView2 w){return json.Deserialize<Dictionary<string,object>>(JS(w,"window.modelState()"));}
static void Mouse(WebView2 w,string type,double x,double y,bool pressed=false){var args=new Dictionary<string,object>{{"type",type},{"x",x},{"y",y},{"button",type=="mouseMoved"&&!pressed?"none":"left"},{"buttons",pressed?1:0},{"clickCount",1},{"modifiers",8}};var task=w.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",json.Serialize(args));while(!task.IsCompleted)Pump();task.GetAwaiter().GetResult();Pump(20);}
static double[] Numbers(object a){return ((System.Collections.IEnumerable)a).Cast<object>().Select(Convert.ToDouble).ToArray();}
static Bitmap Capture(WebView2 w){Pump(180);using(var stream=new MemoryStream()){var task=w.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);while(!task.IsCompleted)Pump();task.GetAwaiter().GetResult();stream.Position=0;using(var b=new Bitmap(stream))return new Bitmap(b);}}
static void WaitHdr(WebView2 w,bool success=true){var timer=Stopwatch.StartNew();while(JS(w,"window.modelState().environment.loading")=="true"&&timer.ElapsedMilliseconds<15000)Pump(30);if(JS(w,"window.modelState().environment.loading")=="true")throw new Exception("HDR load timeout");if(success&&JS(w,"window.modelState().environment.error")!="\"\"")throw new Exception(JS(w,"window.modelState().environment.error"));}
static void CheckDifferent(Bitmap a,Bitmap b,string name){int changed=0;for(int y=170;y<a.Height-70;y+=3)for(int x=100;x<a.Width-100;x+=3){var c=a.GetPixel(x,y);var d=b.GetPixel(x,y);if(Math.Abs(c.R-d.R)+Math.Abs(c.G-d.G)+Math.Abs(c.B-d.B)>10)changed++;}Console.WriteLine(name+": "+changed+" samples changed");if(changed<30)throw new Exception("No visible change: "+name);}
[STAThread]static int Main(string[] args){try{Application.EnableVisualStyles();var a=Assembly.LoadFrom(@"D:\chatGPT\LumaView\LumaView.exe");using(var p=(Form)Activator.CreateInstance(a.GetType("LumaView.ModelViewer"),new object[]{args.Length>0?args[0]:@"D:\chatGPT\LumaView\dependencies\viewport-tests\ao-corner.gltf",new Rectangle(0,0,1100,760)})){
p.Show();var clock=Stopwatch.StartNew();while(!(bool)Get(p,"ready")&&clock.ElapsedMilliseconds<20000)Pump();if(!(bool)Get(p,"ready"))throw new Exception(((Label)Get(p,"status")).Text);var w=(WebView2)Get(p,"web");


JS(w,"window.fxErrors=[];const log=console.error;console.error=(...a)=>{window.fxErrors.push(a.map(String).join(' '));log(...a);};");
var controls=(ToolStrip)Get(p,"tools");if(controls.Items.Cast<ToolStripItem>().Any(x=>x.Text=="스카이돔"||x.Text=="돔 밝기"))throw new Exception("Legacy toolbar controls still present");
foreach(string map in new[]{"studio_small_09","studio_small_03","venice_sunset","forest_slope"}){
 JS(w,"window.selectEnvironment('"+map+"')");WaitHdr(w);
 if(JS(w,"window.modelState().environment.map")!=json.Serialize(map))throw new Exception("Wrong map loaded");
 if(JS(w,"window.modelState().environment.width")!="2048")throw new Exception("Wrong HDR dimensions");
 JS(w,"window.setEnvironment({rotation:0})");using(var first=Capture(w)){JS(w,"window.setEnvironment({rotation:150})");using(var second=Capture(w)){CheckDifferent(first,second,map+" rotation");}}
 Console.WriteLine("PASS map: "+map);
}
JS(w,"window.selectEnvironment('studio_small_09')");WaitHdr(w);
JS(w,"window.setEnvironment({rotation:0,background:false,intensity:1})");
using(var first=Capture(w)){JS(w,"window.setEnvironment({intensity:0})");using(var second=Capture(w)){CheckDifferent(first,second,"intensity");}}
JS(w,"window.setEnvironment({intensity:1,background:true,blur:0})");using(var first=Capture(w)){JS(w,"window.setEnvironment({blur:.8})");using(var second=Capture(w)){CheckDifferent(first,second,"background blur");}}
JS(w,"window.setEnvironment({blur:.1});window.toggleRenderEffects(false)");using(var picture=Capture(w)){using(var stream=new MemoryStream()){picture.Save(stream,System.Drawing.Imaging.ImageFormat.Png);Console.WriteLine("IMAGE:hdr-panel:"+Convert.ToBase64String(stream.ToArray()));}}
JS(w,"window.toggleRenderEffects(false)");var camera=JS(w,"window.modelState().camera");Mouse(w,"mousePressed",220,350,true);Mouse(w,"mouseMoved",420,350,true);Mouse(w,"mouseReleased",420,350);if(JS(w,"window.modelState().camera")!=camera)throw new Exception("HDR drag moved camera");if(JS(w,"window.modelState().environment.rotation")!="100")throw new Exception("HDR drag did not rotate");
JS(w,"window.importDone=false;fetch('./environments/venice_sunset.hdr').then(r=>r.blob()).then(b=>window.importEnvironment(new File([b],'custom.hdr'))).then(()=>window.importDone=true)");var timeout=Stopwatch.StartNew();while(JS(w,"window.importDone")!="true"&&timeout.ElapsedMilliseconds<15000)Pump(30);if(JS(w,"window.modelState().environment.map")!="\"custom\"")throw new Exception("Custom HDR import failed");
JS(w,"window.importEnvironment(new File(['not HDR'],'invalid.hdr'))");WaitHdr(w,false);if(JS(w,"window.modelState().environment.map")!="\"custom\"")throw new Exception("Failed import destroyed previous map");if(JS(w,"!!window.modelState().environment.error")!="true")throw new Exception("Missing import error");
JS(w,"window.selectEnvironment('forest_slope');window.selectEnvironment('studio_small_03');window.setEnvironment({enabled:false})");WaitHdr(w);if(JS(w,"window.modelState().environment.map")!="\"studio_small_03\""||JS(w,"window.modelState().environment.enabled")!="false")throw new Exception("Rapid selection/disable race");
JS(w,"window.setEnvironment({enabled:true,background:false});window.setRenderEffects({bloom:true,shadows:true});window.modelCommand('ssao')");Pump(200);using(var picture=Capture(w)){}
if(JS(w,"window.fxErrors.length")!="0")throw new Exception("GPU errors: "+JS(w,"window.fxErrors"));
p.Close();}
Console.WriteLine("PASS HDR maps, reflection rotation, intensity, background blur, panel, Shift drag, custom import, invalid input, rapid selection, GPU shaders");return 0;}catch(Exception ex){Console.WriteLine(ex);return 1;}}
}



