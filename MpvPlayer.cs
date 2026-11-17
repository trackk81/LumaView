using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace LumaView {
// The decoder runs in its own process; no codec packs or native decoder DLLs are
// loaded into the viewer. Only JSON messages cross the private named pipe.
sealed class MpvPlayer : IDisposable {
    readonly Control owner;
    readonly object gate=new object();
    readonly Dictionary<int,TaskCompletionSource<object>> requests=new Dictionary<int,TaskCompletionSource<object>>();
    readonly SemaphoreSlim writes=new SemaphoreSlim(1,1);
    Process process;
    NamedPipeClientStream pipe;
    StreamWriter writer;
    int nextId;
    volatile bool disposed;
    TaskCompletionSource<bool> movement;
    bool frameMovement;
    double movementStart;
    public event Action<Dictionary<string,object>> Message;
    public event Action<string> Failed;
    public string LastError="";
    public MpvPlayer(Control control){owner=control;}
    public async Task Start(IntPtr window){
        string executable=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"video-runtime","mpv.exe");
        if(!File.Exists(executable))throw new FileNotFoundException("video-runtime 폴더에 영상 재생 엔진이 없습니다.",executable);
        string name="LumaView-"+Guid.NewGuid().ToString("N");
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(executable),
            Arguments="--no-config --load-scripts=no --terminal=no --idle=yes --force-window=yes --keep-open=yes --pause=yes --osc=no --osd-level=0 --input-default-bindings=no --input-vo-keyboard=no --input-cursor-passthrough=yes --audio-display=no --hwdec=auto --vo=gpu-next --gpu-api=d3d11 --hr-seek=yes --hr-seek-framedrop=no --volume=50 --wid="+unchecked((uint)window.ToInt64())+" --input-ipc-server="+name};
        process=new Process{StartInfo=start,EnableRaisingEvents=true};
        process.Exited+=delegate{if(!disposed)Fault("영상 재생 엔진이 종료되었습니다. 영상을 다시 열어주세요.");};
        if(!process.Start())throw new IOException("영상 재생 엔진을 시작하지 못했습니다.");
        pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous);
        await Task.Run(delegate{pipe.Connect(8000);});
        if(disposed)throw new ObjectDisposedException("MpvPlayer");
        writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};
        ReadLoop();
        string[] properties={"time-pos","duration","pause","eof-reached","seeking","video-codec","estimated-vf-fps","video-params","hwdec-current","paused-for-cache"};
        for(int i=0;i<properties.Length;i++)await Command("observe_property",i+1,properties[i]);
        await Command("request_log_messages","warn");
    }
    async void ReadLoop(){
        try{using(var reader=new StreamReader(pipe,new UTF8Encoding(false),false,4096,true)){
            string line;
            var json=new JavaScriptSerializer{MaxJsonLength=4194304};
            while(!disposed&&(line=await reader.ReadLineAsync().ConfigureAwait(false))!=null){
                var data=json.Deserialize<Dictionary<string,object>>(line);object value;
                if(data.TryGetValue("request_id",out value)){
                    TaskCompletionSource<object> completion=null;int id=Convert.ToInt32(value);
                    lock(gate){if(requests.TryGetValue(id,out completion))requests.Remove(id);}
                    if(completion!=null){string error=Convert.ToString(data["error"]);if(error=="success")completion.TrySetResult(data.TryGetValue("data",out value)?value:null);else completion.TrySetException(new InvalidOperationException(error));}
                    continue;
                }
                string kind=data.TryGetValue("event",out value)?Convert.ToString(value):"";
                if(kind=="log-message"&&data.TryGetValue("text",out value))LastError=Convert.ToString(value).Trim();
                lock(gate){
                    if(movement!=null){
                        bool complete=!frameMovement&&kind=="playback-restart";
                        if(frameMovement&&kind=="property-change"&&Convert.ToString(data["name"])=="time-pos"&&data.TryGetValue("data",out value)&&value!=null)complete=Math.Abs(Convert.ToDouble(value)-movementStart)>.000001;
                        if(kind=="property-change"&&Convert.ToString(data["name"])=="eof-reached"&&data.TryGetValue("data",out value)&&value is bool&&(bool)value)complete=true;
                        if(complete)movement.TrySetResult(true);
                    }
                }
                Post(delegate{if(Message!=null)Message(data);});
            }
            if(!disposed)Fault("영상 재생 엔진과의 연결이 끊어졌습니다.");
        }}catch(Exception ex){if(!disposed)Fault(ex.Message);}
    }
    public async Task<object> Command(params object[] command){
        if(disposed||writer==null)throw new ObjectDisposedException("MpvPlayer");
        var completion=new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);int id=Interlocked.Increment(ref nextId);
        lock(gate)requests.Add(id,completion);
        try{
            var data=new Dictionary<string,object>{{"command",command},{"request_id",id}};
            string line=new JavaScriptSerializer().Serialize(data);
            await writes.WaitAsync().ConfigureAwait(false);
            try{if(disposed)throw new ObjectDisposedException("MpvPlayer");await writer.WriteLineAsync(line).ConfigureAwait(false);}finally{writes.Release();}
            if(await Task.WhenAny(completion.Task,Task.Delay(8000)).ConfigureAwait(false)!=completion.Task)throw new TimeoutException("영상 재생 명령의 응답 시간이 초과되었습니다.");
            return await completion.Task.ConfigureAwait(false);
        }finally{lock(gate)requests.Remove(id);}
    }
    public async Task Navigate(double position,bool frame,params object[] command){
        var done=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock(gate){movement=done;frameMovement=frame;movementStart=position;}
        try{
            await Command(command).ConfigureAwait(false);
            if(command.Length>0&&Convert.ToString(command[0])!="frame-back-step"){object eof=await Command("get_property","eof-reached").ConfigureAwait(false);if(eof is bool&&(bool)eof)done.TrySetResult(true);}
            if(await Task.WhenAny(done.Task,Task.Delay(8000)).ConfigureAwait(false)!=done.Task)throw new TimeoutException("영상 탐색이 지연되고 있습니다. 다른 위치로 이동하거나 영상을 다시 열어주세요.");
            await done.Task.ConfigureAwait(false);
        }finally{lock(gate){if(movement==done)movement=null;}}
    }
    public void CancelNavigation(){lock(gate){if(movement!=null){movement.TrySetCanceled();movement=null;}}}
    void Post(Action action){try{if(!disposed&&!owner.IsDisposed&&owner.IsHandleCreated)owner.BeginInvoke((Action)delegate{if(!disposed&&!owner.IsDisposed)action();});}catch(InvalidOperationException){}}
    void Fault(string error){
        lock(gate){foreach(var request in requests.Values)request.TrySetException(new IOException(error));requests.Clear();if(movement!=null)movement.TrySetException(new IOException(error));}
        Post(delegate{if(Failed!=null)Failed(error);});
    }
    public void Dispose(){
        if(disposed)return;disposed=true;CancelNavigation();
        lock(gate){foreach(var request in requests.Values)request.TrySetCanceled();requests.Clear();}
        if(pipe!=null)pipe.Dispose();
        var child=process;
        if(child!=null)Task.Run(delegate{try{if(!child.HasExited)child.Kill();child.WaitForExit(1500);}catch(InvalidOperationException){}finally{child.Dispose();}});
    }
}
sealed class VideoSurface : Control {
    public VideoSurface(){BackColor=System.Drawing.Color.Black;Dock=DockStyle.Fill;TabStop=true;SetStyle(ControlStyles.Selectable,true);}
}
// TrackBar clicks page by a small increment rather than seeking to the pointer.
// This timeline uses absolute pointer positions and retains capture while scrubbing.
sealed class VideoTimeline : Control {
    int position;
    public int Minimum{get{return 0;}}
    public int Maximum{get{return 10000;}}
    public int Value{get{return position;}set{int next=Math.Max(0,Math.Min(10000,value));if(next!=position){position=next;Invalidate();}}}
    public event EventHandler Scroll;
    public VideoTimeline(){Dock=DockStyle.Fill;Enabled=false;TabStop=true;Cursor=Cursors.Hand;SetStyle(ControlStyles.Selectable|ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);}
    void MoveTo(int x){Value=(int)Math.Round(Math.Max(0,Math.Min(1,(x-10.0)/Math.Max(1,Width-20)))*10000);if(Scroll!=null)Scroll(this,EventArgs.Empty);}
    protected override void OnMouseDown(MouseEventArgs e){if(e.Button!=MouseButtons.Left)return;Focus();base.OnMouseDown(e);Capture=true;MoveTo(e.X);}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(Capture&&(e.Button&MouseButtons.Left)!=0)MoveTo(e.X);}
    protected override void OnMouseUp(MouseEventArgs e){if(e.Button!=MouseButtons.Left)return;MoveTo(e.X);base.OnMouseUp(e);Capture=false;}
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float y=Height/2f,left=10,right=Math.Max(10,Width-10),x=left+(right-left)*position/10000f;
        using(var track=new System.Drawing.Pen(System.Drawing.Color.FromArgb(82,91,103),4))g.DrawLine(track,left,y,right,y);
        using(var fill=new System.Drawing.Pen(Enabled?System.Drawing.Color.FromArgb(113,201,255):System.Drawing.Color.Gray,4))g.DrawLine(fill,left,y,x,y);
        using(var thumb=new System.Drawing.SolidBrush(Enabled?System.Drawing.Color.White:System.Drawing.Color.Gray))g.FillEllipse(thumb,x-5,y-5,10,10);
        if(Focused)ControlPaint.DrawFocusRectangle(g,ClientRectangle);
    }
}
}
