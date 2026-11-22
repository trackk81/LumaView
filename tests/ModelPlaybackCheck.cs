using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

class ModelPlaybackCheck {
    static void Wait(Func<bool> condition,string label) {
        var timer=Stopwatch.StartNew();
        while(!condition()){
            if(timer.ElapsedMilliseconds>20000)throw new Exception("Timed out waiting for "+label);
            Application.DoEvents();Thread.Sleep(5);
        }
    }
    static T Finish<T>(Task<T> task){Wait(()=>task.IsCompleted,"WebView2");return task.GetAwaiter().GetResult();}
    static string Script(WebView2 web,string script){return Finish(web.CoreWebView2.ExecuteScriptAsync(script));}
    static void Expect(WebView2 web,string expression){
        if(Script(web,expression)!="true")throw new Exception("Failed: "+expression);
    }
    static Bitmap Snapshot(WebView2 web){
        using(var stream=new MemoryStream()){
            var task=web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
            Wait(()=>task.IsCompleted,"snapshot");task.GetAwaiter().GetResult();stream.Position=0;
            using(var bmp=new Bitmap(stream))return new Bitmap(bmp);
        }
    }
    static void CheckPose(WebView2 web){
        using(var first=Snapshot(web)){
            Script(web,"seekTo(+range.max*.85)");
            using(var next=Snapshot(web)){
                int changed=0;
                for(int y=first.Height/4;y<first.Height*3/4;y+=3)
                    for(int x=first.Width/4;x<first.Width*3/4;x+=3)
                        if(first.GetPixel(x,y)!=next.GetPixel(x,y))changed++;
                Console.WriteLine("Pose changed at "+changed+" sampled viewport pixels");
                if(changed<50)throw new Exception("Seeking did not change the rendered pose");
            }
        }
    }
    [STAThread]static int Main(string[] args){
        try{
            Application.EnableVisualStyles();
            string model=Path.GetFullPath(args.Length>0?args[0]:@"tests\fixtures\motion.gltf");
            var assembly=Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"DollView.exe"));
            var type=assembly.GetType("LumaView.ModelViewer");
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            using(var form=(Form)Activator.CreateInstance(type,new object[]{model,new Rectangle(0,0,820,620)})){
                form.Show();Wait(()=>(bool)type.GetField("ready",flags).GetValue(form),"model");
                var web=(WebView2)type.GetField("web",flags).GetValue(form);
                Script(web,"window.bar=document.querySelector('#playback');window.range=bar.querySelector('[type=range]');window.play=bar.querySelector('button');window.clips=bar.querySelector('select');window.loopBox=bar.querySelector('[type=checkbox]');window.seekTo=t=>{range.value=t;range.dispatchEvent(new Event('input'));}");
                if(args.Length>1&&args[1]=="--static"){Expect(web,"bar.hidden");return 0;}
                if(args.Length>1&&args[1]=="--single"){
                    Expect(web,"!bar.hidden && clips.hidden && clips.options.length===1");
                    CheckPose(web);return 0;
                }
                Expect(web,"!bar.hidden && !clips.hidden && clips.options.length===2 && +range.value===0");
                CheckPose(web);
                Script(web,"seekTo(0);play.click()");
                Wait(()=>Script(web,"+range.value>0.1")=="true","advancing playback");
                Script(web,"play.click();window.pausedAt=range.value");
                var timer=Stopwatch.StartNew();Wait(()=>timer.ElapsedMilliseconds>200,"paused interval");
                Expect(web,"range.value===pausedAt");
                Script(web,"if(loopBox.checked)loopBox.click();seekTo(+range.max-.12);play.click()");
                Wait(()=>Script(web,"+range.value===+range.max")=="true","end of clip");
                Script(web,"play.click()");Wait(()=>Script(web,"+range.value>0 && +range.value<+range.max-.1")=="true","replay");
                Script(web,"loopBox.click();seekTo(+range.max-.12)");
                Wait(()=>Script(web,"+range.value>0 && +range.value<+range.max-.15")=="true","loop wrap");
                Script(web,"window.firstEnd=range.max;clips.selectedIndex=1;clips.dispatchEvent(new Event('change'))");
                Expect(web,"+range.max!==+firstEnd && +range.value<.5");
                Wait(()=>Script(web,"+range.value>.15")=="true","playback after switching clips");
                Script(web,"play.click()");
                form.Size=new Size(440,340);
                Expect(web,"[play,range,clips,loopBox].every(el=>{const r=el.getBoundingClientRect();return r.width>0&&r.left>=0&&r.right<=innerWidth&&r.top>=0&&r.bottom<=innerHeight;})");
                Script(web,"window.selectedTime=range.value");
                var fullscreen=type.GetMethod("ToggleFullscreen",flags);
                fullscreen.Invoke(form,null);
                Wait(()=>Script(web,"(innerHeight-bar.getBoundingClientRect().bottom)*devicePixelRatio>=56")=="true","fullscreen footer clearance");
                Expect(web,"range.value===selectedTime && clips.selectedIndex===1");
                fullscreen.Invoke(form,null);
                Expect(web,"range.value===selectedTime && clips.selectedIndex===1");
                form.Close();
            }
            Console.WriteLine("PASS playback, pause, pose seeking, replay, loop, clip switching and compact window");return 0;
        }catch(Exception ex){Console.WriteLine(ex);return 1;}
    }
}
