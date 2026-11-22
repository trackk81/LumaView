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
    [DllImport("user32.dll")] static extern void mouse_event(uint flags,uint dx,uint dy,int data,UIntPtr extra);
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
    static void MovePointer(Control surface,int screenY){
        Cursor.Position=new Point(surface.PointToScreen(new Point(surface.Width/2,0)).X,screenY);
    }
    static async Task Run(string folder){
        var app=Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"DollView.exe"));
        var ui=app.GetType("LumaView.Ui");
        string settings=Path.Combine(Path.GetTempPath(),"DollView-media-dial-"+Guid.NewGuid().ToString("N")+".ini");
        ui.GetField("SettingsPath").SetValue(null,settings);ui.GetMethod("Load").Invoke(null,null);
        Form viewer=(Form)Activator.CreateInstance(app.GetType("LumaView.Viewer"),new object[]{Path.Combine(folder,"clip1.png")});
        var savedCursor=Cursor.Position;
        try{
            var decode=app.GetType("LumaView.MediaFiles").GetMethod("ReadDecoderThumbnail",BindingFlags.Static|BindingFlags.NonPublic);
            using(var frame=await Task.Run(()=>(Bitmap)decode.Invoke(null,new object[]{Path.Combine(folder,"clip3.mov")})))
                Check(frame!=null&&frame.Width>1&&frame.Width<=192&&frame.Height>1&&frame.Height<=160,"bundled decoder thumbnail");
            viewer.Show();
            var canvas=(Control)Field(viewer,"canvas");var album=Field(viewer,"album");
            Func<string> image=()=>Path.GetFileName((string)album.GetType().GetProperty("Current").GetValue(album,null));
            await Wait(()=>image()=="clip1.png","initial photo");
            ((ToolStripButton)Field(viewer,"imageModeButton")).PerformClick();
            Check(((string[])Field(album,"WheelFiles")).Select(Path.GetFileName).SequenceEqual(new[]{"clip1.png","clip2.mp4","clip3.mov","clip4.png","clip10.png"}),"mixed natural order");
            Wheel(canvas,-120);
            await Wait(()=>NameOfVideo(Video())=="clip2.mp4"&&(bool)Field(Video(),"ready"),"image to playing MP4");
            var movie=Video();var surface=(Control)Field(movie,"host");
            Check(!viewer.Visible&&(bool)Field(movie,"fullscreen"),"one visible fullscreen viewer");
            Wheel(surface,60);
            var overlay=(Form)Field(movie,"dialOverlay");
            await Wait(()=>overlay.Visible,"overlay above mpv");
            var sidebar=Field(movie,"sidebar");
            await Wait(()=>!(bool)Field(sidebar,"loading"),"video thumbnails");
            var thumbs=(System.Collections.IDictionary)Field(sidebar,"thumbnails");
            Check(thumbs[Path.Combine(folder,"clip2.mp4")]!=null&&thumbs[Path.Combine(folder,"clip4.png")]!=null,"video and photo previews");
            Wheel(surface,-60);
            movie.Activate();surface.Focus();
            Cursor.Position=overlay.PointToScreen(new Point(overlay.Width/2,overlay.Height/2));
            await Task.Delay(70);
            mouse_event(0x800,0,0,-120,UIntPtr.Zero);
            await Wait(()=>NameOfVideo(movie)=="clip3.mov"&&(bool)Field(movie,"ready"),"native wheel over video dial");
            movie.Activate();surface.Focus();
            int y=surface.PointToScreen(new Point(0,surface.Height/2)).Y;
            int stride=48*surface.DeviceDpi/96;
            MovePointer(surface,y);mouse_event(0x20,0,0,0,UIntPtr.Zero);await Task.Delay(50);
            Check(!(bool)Field(movie,"paused"),"middle button does not pause playback");
            MovePointer(surface,y+stride+2);
            await Wait(()=>Video()==null&&image()=="clip4.png","drag from movie to image");
            MovePointer(canvas,y+stride*2+2);
            await Wait(()=>image()=="clip10.png","same held drag continues across viewers");
            MovePointer(canvas,y+stride-2);await Wait(()=>image()=="clip4.png","drag reversal");
            MovePointer(canvas,y-2);
            await Wait(()=>NameOfVideo(Video())=="clip3.mov"&&(bool)Field(Video(),"ready"),"held drag back to movie");
            movie=Video();surface=(Control)Field(movie,"host");
            mouse_event(0x40,0,0,0,UIntPtr.Zero);
            await Task.Delay(70);
            ((ToolStripButton)Field(movie,"sidebarButton")).PerformClick();
            Wheel(surface,240);await Wait(()=>Video()==null&&image()=="clip1.png","wheel back across two files");
            Check((bool)Field(viewer,"imageFullscreen")&&!((ToolStripButton)Field(viewer,"sidebarButton")).Checked,"fullscreen and toggle survive round trip");
            Wheel(canvas,120);await Task.Delay(70);Check(image()=="clip1.png","clamp at first file");
            viewer.GetType().GetMethod("ExitImageView",Fields).Invoke(viewer,null);
            var open=viewer.GetType().GetMethod("Open",Fields);
            viewer.BeginInvoke(new Action(()=>open.Invoke(viewer,new object[]{Path.Combine(folder,"clip3.mov")})));
            await Wait(()=>NameOfVideo(Video())=="clip3.mov"&&(bool)Field(Video(),"ready"),"open video directly");
            movie=Video();surface=(Control)Field(movie,"host");
            ((ToolStripButton)Field(movie,"modeButton")).PerformClick();
            var screen=movie.Bounds;
            Wheel(surface,-120);
            await Wait(()=>Video()==null&&image()=="clip4.png","direct video to neighboring image");
            Check(viewer.Bounds==screen&&(bool)Field(viewer,"imageFullscreen"),"retain fullscreen display");
            Console.WriteLine("PASS mixed fullscreen wheel, video thumbnails, middle drag across views, playback, order and toggle");
        }finally{
            if((Control.MouseButtons&MouseButtons.Middle)!=0)mouse_event(0x40,0,0,0,UIntPtr.Zero);
            Cursor.Position=savedCursor;
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
                try{await Run(Path.GetFullPath(args[0]));result=0;}catch(Exception e){Console.WriteLine(e);}
                finally{Application.ExitThread();}
            }));
            Application.Run();
        }
        return result;
    }
}
