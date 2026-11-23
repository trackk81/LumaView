using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

class WheelSidebarCheck {
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    static object Field(object owner,string name){return owner.GetType().GetField(name,Fields).GetValue(owner);}
    static void Wait(Func<bool> condition,string reason){
        var t=Stopwatch.StartNew();
        while(!condition()){
            if(t.ElapsedMilliseconds>10000)throw new Exception("Timeout: "+reason);
            Application.DoEvents();Thread.Sleep(5);
        }
    }
    static void Check(bool condition,string reason){if(!condition)throw new Exception(reason);}
    static Color ColorFor(int n){return Color.FromArgb(35+20*n,200-17*n,80+11*n);}
    readonly string temp=Path.Combine(Path.GetTempPath(),"DollView-wheel-test-"+Guid.NewGuid().ToString("N"));
    Control canvas;
    object album,sidebar;
    Form viewer;
    Bitmap Layer(){return (Bitmap)sidebar.GetType().GetMethod("Layer").Invoke(sidebar,null);}
    void Wheel(int delta){
        var p=canvas.PointToScreen(new Point(canvas.Width/2,canvas.Height/2));
        SendMessage(canvas.Handle,0x20A,new IntPtr(delta<<16),new IntPtr((p.Y<<16)|(p.X&0xffff)));
    }
    void At(int index){
        Wait(()=>(int)Field(album,"Index")==index&&!(bool)Field(viewer,"busy"),"image "+index);
    }
    void Center(int index){
        var b=Layer();
        Check(b!=null&&b.GetPixel(b.Width/2,b.Height/2).ToArgb()==ColorFor(index).ToArgb(),"focused thumbnail "+index);
    }
    static void CheckVisibleCards(Bitmap layer,int first,int last){
        int y=0;
        for(int index=first;index<=last;index++){
            var expected=ColorFor(index);
            int run=0,minRows=Math.Max(4,layer.Height/45);
            while(y<layer.Height&&run<minRows){
                var p=layer.GetPixel(layer.Width/2,y++);
                // Premultiplied channels lose precision as the outer thumbnails fade.
                int tolerance=Math.Max(4,160/Math.Max(1,(int)p.A));
                bool matches=p.A>=10&&Math.Abs(p.R-expected.R)<=tolerance&&Math.Abs(p.G-expected.G)<=tolerance&&Math.Abs(p.B-expected.B)<=tolerance;
                run=matches?run+1:0;
            }
            Check(run>=minRows,"thumbnail "+index+" present in navigation order");
        }
    }
    void CompositedCenter(int index){
        canvas.Refresh();
        var location=(Point)sidebar.GetType().GetProperty("Location").GetValue(sidebar,null);
        var b=Layer();
        location.Offset(b.Width/2,b.Height/2);
        using(var frame=new Bitmap(canvas.Width,canvas.Height)){
            canvas.DrawToBitmap(frame,new Rectangle(Point.Empty,frame.Size));
            Check(frame.GetPixel(location.X,location.Y).ToArgb()==ColorFor(index).ToArgb(),"composited thumbnail "+index);
        }
    }
    void Run(string fixtures){
        var assembly=Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"DollView.exe"));
        var ui=assembly.GetType("LumaView.Ui");
        Directory.CreateDirectory(temp);
        try{
            var settings=Path.Combine(temp,"settings.ini");
            ui.GetField("SettingsPath").SetValue(null,settings);
            ui.GetMethod("Load").Invoke(null,null);
            if(fixtures==null){
                fixtures=temp;
                for(int i=0;i<11;i++){
                    using(var b=new Bitmap(i%2==0?80:160,100)){
                        using(var g=Graphics.FromImage(b))g.Clear(ColorFor(i));
                        b.Save(Path.Combine(fixtures,i+".png"),ImageFormat.Png);
                    }
                }
            }
            using(viewer=(Form)Activator.CreateInstance(assembly.GetType("LumaView.Viewer"),new object[]{Path.Combine(fixtures,"4.png")})){
                canvas=(Control)Field(viewer,"canvas");album=Field(viewer,"album");sidebar=Field(canvas,"Sidebar");
                viewer.Show();At(4);
                var toggle=(ToolStripButton)Field(viewer,"sidebarButton");
                var mode=(ToolStripButton)Field(viewer,"imageModeButton");
                Check(toggle.Available&&toggle.Checked&&Layer()==null,"sidebar is enabled but idle in a window");
                mode.PerformClick();Check(toggle.Available&&Layer()==null,"enter fullscreen");
                Wheel(-120);At(5);Center(5);
                Wait(()=>(int)Field(sidebar,"direction")==0&&!(bool)Field(sidebar,"loading"),"neighbors");
                var b=Layer();
                Check(b.GetPixel(b.Width/2,b.Height/2-5).A==255&&b.GetPixel(b.Width/2,0).A<10&&b.GetPixel(b.Width/2,b.Height-1).A<10,"edge fade");
                CheckVisibleCards(b,0,10);
                // Sample opaque fixture photos at the five resting card centers on each side.
                foreach(int side in new[]{-1,1}){
                    int previousAlpha=255;
                    foreach(double offset in new[]{.107,.21,.306,.392,.466}){
                        int alpha=b.GetPixel(b.Width/2,(int)(b.Height*(.5+side*offset))).A;
                        if(offset==.107)Check(alpha==255,"first neighbor stays sharp");
                        else Check(alpha<previousAlpha,"fade increases from second neighbor out");
                        previousAlpha=alpha;
                    }
                    Check(previousAlpha>0&&previousAlpha<=25,"fifth neighbor is barely visible");
                }
                canvas.Refresh();
                var gpu=Field(canvas,"gpu");
                if(!(bool)Field(canvas,"gpuUnavailable")){
                    Check(gpu!=null&&(IntPtr)Field(gpu,"sidebarTexture")!=IntPtr.Zero&&ReferenceEquals(Field(gpu,"sidebarSource"),Layer()),"Direct2D sidebar upload");
                    Console.WriteLine("PASS Direct2D sidebar texture");
                }
                canvas.GetType().GetField("gpuUnavailable",Fields).SetValue(canvas,true);
                Wheel(120);At(4);Center(4);CompositedCenter(4);
                Wheel(-240);Wheel(-120);At(7);Center(7);
                Wheel(60);At(7);Wheel(60);At(6);
                Wheel(-600);At(10);Wheel(-120);At(10);Center(10);
                Wait(()=>(int)Field(sidebar,"direction")==0&&!(bool)Field(sidebar,"loading"),"settled at last image");
                b=Layer();CheckVisibleCards(b,5,10);Check(b.GetPixel(b.Width/2,b.Height*3/4).A==0,"no thumbnails below last image");
                Wheel(1200);At(0);Wheel(120);At(0);Center(0);
                Wait(()=>(int)Field(sidebar,"direction")==0&&!(bool)Field(sidebar,"loading"),"settled at first image");
                b=Layer();CheckVisibleCards(b,0,5);Check(b.GetPixel(b.Width/2,b.Height/4).A==0,"no thumbnails above first image");
                Wait(()=>Layer()==null,"auto-hide");
                toggle.PerformClick();Check(!toggle.Checked&&File.ReadAllText(settings).Contains("wheelSidebar=off"),"persist off");
                Wheel(-120);At(1);Check(Layer()==null,"navigate while disabled");
                toggle.PerformClick();Center(1);Check(File.ReadAllText(settings).Contains("wheelSidebar=on"),"persist on");
                mode.PerformClick();Check(toggle.Available&&Layer()==null,"leave fullscreen");
                Wheel(120);At(0);Center(0);CompositedCenter(0);
                Wait(()=>Layer()==null,"windowed sidebar hides after inactivity");
                viewer.Close();
            }
        }finally{
            foreach(var f in Directory.GetFiles(temp))File.Delete(f);
            Directory.Delete(temp);
        }
    }
    [STAThread] static int Main(string[] args){
        Application.EnableVisualStyles();
        int status=1;
        using(var pump=new Control()){
            var handle=pump.Handle;
            pump.BeginInvoke(new Action(delegate{
                try{new WheelSidebarCheck().Run(args.Length>0?args[0]:null);Console.WriteLine("PASS fullscreen wheel sidebar");status=0;}
                catch(Exception e){Console.Error.WriteLine(e);}
                finally{Application.ExitThread();}
            }));
            Application.Run();
        }
        return status;
    }
}
