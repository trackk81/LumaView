using System;using System.IO;using System.Linq;using System.Reflection;using System.Windows.Forms;using System.Drawing;using System.Diagnostics;using System.Web.Script.Serialization;using System.Collections.Generic;using Microsoft.Web.WebView2.WinForms;using Microsoft.Web.WebView2.Core;
class ModelToolbarCheck{
static BindingFlags f=BindingFlags.Instance|BindingFlags.NonPublic;static JavaScriptSerializer json=new JavaScriptSerializer();static object Get(object o,string n){return o.GetType().GetField(n,f).GetValue(o);}static void Pump(int ms=10){var c=Stopwatch.StartNew();while(c.ElapsedMilliseconds<ms){Application.DoEvents();System.Threading.Thread.Sleep(2);}}static string JS(WebView2 w,string s){var task=w.CoreWebView2.ExecuteScriptAsync(s);while(!task.IsCompleted)Pump();return task.GetAwaiter().GetResult();}static Dictionary<string,object> State(WebView2 w){return json.Deserialize<Dictionary<string,object>>(JS(w,"window.modelState()"));}
static void Mouse(WebView2 w,string type,double x,double y,bool pressed=false){var args=new Dictionary<string,object>{{"type",type},{"x",x},{"y",y},{"button",type=="mouseMoved"&&!pressed?"none":"left"},{"buttons",pressed?1:0},{"clickCount",1}};var task=w.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",json.Serialize(args));while(!task.IsCompleted)Pump();task.GetAwaiter().GetResult();Pump(20);}
static double[] Numbers(object a){return ((System.Collections.IEnumerable)a).Cast<object>().Select(Convert.ToDouble).ToArray();}
static Bitmap Capture(WebView2 w){Pump(180);using(var stream=new MemoryStream()){var task=w.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);while(!task.IsCompleted)Pump();task.GetAwaiter().GetResult();stream.Position=0;using(var b=new Bitmap(stream))return new Bitmap(b);}}
[System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
[System.Runtime.InteropServices.DllImport("user32.dll")]static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
static void ClickItem(ToolStrip bar,ToolStripItem item){var pos=bar.PointToScreen(new Point(item.Bounds.Left+item.Width/2,item.Bounds.Top+item.Height/2));SetCursorPos(pos.X,pos.Y);Pump(100);mouse_event(2,0,0,0,UIntPtr.Zero);Pump(30);mouse_event(4,0,0,0,UIntPtr.Zero);Pump(200);}
[STAThread]static int Main(string[] args){try{Application.EnableVisualStyles();var a=Assembly.LoadFrom(args.Length>0?args[0]:@"D:\chatGPT\LumaView\LumaView.exe");using(var p=(Form)Activator.CreateInstance(a.GetType("LumaView.ModelViewer"),new object[]{@"D:\chatGPT\LumaView\dependencies\viewport-tests\ao-corner.gltf",new Rectangle(0,0,1100,760)})){
p.Show();var clock=Stopwatch.StartNew();while(!(bool)Get(p,"ready")&&clock.ElapsedMilliseconds<20000)Pump();if(!(bool)Get(p,"ready"))throw new Exception(((Label)Get(p,"status")).Text);var w=(WebView2)Get(p,"web");
var bar=(ToolStrip)Get(p,"tools");var wire=bar.Items.Cast<ToolStripItem>().First(x=>x.Text=="와이어프레임");
for(int i=0;i<8;i++){p.Activate();w.Focus();Pump(60);bool before=(bool)State(w)["wire"];ClickItem(bar,wire);if((bool)State(w)["wire"]==before)throw new Exception("First toolbar click lost at cycle "+i);}
var fx=bar.Items.Cast<ToolStripItem>().First(x=>x.Text=="렌더링 효과");ClickItem(bar,fx);if(JS(w,"document.querySelector('#effects').hidden")!="false")throw new Exception("Effects first click failed");ClickItem(bar,fx);if(JS(w,"document.querySelector('#effects').hidden")!="true")throw new Exception("Effects close click failed");
Console.WriteLine("PASS 8 native single-click wire toggles after WebView focus; effects open and close on one click");
p.Close();}
Console.WriteLine("PASS model toolbar interaction");return 0;}catch(Exception ex){Console.WriteLine(ex);return 1;}}
}



