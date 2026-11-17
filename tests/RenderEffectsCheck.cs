using System;using System.IO;using System.Linq;using System.Reflection;using System.Windows.Forms;using System.Drawing;using System.Diagnostics;using System.Web.Script.Serialization;using System.Collections.Generic;using Microsoft.Web.WebView2.WinForms;using Microsoft.Web.WebView2.Core;
class RenderEffectsCheck{
static BindingFlags f=BindingFlags.Instance|BindingFlags.NonPublic;static JavaScriptSerializer json=new JavaScriptSerializer();static object Get(object o,string n){return o.GetType().GetField(n,f).GetValue(o);}static void Pump(int ms=10){var c=Stopwatch.StartNew();while(c.ElapsedMilliseconds<ms){Application.DoEvents();System.Threading.Thread.Sleep(2);}}static string JS(WebView2 w,string s){var task=w.CoreWebView2.ExecuteScriptAsync(s);while(!task.IsCompleted)Pump();return task.GetAwaiter().GetResult();}static Dictionary<string,object> State(WebView2 w){return json.Deserialize<Dictionary<string,object>>(JS(w,"window.modelState()"));}
static void Mouse(WebView2 w,string type,double x,double y,bool pressed=false){var args=new Dictionary<string,object>{{"type",type},{"x",x},{"y",y},{"button",type=="mouseMoved"&&!pressed?"none":"left"},{"buttons",pressed?1:0},{"clickCount",1}};var task=w.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",json.Serialize(args));while(!task.IsCompleted)Pump();task.GetAwaiter().GetResult();Pump(20);}
static double[] Numbers(object a){return ((System.Collections.IEnumerable)a).Cast<object>().Select(Convert.ToDouble).ToArray();}
static Bitmap Capture(WebView2 w){Pump(180);using(var stream=new MemoryStream()){var task=w.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);while(!task.IsCompleted)Pump();task.GetAwaiter().GetResult();stream.Position=0;using(var b=new Bitmap(stream))return new Bitmap(b);}}
[STAThread]static int Main(){try{Application.EnableVisualStyles();var a=Assembly.LoadFrom(@"D:\chatGPT\LumaView\LumaView.exe");using(var p=(Form)Activator.CreateInstance(a.GetType("LumaView.ModelViewer"),new object[]{@"D:\chatGPT\LumaView\dependencies\viewport-tests\ao-corner.gltf",new Rectangle(0,0,1100,760)})){
p.Show();var clock=Stopwatch.StartNew();while(!(bool)Get(p,"ready")&&clock.ElapsedMilliseconds<20000)Pump();if(!(bool)Get(p,"ready"))throw new Exception(((Label)Get(p,"status")).Text);var w=(WebView2)Get(p,"web");

JS(w,"window.fxErrors=[];const log=console.error;console.error=(...a)=>{window.fxErrors.push(a.map(String).join(' '));log(...a);};");
var captures=new Dictionary<string,Bitmap>();
foreach(string preset in new[]{"basic","realistic","toon","bloom"}){
 JS(w,"window.setRenderPreset('"+preset+"')");Pump(350);
 if(JS(w,"document.getElementById('message').textContent")!="\"\"")throw new Exception("Viewer error: "+JS(w,"document.getElementById('message').textContent"));
 var picture=Capture(w);captures[preset]=picture;using(var stream=new MemoryStream()){picture.Save(stream,System.Drawing.Imaging.ImageFormat.Png);Console.WriteLine("IMAGE:"+preset+":"+Convert.ToBase64String(stream.ToArray()));}
 Console.WriteLine(preset+": "+JS(w,"window.modelState().effects"));
}
foreach(string preset in new[]{"realistic","toon","bloom"}){long changed=0;var before=captures["basic"];var after=captures[preset];for(int y=160;y<before.Height-60;y+=3)for(int x=80;x<before.Width-80;x+=3){var c=before.GetPixel(x,y);var d=after.GetPixel(x,y);if(Math.Abs(c.R-d.R)+Math.Abs(c.G-d.G)+Math.Abs(c.B-d.B)>12)changed++;}if(changed<50)throw new Exception("Preset has no visible effect: "+preset);Console.WriteLine(preset+" changed pixels: "+changed);}
JS(w,"window.toggleRenderEffects(false);document.querySelector('#fx-preset').value='toon';document.querySelector('#fx-preset').dispatchEvent(new Event('change'));");
if(JS(w,"window.modelState().effects.toon")!="true")throw new Exception("Preset UI failed");
JS(w,"const slider=document.querySelector('[data-key=exposure]');slider.value=1.5;slider.dispatchEvent(new Event('input'));");if(JS(w,"window.modelState().effects.exposure")!="1.5")throw new Exception("Exposure slider failed");
p.Size=new Size(900,650);Pump(200);using(var picture=Capture(w)){using(var stream=new MemoryStream()){picture.Save(stream,System.Drawing.Imaging.ImageFormat.Png);Console.WriteLine("IMAGE:panel:"+Convert.ToBase64String(stream.ToArray()));}}
JS(w,"window.setRenderEffects({shadows:true,toon:true,outline:true,bloom:true,contrast:1.1,saturation:1.1});window.modelCommand('ssao');window.modelCommand('flat');");Pump(250);using(var picture=Capture(w)){}
JS(w,"window.setRenderPreset('basic')");if(JS(w,"Object.values(window.modelState().effects).filter(v=>v===true).length")!="0")throw new Exception("Reset failed");
if(JS(w,"window.fxErrors.length")!="0")throw new Exception("GPU errors: "+JS(w,"window.fxErrors"));
foreach(var picture in captures.Values)picture.Dispose();
p.Close();}
Console.WriteLine("PASS rendering presets, pixel differences, controls, resize, combined effects, reset, no GPU errors.");return 0;}catch(Exception ex){Console.WriteLine(ex);return 1;}}
}



