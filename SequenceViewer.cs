using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows.Forms;

namespace LumaView {
static class Sequences {
    public static Dictionary<string,string[]> Group(string[] paths){
        var candidates=new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach(string path in paths){
            if(!Album.Supported(path))continue;
            var match=Regex.Match(Path.GetFileNameWithoutExtension(path),@"^(.*?)(\d{3,})$");
            if(!match.Success)continue;
            string key=Path.GetDirectoryName(path)+"|"+match.Groups[1].Value+"|"+match.Groups[2].Length+"|"+Path.GetExtension(path);
            List<string> list;if(!candidates.TryGetValue(key,out list))candidates[key]=list=new List<string>();list.Add(path);
        }
        var result=new Dictionary<string,string[]>(StringComparer.OrdinalIgnoreCase);
        foreach(var list in candidates.Values)if(list.Count>=2){var ordered=list.OrderBy(p=>p,new NaturalSort()).ToArray();result[ordered[0]]=ordered;}
        return result;
    }
    public static void Badge(Bitmap bitmap){using(var g=Graphics.FromImage(bitmap)){
        float x=bitmap.Width/2f,y=bitmap.Height/2f;g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var shade=new SolidBrush(Color.FromArgb(200,12,20,26)))g.FillEllipse(shade,x-23,y-23,46,46);
        using(var font=new Font("Segoe UI",13,FontStyle.Bold))using(var brush=new SolidBrush(Color.FromArgb(151,232,216)))using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})g.DrawString("SQ",font,brush,new RectangleF(x-23,y-23,46,46),format);
    }}
}
sealed class SequenceViewer : Form {
    readonly string[] frames;
    readonly Canvas view=new Canvas();
    readonly ToolStrip tools=new ToolStrip{GripStyle=ToolStripGripStyle.Hidden,Padding=new Padding(10,8,10,8)};
    readonly TrackBar seek=new TrackBar{Dock=DockStyle.Fill,Minimum=0,TickStyle=TickStyle.None};
    readonly Label status=new Label{Dock=DockStyle.Right,Width=470,TextAlign=ContentAlignment.MiddleCenter,ForeColor=Color.White};
    readonly Timer timer=new Timer{Interval=1};
    readonly Label frameNumber=new Label{AutoSize=true,Visible=false,BackColor=Color.FromArgb(32,35,40),ForeColor=Color.White,Padding=new Padding(10,6,10,6)};
    readonly HashSet<int> decoding=new HashSet<int>();
    int direction=1;
    readonly Stopwatch clock=Stopwatch.StartNew();
    readonly Dictionary<int,Bitmap> cache=new Dictionary<int,Bitmap>();
    FullscreenChrome chrome;
    ToolStripButton play;
    int requested,displayed=-1,anchorFrame;
    double fps=24,anchorTime;
    bool waitingForStart=true,endedPlayback;
    readonly NumericUpDown rate=new NumericUpDown{Minimum=1,Maximum=120,DecimalPlaces=3,Increment=1,Value=24,Width=88};
    bool playing=true,closed,dragging;
    long bytes;
    const long Budget=256L*1024*1024;
    public SequenceViewer(string[] files,Rectangle bounds,int startIndex=0){
        frames=files;Text=Ui.T("LumaView 시퀀스");Font=new Font("Malgun Gothic",9);BackColor=Color.Black;FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;Bounds=bounds;KeyPreview=true;
        Add(Ui.T("목록으로 (Esc)"),Close);play=Add(Ui.T("Ⅱ 일시정지"),Toggle);
        Add(Ui.T("−5초"),delegate{Jump(-(int)Math.Round(5*fps));});Add(Ui.T("+5초"),delegate{Jump((int)Math.Round(5*fps));});Add(Ui.T("처음으로"),delegate{Request(0);});
        Add(Ui.T("◀ 프레임 (D)"),delegate{Step(-1);});Add(Ui.T("프레임 ▶ (F)"),delegate{Step(1);});
        tools.Items.Add(new ToolStripLabel(Ui.T("재생 FPS")));rate.AccessibleName=Ui.T("재생 FPS");rate.ValueChanged+=delegate{ChangeRate((double)rate.Value);};tools.Items.Add(new ToolStripControlHost(rate));
        tools.Items.Add(new ToolStripLabel(Ui.T("실시간 재생")){ToolTipText=Ui.T("읽기가 늦으면 프레임을 건너뛰어 재생 시간을 유지합니다.")});
        var flip=new ToolStripDropDownButton(Ui.T("반전"));flip.DropDownItems.Add(Ui.T("좌우 반전"),null,delegate{view.FlipHorizontal=!view.FlipHorizontal;view.Invalidate();});flip.DropDownItems.Add(Ui.T("상하 반전"),null,delegate{view.FlipVertical=!view.FlipVertical;view.Invalidate();});tools.Items.Add(flip);
        Add(Ui.T("화면 맞춤"),delegate{view.Fit=true;view.Reset();});Add(Ui.T("상세 정보"),delegate{view.ShowDetails=!view.ShowDetails;view.Invalidate();});
        var frameToggle=Add(Ui.T("프레임 번호"),delegate{});frameToggle.CheckOnClick=true;frameToggle.CheckedChanged+=delegate{frameNumber.Visible=frameToggle.Checked;};
        frameNumber.Font=new Font("Consolas",14,FontStyle.Bold);view.Controls.Add(frameNumber);
        tools.SizeChanged+=delegate{frameNumber.Location=new Point(16,32+tools.Height+12);};frameNumber.Location=new Point(16,32+tools.Height+12);
        var bottom=new Panel{Height=56,Padding=new Padding(8),BackColor=Color.FromArgb(28,31,37)};bottom.Controls.Add(seek);bottom.Controls.Add(status);seek.Maximum=Math.Max(1,frames.Length-1);
        seek.MouseDown+=delegate{dragging=true;};seek.MouseUp+=delegate{dragging=false;playing=false;UpdatePlay();Request(seek.Value);};seek.Scroll+=delegate{playing=false;UpdatePlay();Request(seek.Value);};
        Controls.Add(view);chrome=new FullscreenChrome(this,tools,bottom,delegate(Keys key){var message=new Message();return ProcessCmdKey(ref message,key);});
        timer.Tick+=delegate{TickPlayback();};
        Shown+=delegate{Bounds=bounds;chrome.Enable();Request(startIndex);timer.Start();};
        FormClosed+=delegate{closed=true;timer.Dispose();chrome.Dispose();view.Image=null;foreach(var bitmap in cache.Values)bitmap.Dispose();cache.Clear();};
    }
    ToolStripButton Add(string label,Action action){var b=new ToolStripButton(label){Padding=new Padding(4,3,4,3)};b.Click+=delegate{action();};tools.Items.Add(b);return b;}
    void UpdatePlay(){play.Text=playing?Ui.T("Ⅱ 일시정지"):Ui.T("▶ 재생");}
    double Now {get{return clock.Elapsed.TotalSeconds;}}
    void ResetClock(bool wait){endedPlayback=false;anchorFrame=requested;anchorTime=Now;waitingForStart=playing&&wait;}
    void ChangeRate(double value){
        TickPlayback();fps=value;ResetClock(playing&&waitingForStart);UpdateStatus();
    }
    void Toggle(){
        if(playing){TickPlayback();playing=false;waitingForStart=false;Request(requested);}
        else{playing=true;direction=1;Request(requested==frames.Length-1?0:requested);}
        UpdatePlay();
    }
    void Step(int delta){playing=false;waitingForStart=false;UpdatePlay();Request(requested+delta);}
    void Jump(int delta){Request(requested+delta);}
    void TickPlayback(){
        if(closed||!playing||dragging||waitingForStart)return;
        // The playhead follows wall time, never the completion time of a decode.
        double elapsed=Math.Max(0,Now-anchorTime);
        int target=anchorFrame+(int)Math.Floor(elapsed*fps+1e-7);
        bool ended=target>=frames.Length;
        if(ended){endedPlayback=true;playing=false;UpdatePlay();}
        target=Math.Min(frames.Length-1,target);
        if(target!=requested)RequestCore(target,false);
        else if(ended){Pump();UpdateStatus();}
    }
    void Request(int index){RequestCore(index,true);}
    void RequestCore(int index,bool resetClock){
        if(index!=requested)direction=index>requested?1:-1;
        requested=Math.Max(0,Math.Min(frames.Length-1,index));
        if(resetClock)ResetClock(playing&&!cache.ContainsKey(requested));
        Bitmap bitmap;
        if(cache.TryGetValue(requested,out bitmap))Display(requested,bitmap);
        else if(playing&&!waitingForStart){
            // Use the newest available frame rather than freezing while chasing a moving target.
            int ready=cache.Keys.Where(i=>i<=requested&&i>displayed).DefaultIfEmpty(-1).Max();
            if(ready>=0)Display(ready,cache[ready]);
        }
        if(failed.Contains(requested)&&waitingForStart){waitingForStart=false;playing=false;UpdatePlay();}
        UpdateStatus();Pump();
    }
    void UpdateStatus(){
        if(!dragging)seek.Value=requested;
        double position=endedPlayback?frames.Length/fps:playing&&!waitingForStart?Math.Min(frames.Length/fps,anchorFrame/fps+Math.Max(0,Now-anchorTime)):requested/fps;
        status.Text=String.Format("SQ {0}/{1} · {2:0.###} fps · {3:0.00}/{4:0.00}s",requested+1,frames.Length,fps,position,frames.Length/fps);
        if(displayed!=requested)status.Text+=" · "+Ui.T("불러오는 중…");
    }
    void Display(int index,Bitmap bitmap){
        view.Image=bitmap;displayed=index;
        if(waitingForStart&&displayed==requested){waitingForStart=false;anchorFrame=requested;anchorTime=Now;}
        var number=Regex.Match(Path.GetFileNameWithoutExtension(frames[displayed]),@"\d+$");
        frameNumber.Text="Frame "+(number.Success?number.Value:(displayed+1).ToString())+"   ["+(displayed+1)+" / "+frames.Length+"]";
        view.DetailsText=Ui.T("시퀀스: ")+Path.GetFileName(frames[displayed])+Ui.T("\n프레임: ")+(displayed+1)+" / "+frames.Length+"  ·  "+fps+Ui.T(" fps\n미리보기: ")+bitmap.Width+" × "+bitmap.Height;
        view.Invalidate();UpdateStatus();
    }
    List<int> Wanted(){
        var wanted=new List<int>{requested};
        for(int n=1;n<=12;n++){int i=requested+n*direction;if(i>=0&&i<frames.Length)wanted.Add(i);}
        for(int n=1;n<=3;n++){int i=requested-n*direction;if(i>=0&&i<frames.Length)wanted.Add(i);}
        return wanted;
    }
    void Pump(){
        if(closed)return;
        var wanted=Wanted();
        foreach(int key in cache.Keys.OrderByDescending(i=>Math.Abs(i-requested)).ToArray()){
            if(key==displayed||key==requested)continue;
            if(wanted.Contains(key)&&bytes<=Budget&&cache.Count<=24)continue;
            var old=cache[key];bytes-=(long)old.Width*old.Height*4;cache.Remove(key);old.Dispose();
        }
        // Two independent decodes: a slow prefetch cannot block a newly requested frame.
        foreach(int target in wanted){
            if(decoding.Count>=2)break;
            if(cache.ContainsKey(target)||decoding.Contains(target)||failed.Contains(target))continue;
            if(target!=requested&&bytes+decoding.Count*8L*1024*1024>=Budget)break;
            decoding.Add(target);DecodeFrame(target);
        }
    }
    void DecodeFrame(int target){
        Task.Run(delegate{
            if(ExtendedDecoder.Extensions.Contains(Path.GetExtension(frames[target]).ToLowerInvariant()))return ExtendedDecoder.Decode(frames[target],1920,1080);
            var original=Album.Decode(frames[target]);
            if(original.Width<=1920&&original.Height<=1080)return original;
            using(original){
                double scale=Math.Min(1,Math.Min(1920.0/original.Width,1080.0/original.Height));
                var image=new Bitmap(Math.Max(1,(int)(original.Width*scale)),Math.Max(1,(int)(original.Height*scale)));
                try{using(var g=Graphics.FromImage(image)){g.InterpolationMode=InterpolationMode.HighQualityBilinear;g.DrawImage(original,0,0,image.Width,image.Height);}return image;}
                catch{image.Dispose();throw;}
            }
        }).ContinueWith(task=>{
            Bitmap decoded=task.Status==TaskStatus.RanToCompletion?task.Result:null;
            Exception error=task.Exception==null?null:task.Exception.GetBaseException();
            // Commit UI/cache changes only on the viewer thread, including during startup.
            try{BeginInvoke((Action)delegate{CompleteDecode(target,decoded,error);});}
            catch(InvalidOperationException){if(decoded!=null)decoded.Dispose();}
        });
    }
    void CompleteDecode(int target,Bitmap decoded,Exception error){
        try{
            if(closed){if(decoded!=null)decoded.Dispose();return;}
            if(error!=null){
                failed.Add(target);
                if(target==requested&&(!playing||waitingForStart)){playing=false;waitingForStart=false;UpdatePlay();status.Text=Ui.T("프레임 읽기 실패: ")+error.Message;}
                return;
            }
            bool usefulLate=playing&&!waitingForStart&&target<=requested&&target>displayed;
            if(!Wanted().Contains(target)&&!usefulLate){decoded.Dispose();return;}
            cache[target]=decoded;bytes+=(long)decoded.Width*decoded.Height*4;
            if(target==requested||usefulLate)Display(target,decoded);
        }finally{decoding.Remove(target);if(!closed)Pump();}
    }
    readonly HashSet<int> failed=new HashSet<int>();
    protected override bool ProcessCmdKey(ref Message msg,Keys key){if(rate.ContainsFocus&&key!=Keys.Escape&&key!=Keys.F11){if(key==Keys.Enter){view.Focus();return true;}return base.ProcessCmdKey(ref msg,key);}switch(key){case Keys.Escape:case Keys.F11:Close();return true;case Keys.Space:Toggle();return true;case Keys.D:Step(-1);return true;case Keys.F:Step(1);return true;case Keys.Left:Jump(-(int)Math.Round(5*fps));return true;case Keys.Right:Jump((int)Math.Round(5*fps));return true;case Keys.Home:Request(0);return true;case Keys.End:Request(frames.Length-1);return true;}return base.ProcessCmdKey(ref msg,key);}
}
}





