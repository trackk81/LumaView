using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

// Pass a test folder containing clip1.png, clip2.mp4, clip3.mov, clip4.png and clip10.png.
class MixedMediaDialCheck {
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd,int msg,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,int msg,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] static extern bool GetKeyboardState(byte[] state);
    [DllImport("user32.dll")] static extern bool SetKeyboardState(byte[] state);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int GetWindowLong(IntPtr hwnd,int index);
    static object Field(object obj,string name){return obj.GetType().GetField(name,Fields).GetValue(obj);}
    static string NameOfVideo(Form f){return f==null?"":Path.GetFileName((string)Field(f,"current"));}
    static Form Video(){return Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.GetType().Name=="VideoViewer");}
    static async Task Wait(Func<bool> done,string message){
        var t=Stopwatch.StartNew();
        while(!done()){if(t.ElapsedMilliseconds>15000)throw new Exception(message);await Task.Delay(12);}
    }
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Wheel(Control surface,int delta){
        var p=surface.PointToScreen(new Point(150,150));
        PostMessage(surface.Handle,0x020A,new IntPtr(delta<<16),new IntPtr((p.Y<<16)|(p.X&65535)));
    }
    static void DragMessage(Control surface,int message,int screenY){
        int y=screenY-surface.PointToScreen(Point.Empty).Y;
        SendMessage(surface.Handle,message,new IntPtr(message==0x208?0:0x10),new IntPtr((y<<16)|(surface.Width/2)));
    }
    static void SetMiddleButton(bool down){
        var state=new byte[256];Check(GetKeyboardState(state),"read thread input state");
        state[4]=(byte)(down?128:0);Check(SetKeyboardState(state),"set thread middle button");
    }
    static async Task Run(string folder,bool fullscreen){
        var app=Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"DollView.exe"));
        var ui=app.GetType("LumaView.Ui");
        string settings=Path.Combine(Path.GetTempPath(),"DollView-media-dial-"+Guid.NewGuid().ToString("N")+".ini");
        ui.GetField("SettingsPath").SetValue(null,settings);ui.GetMethod("Load").Invoke(null,null);
        Form viewer=(Form)Activator.CreateInstance(app.GetType("LumaView.Viewer"),new object[]{Path.Combine(folder,"clip1.png")});
        try{
            var decode=app.GetType("LumaView.MediaFiles").GetMethod("ReadDecoderThumbnail",BindingFlags.Static|BindingFlags.NonPublic);
            using(var frame=await Task.Run(()=>(Bitmap)decode.Invoke(null,new object[]{Path.Combine(folder,"clip3.mov")})))
                Check(frame!=null&&frame.Width>1&&frame.Width<=192&&frame.Height>1&&frame.Height<=160,"bundled decoder thumbnail");
            viewer.Show();
            var canvas=(Control)Field(viewer,"canvas");var album=Field(viewer,"album");
            Func<string> image=()=>Path.GetFileName((string)album.GetType().GetProperty("Current").GetValue(album,null));
            await Wait(()=>image()=="clip1.png","initial photo");
            if(fullscreen)((ToolStripButton)Field(viewer,"imageModeButton")).PerformClick();
            else{
                var work=Screen.FromControl(viewer).WorkingArea;
                viewer.Bounds=new Rectangle(work.Left+30,work.Top+40,Math.Min(720,work.Width-60),Math.Min(560,work.Height-80));
            }
            var expectedBounds=viewer.Bounds;
            Check(((string[])Field(album,"WheelFiles")).Select(Path.GetFileName).SequenceEqual(new[]{"clip1.png","clip2.mp4","clip3.mov","clip4.png","clip10.png"}),"mixed natural order");
            Wheel(canvas,-120);
            await Wait(()=>NameOfVideo(Video())=="clip2.mp4"&&(bool)Field(Video(),"ready"),"image to playing MP4");
            var movie=Video();var surface=(Control)Field(movie,"host");
            Check(!viewer.Visible&&(bool)Field(movie,"fullscreen")==fullscreen&&movie.Bounds==expectedBounds,"video inherits the image window");
            Wheel(surface,60);
            var overlay=(Form)Field(movie,"dialOverlay");
            await Wait(()=>overlay.Visible,"overlay above mpv");
            var sidebar=Field(movie,"sidebar");
            await Wait(()=>!(bool)Field(sidebar,"loading"),"video thumbnails");
            if(!fullscreen){
                movie.SetBounds(movie.Left+15,movie.Top+15,movie.Width-10,movie.Height-10);
                expectedBounds=movie.Bounds;
                await Wait(()=>overlay.Location==surface.PointToScreen((Point)sidebar.GetType().GetProperty("Location").GetValue(sidebar,null)),"sidebar follows moved and resized video window");
                await Wait(()=>!overlay.Visible,"idle video sidebar hides in a window");
            }
            var thumbs=(System.Collections.IDictionary)Field(sidebar,"thumbnails");
            Check(thumbs[Path.Combine(folder,"clip2.mp4")]!=null&&thumbs[Path.Combine(folder,"clip4.png")]!=null,"video and photo previews");
            Wheel(surface,-60);
            movie.Activate();surface.Focus();
            await Wait(()=>overlay.Visible,"reveal dial again");
            Check((GetWindowLong(overlay.Handle,-20)&0x80020)==0x80020,"click-through video overlay");
            var dialPoint=overlay.PointToScreen(new Point(overlay.Width/2,overlay.Height/2));
            PostMessage(surface.Handle,0x020A,new IntPtr(-120<<16),new IntPtr((dialPoint.Y<<16)|(dialPoint.X&65535)));
            await Wait(()=>NameOfVideo(movie)=="clip3.mov"&&(bool)Field(movie,"ready"),"wheel over video dial");
            movie.Activate();surface.Focus();
            int y=surface.PointToScreen(new Point(0,surface.Height/2)).Y;
            int stride=48*surface.DeviceDpi/96;
            SetMiddleButton(true);DragMessage(surface,0x207,y);await Task.Delay(50);
            Check(!(bool)Field(movie,"paused"),"middle button does not pause playback");
            DragMessage(surface,0x200,y+stride+2);
            await Wait(()=>Video()==null&&image()=="clip4.png","drag from movie to image");
            Check(viewer.Bounds==expectedBounds&&(bool)Field(viewer,"imageFullscreen")==fullscreen,"image inherits the video window");
            DragMessage(canvas,0x200,y+stride*2+2);
            await Wait(()=>image()=="clip10.png","same held drag continues across viewers");
            DragMessage(canvas,0x200,y+stride-2);await Wait(()=>image()=="clip4.png","drag reversal");
            viewer.BeginInvoke(new Action(()=>DragMessage(canvas,0x200,y-2)));
            await Wait(()=>NameOfVideo(Video())=="clip3.mov"&&(bool)Field(Video(),"ready"),"held drag back to movie");
            movie=Video();surface=(Control)Field(movie,"host");
            SetMiddleButton(false);DragMessage(surface,0x208,y-2);
            await Task.Delay(70);
            ((ToolStripButton)Field(movie,"sidebarButton")).PerformClick();
            Wheel(surface,240);await Wait(()=>Video()==null&&image()=="clip1.png","wheel back across two files");
            Check((bool)Field(viewer,"imageFullscreen")==fullscreen&&!((ToolStripButton)Field(viewer,"sidebarButton")).Checked,"view mode and toggle survive round trip");
            Wheel(canvas,120);await Task.Delay(70);Check(image()=="clip1.png","clamp at first file");
            viewer.GetType().GetMethod("ExitImageView",Fields).Invoke(viewer,null);
            var open=viewer.GetType().GetMethod("Open",Fields);
            viewer.BeginInvoke(new Action(()=>open.Invoke(viewer,new object[]{Path.Combine(folder,"clip3.mov")})));
            await Wait(()=>NameOfVideo(Video())=="clip3.mov"&&(bool)Field(Video(),"ready"),"open video directly");
            movie=Video();surface=(Control)Field(movie,"host");
            if(fullscreen)((ToolStripButton)Field(movie,"modeButton")).PerformClick();
            else movie.WindowState=FormWindowState.Maximized;
            var screen=movie.Bounds;
            Wheel(surface,-120);
            await Wait(()=>Video()==null&&image()=="clip4.png","direct video to neighboring image");
            Check(viewer.Bounds==screen&&(bool)Field(viewer,"imageFullscreen")==fullscreen,"retain display mode and bounds");
            if(!fullscreen)Check(viewer.WindowState==FormWindowState.Maximized,"retain maximized window");
            Console.WriteLine("PASS mixed media navigation, middle drag and sidebar in "+(fullscreen?"fullscreen":"windowed mode"));
        }finally{
            SetMiddleButton(false);
            var movie=Video();if(movie!=null)movie.Close();
            if(!viewer.IsDisposed)viewer.Close();
            if(File.Exists(settings))File.Delete(settings);
        }
    }
    [STAThread] static int Main(string[] args){
        Application.EnableVisualStyles();int result=1;
        using(var queue=new Control()){
            var handle=queue.Handle;
            queue.BeginInvoke(new Action(async delegate{
                try{string folder=Path.GetFullPath(args[0]);await Run(folder,false);await Run(folder,true);result=0;}catch(Exception e){Console.WriteLine(e);}
                finally{Application.ExitThread();}
            }));
            Application.Run();
        }
        return result;
    }
}
