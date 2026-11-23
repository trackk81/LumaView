using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

// Run next to DollView.exe with a test media folder and a settings directory.
// In file-name order, supply a PNG, fixtures/three-color-pages.pdf, an MP4,
// three PNGs ending in 001/002/003, and a final PNG.
class PdfNavigationCheck {
    const BindingFlags Members=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h,int message,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr GetLastActivePopup(IntPtr owner);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    static object Field(object o,string n){return o.GetType().GetField(n,Members).GetValue(o);}
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static async Task Wait(Func<bool> done,string message){
        var time=Stopwatch.StartNew();
        while(!done()){if(time.ElapsedMilliseconds>15000)throw new Exception(message);await Task.Delay(15);}
    }
    static void Wheel(Control c,int steps){
        var p=c.PointToScreen(new Point(120,120));
        PostMessage(c.Handle,0x020A,new IntPtr((-steps*120)<<16),new IntPtr((p.Y<<16)|(p.X&65535)));
    }
    static Form Video(){return Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.GetType().Name=="VideoViewer");}
    static async Task CheckPrintDialog(Form owner,Action click){
        bool shown=false;
        using(var timer=new Timer{Interval=60}){
            timer.Tick+=delegate{
                IntPtr dialog=GetLastActivePopup(owner.Handle);
                if(dialog==owner.Handle||!IsWindowVisible(dialog))return;
                shown=true;timer.Stop();PostMessage(dialog,0x0010,IntPtr.Zero,IntPtr.Zero);
            };
            timer.Start();owner.BeginInvoke(click);
            await Wait(()=>shown&&!(bool)Field(owner,"printing"),"open and cancel native print dialog");
        }
    }
    static void PrintCheck(Assembly app,string[] files,int count,int current,PrintRange range,int first,int last){
        var type=app.GetType("LumaView.MediaPrintDocument");
        using(var doc=(PrintDocument)Activator.CreateInstance(type,Members,null,new object[]{files,count,current},null)){
            var printer=doc.PrinterSettings;
            printer.PrinterName="Microsoft Print to PDF";Check(printer.IsValid,"missing virtual test printer");
            printer.PrintRange=range;printer.FromPage=first;printer.ToPage=last;
            var preview=new PreviewPrintController();doc.PrintController=preview;doc.Print();
            var pages=preview.GetPreviewPageInfo();
            try{
                Check(pages.Length==(range==PrintRange.CurrentPage?1:last-first+1),"printed page range");
                if(range==PrintRange.CurrentPage){
                    using(var b=new Bitmap(pages[0].Image,300,400)){
                        Color color=b.GetPixel(b.Width/2,b.Height/2);
                        Check(color.G>color.R+100&&color.G>color.B+100,"printed the current green page");
                    }
                }
            }finally{foreach(var page in pages)page.Image.Dispose();}
        }
    }
    static async Task Run(string folder,string output){
        var app=Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"DollView.exe"));
        var files=Directory.GetFiles(folder).OrderBy(f=>f).ToArray();
        var ui=app.GetType("LumaView.Ui");
        ui.GetField("SettingsPath").SetValue(null,Path.Combine(output,"settings.ini"));ui.GetMethod("Load").Invoke(null,null);
        using(var viewer=(Form)Activator.CreateInstance(app.GetType("LumaView.Viewer"),new object[]{files[1]})){
            viewer.Show();
            var album=Field(viewer,"album");var canvas=(Control)Field(viewer,"canvas");var side=Field(canvas,"Sidebar");
            Func<string,int,bool> at=(path,page)=>!(bool)Field(viewer,"busy")&&(string)album.GetType().GetProperty("Current").GetValue(album,null)==path&&(int)Field(album,"PageIndex")==page;
            await Wait(()=>at(files[1],0),"open PDF directly");
            Check((int)Field(album,"PageCount")==3&&(bool)Field(side,"visible"),"expand PDF on entry");
            Check(((string[])Field(side,"files")).Length==7,"three PDF pages with adjacent sequence collapsed");
            Check(((ToolStripButton)Field(viewer,"viewerPrint")).Enabled,"viewer print available");
            await CheckPrintDialog(viewer,((ToolStripButton)Field(viewer,"viewerPrint")).PerformClick);
            Wheel(canvas,2);await Wait(()=>at(files[1],2),"forward to third page");
            var image=(Bitmap)Field(album,"Image");Check(image.GetPixel(image.Width/2,image.Height/2).B>220,"render third page");
            Wheel(canvas,1);await Wait(()=>Video()!=null&&(bool)Field(Video(),"ready"),"PDF to video");
            Wheel((Control)Field(Video(),"host"),-1);await Wait(()=>Video()==null&&at(files[1],2),"video back to last PDF page");
            Wheel(canvas,-3);await Wait(()=>at(files[0],0),"past first PDF page to photo");
            Wheel(canvas,2);await Wait(()=>at(files[1],1),"multiple wheel steps enter PDF");
            ((ToolStripButton)Field(viewer,"imageModeButton")).PerformClick();
            Check(((ToolStripButton)Field(viewer,"viewerPrint")).Available,"fullscreen print control");
            viewer.GetType().GetMethod("ExitImageView",Members).Invoke(viewer,null);
            var list=(ListView)Field(viewer,"fileList");
            await Wait(()=>list.Items.Count==5&&list.SelectedItems.Count==1,"gallery collapsed PDF and sequence");
            var galleryPrint=(Button)Field(viewer,"galleryPrint");Check(galleryPrint.Enabled,"gallery PDF print enabled");
            await CheckPrintDialog(viewer,galleryPrint.PerformClick);
            list.SelectedItems.Clear();list.Items[2].Selected=true;
            Check(!galleryPrint.Enabled,"video is not printable");
            list.SelectedItems.Clear();list.Items[3].Selected=true;
            viewer.GetType().GetMethod("OpenSelectedImage",Members).Invoke(viewer,new object[]{false});
            await Wait(()=>at(files[3],0),"open sequence");
            Check((bool)Field(side,"visible")&&((string[])Field(side,"files")).Length==7,"expand frames on entry");
            Wheel(canvas,3);await Wait(()=>at(files[6],0),"past sequence to next image");
        }
        await Task.Run(delegate{
            PrintCheck(app,new[]{files[1]},3,1,PrintRange.CurrentPage,1,3);
            PrintCheck(app,new[]{files[1]},3,0,PrintRange.SomePages,2,3);
            PrintCheck(app,files.Skip(3).Take(3).ToArray(),3,-1,PrintRange.AllPages,1,3);
            PrintCheck(app,new[]{files[0]},1,0,PrintRange.AllPages,1,1);
        });
    }
    [STAThread] static int Main(string[] args){
        Application.EnableVisualStyles();int result=1;
        using(var queue=new Control()){
            var handle=queue.Handle;
            queue.BeginInvoke(new Action(async delegate{
                try{Directory.CreateDirectory(args[1]);await Run(Path.GetFullPath(args[0]),Path.GetFullPath(args[1]));Console.WriteLine("PASS PDF and sequence navigation, print controls, and print pages");result=0;}
                catch(Exception ex){Console.WriteLine(ex);}finally{Application.ExitThread();}
            }));
            Application.Run();
        }
        return result;
    }
}
