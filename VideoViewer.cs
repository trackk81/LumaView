using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Threading.Tasks;



using Color=System.Drawing.Color;

namespace LumaView {
static class MediaFiles {
    static readonly string[] videos={".mp4",".m4v",".mov",".wmv",".avi",".mpg",".mpeg",".mkv",".webm"};
    public static bool IsVideo(string path){return videos.Contains(Path.GetExtension(path).ToLowerInvariant());}
    public static bool Supported(string path){return Album.Supported(path)||IsVideo(path)||ModelFiles.Supported(path);}
    [StructLayout(LayoutKind.Sequential)] struct ThumbnailSize { public int Width,Height; }
    [ComImport,Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IImageFactory { [PreserveSig] int GetImage(ThumbnailSize size,uint flags,out IntPtr bitmap); }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=true)]
    static extern int SHCreateItemFromParsingName(string path,IntPtr context,ref Guid iid,[MarshalAs(UnmanagedType.Interface)] out IImageFactory factory);
    [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr reserved,uint mode);
    [DllImport("ole32.dll")] static extern void CoUninitialize();
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr handle);
    [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid Format;public uint Id; }
    [StructLayout(LayoutKind.Explicit,Size=24)] struct PropertyValue { [FieldOffset(0)] public ushort Type;[FieldOffset(8)] public uint UInt; }
    [ComImport,Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index,out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key,out PropertyValue value);
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern int SHGetPropertyStoreFromParsingName(string path,IntPtr context,uint flags,ref Guid iid,out IPropertyStore store);
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropertyValue value);
    public static double FrameRate(string path){
        IPropertyStore store=null;PropertyValue value=new PropertyValue();int initialized=CoInitializeEx(IntPtr.Zero,0);
        try{
            Guid iid=typeof(IPropertyStore).GUID;
            if(SHGetPropertyStoreFromParsingName(path,IntPtr.Zero,0,ref iid,out store)<0)return 0;
            var key=new PropertyKey{Format=new Guid("64440491-4c8b-11d1-8b70-080036b11a03"),Id=6};
            if(store.GetValue(ref key,out value)<0||value.Type!=19)return 0;
            double fps=value.UInt/1000.0;return fps>0&&fps<=1000?fps:0;
        }catch(COMException){return 0;}
        finally{PropVariantClear(ref value);if(store!=null)Marshal.FinalReleaseComObject(store);if(initialized>=0)CoUninitialize();}
    }
    // Called by the gallery/filmstrip background loaders, never the UI thread.
    static Bitmap ReadVideoThumbnail(string path){
        int initialized=CoInitializeEx(IntPtr.Zero,0);IImageFactory factory=null;IntPtr handle=IntPtr.Zero;
        try {
            Guid iid=typeof(IImageFactory).GUID;
            if(SHCreateItemFromParsingName(Path.GetFullPath(path),IntPtr.Zero,ref iid,out factory)<0)return null;
            if(factory.GetImage(new ThumbnailSize{Width=360,Height=240},0x9,out handle)<0||handle==IntPtr.Zero)return null;
            using(var image=System.Drawing.Image.FromHbitmap(handle))return new Bitmap(image);
        }catch(COMException){return null;}catch(ArgumentException){return null;}
        finally {
            if(handle!=IntPtr.Zero)DeleteObject(handle);
            if(factory!=null)Marshal.FinalReleaseComObject(factory);
            if(initialized>=0)CoUninitialize();
        }
    }
    public static Bitmap VideoTile(string path){
        var bitmap=new Bitmap(180,120);using(var g=Graphics.FromImage(bitmap)){
            g.Clear(Color.FromArgb(30,36,45));
            using(var preview=ReadVideoThumbnail(path)){
                if(preview!=null){
                    float scale=Math.Min(180f/preview.Width,120f/preview.Height);
                    float w=preview.Width*scale,h=preview.Height*scale;
                    g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(preview,(180-w)/2,(120-h)/2,w,h);
                }
            }
            g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using(var shade=new SolidBrush(Color.FromArgb(175,0,0,0)))g.FillEllipse(shade,67,37,46,46);
            using(var brush=new SolidBrush(Color.White))g.FillPolygon(brush,new[]{new Point(84,47),new Point(84,73),new Point(103,60)});
            using(var font=new Font("Segoe UI",8,FontStyle.Bold))using(var format=new StringFormat{Alignment=StringAlignment.Far}){
                string label=Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
                float width=g.MeasureString(label,font).Width+8;
                using(var shade=new SolidBrush(Color.FromArgb(190,0,0,0)))g.FillRectangle(shade,180-width,102,width,18);
                g.DrawString(label,font,System.Drawing.Brushes.White,new RectangleF(0,103,177,17),format);
            }
        }return bitmap;
    }
}
partial class Viewer {
    void OpenVideo(string path){
        StopSlide();enterAfterLoad=false;startSlideAfterLoad=false;
        if(full&&!startupOpening)ExitImageView();
        ShowFileFolder(path);
        var area=Screen.FromControl(this).WorkingArea;
        var size=new Size(Math.Min(1200,area.Width),Math.Min(820,area.Height));
        var bounds=new Rectangle(area.X+(area.Width-size.Width)/2,area.Y+(area.Height-size.Height)/2,size.Width,size.Height);
        playbackActive=true;try{using(var player=new VideoViewer(path,bounds))ShowMediaViewer(player);}finally{playbackActive=false;if(startupOpening&&!IsDisposed){startupOpening=false;ExitImageView();}}
        if(IsDisposed)return;
        ShowFileFolder(path);
        fileList.Focus();UpdateBrowserInfo();
    }
}
sealed class VideoViewer : MediaViewerForm {
    readonly VideoSurface host=new VideoSurface();
    readonly ToolStrip tools=new ViewerToolStrip();
    readonly Label title=new Label(),details=new Label(),timing=new Label();
    readonly VideoTimeline seek=new VideoTimeline();
    readonly Timer timer=new Timer{Interval=33};
    readonly Queue<int> frames=new Queue<int>();
    readonly FullscreenChrome chrome;
    readonly MpvPlayer player;
    Task startup;
    ToolStripButton play,detailsButton;
    readonly ToolStripButton modeButton;
    bool fullscreen;
    Rectangle windowBounds;
    FormWindowState windowState;
    bool ready,paused,ended,dragging,closing,navigating,resumeAfterScrub,flipHorizontal,flipVertical,muted;
    double position,duration,frameRate;
    double? pendingSeek,seekTarget;
    int loadVersion,width,height;
    string codec="",hardware="",current;
    string[] files;
    int index;
    public VideoViewer(string path,Rectangle bounds){
        Text=AppUpdater.WindowTitle(Path.GetFileName(path));BackColor=Color.Black;ForeColor=Color.White;Font=new Font("Malgun Gothic",9);KeyPreview=true;ImeMode=ImeMode.Disable;FormBorderStyle=FormBorderStyle.Sizable;MinimumSize=new Size(400,310);StartPosition=FormStartPosition.Manual;Bounds=bounds;
        files=Directory.GetFiles(Path.GetDirectoryName(Path.GetFullPath(path))).Where(MediaFiles.IsVideo).OrderBy(p=>p,new NaturalSort()).ToArray();index=Array.FindIndex(files,p=>String.Equals(p,Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase));
        if(index<0){files=new[]{Path.GetFullPath(path)};index=0;}
        tools.GripStyle=ToolStripGripStyle.Hidden;tools.Padding=new Padding(10,8,10,8);
        Button(Ui.T("목록으로 (Esc)"),delegate{Close();});
        modeButton=Button(ComponentSetup.Say("전체화면","Fullscreen"),ToggleFullscreen);
        modeButton.ToolTipText=Shortcuts.Format("fullscreen");
        Button(Ui.T("◀ 이전 영상"),delegate{Next(-1);});Button(Ui.T("다음 영상 ▶"),delegate{Next(1);});tools.Items.Add(new ToolStripSeparator());
        play=Button(Ui.T("Ⅱ 일시정지"),Toggle);Button(Ui.T("−5초"),delegate{Seek(-5);});Button(Ui.T("+5초"),delegate{Seek(5);});Button(Ui.T("처음으로"),Restart);
        Button(Ui.T("◀ 프레임 (D)"),delegate{StepFrame(-1);});Button(Ui.T("프레임 ▶ (F)"),delegate{StepFrame(1);});
        var flip=new ToolStripDropDownButton(Ui.T("반전"));flip.DropDownItems.Add(Ui.T("좌우 반전"),null,delegate{flipHorizontal=!flipHorizontal;ApplyFlip();});flip.DropDownItems.Add(Ui.T("상하 반전"),null,delegate{flipVertical=!flipVertical;ApplyFlip();});tools.Items.Add(flip);
        var mute=Button(Ui.T("음소거"),delegate{muted=!muted;Send("set_property","mute",muted);});mute.CheckOnClick=true;
        tools.Items.Add(new ToolStripLabel(Ui.T("볼륨")));var volume=new TrackBar{Minimum=0,Maximum=100,Value=50,TickStyle=TickStyle.None,Width=110,Height=26};volume.ValueChanged+=delegate{Send("set_property","volume",volume.Value);};tools.Items.Add(new ToolStripControlHost(volume));
        detailsButton=Button(Ui.T("상세 정보"),delegate{});detailsButton.CheckOnClick=true;detailsButton.ToolTipText=Ui.T("상세 정보")+" ("+Shortcuts.Format("details")+")";
        detailsButton.CheckedChanged+=delegate{details.Visible=detailsButton.Checked;};
        title.Height=28;title.Padding=new Padding(12,4,0,0);details.Height=64;details.Padding=new Padding(12,6,0,0);details.Visible=false;
        var bottom=new Panel{Height=48,BackColor=Color.FromArgb(38,42,48),Padding=new Padding(8)};
        timing.Dock=DockStyle.Right;timing.Width=280;timing.TextAlign=ContentAlignment.MiddleCenter;bottom.Controls.Add(seek);bottom.Controls.Add(timing);
        seek.MouseDown+=delegate{if(!ready)return;dragging=true;resumeAfterScrub=!paused&&!ended;paused=true;Send("set_property","pause",true);UpdatePlay();};
        seek.Scroll+=delegate{SeekAbsolute();};
        seek.MouseUp+=delegate{if(!dragging)return;dragging=false;SeekAbsolute();};
        seek.MouseCaptureChanged+=delegate{if(dragging&&!seek.Capture){dragging=false;SeekAbsolute();}};
        host.MouseDown+=delegate{host.Focus();Toggle();};Controls.Add(host);
        chrome=new FullscreenChrome(this,tools,bottom,delegate(Keys key){var msg=new Message();return ProcessCmdKey(ref msg,key);});chrome.AddTop(details);chrome.AddTop(title);
        DockWindowControls();
        HandleCreated+=delegate{ModernUi.TitleBar(this);};
        player=new MpvPlayer(this);player.Message+=OnPlayerMessage;player.Failed+=ShowFailure;
        timer.Tick+=delegate{UpdateTimeline();DrainNavigation();};
        Shown+=delegate{startup=player.Start(host.Handle);LoadCurrent();timer.Start();};
        FormClosed+=delegate{closing=true;loadVersion++;timer.Stop();timer.Dispose();chrome.Dispose();player.Dispose();};
    }
    void DockWindowControls(){
        chrome.DockToolbarInOwner();
        Controls.Add(details);
        details.Dock=DockStyle.Top;
        Controls.SetChildIndex(details,Controls.GetChildIndex(tools));
        host.BringToFront();
    }
    void ToggleFullscreen(){
        if(closing)return;
        fullscreen=!fullscreen;
        if(fullscreen){
            var screen=Screen.FromControl(this).Bounds;
            windowState=WindowState;
            windowBounds=windowState==FormWindowState.Normal?Bounds:RestoreBounds;
            WindowState=FormWindowState.Normal;
            FormBorderStyle=FormBorderStyle.None;
            Bounds=screen;
            chrome.AddTop(details);
            chrome.Enable();
        }else{
            DockWindowControls();
            FormBorderStyle=FormBorderStyle.Sizable;
            Bounds=windowBounds;
            WindowState=windowState;
        }
        modeButton.Text=fullscreen?ComponentSetup.Say("창 모드","Windowed mode"):ComponentSetup.Say("전체화면","Fullscreen");
        host.Focus();
    }
    ToolStripButton Button(string label,Action action){var b=new ToolStripButton(label){DisplayStyle=ToolStripItemDisplayStyle.Text,Padding=new Padding(4,3,4,3)};b.Click+=delegate{action();};tools.Items.Add(b);return b;}
    async void Send(params object[] command){try{if(startup!=null)await startup;if(!closing)await player.Command(command);}catch(Exception ex){if(!closing)ShowFailure(ex.Message);}}
    async void LoadCurrent(){
        int version=++loadVersion;player.CancelNavigation();pendingSeek=seekTarget=null;frames.Clear();dragging=resumeAfterScrub=false;
        current=files[index];ready=false;paused=false;ended=false;duration=position=frameRate=0;width=height=0;codec=hardware="";flipHorizontal=flipVertical=false;
        seek.Value=0;seek.Enabled=false;timing.Text=Ui.T("불러오는 중…");title.Text=Path.GetFileName(current);Text=AppUpdater.WindowTitle(title.Text);details.Text="";UpdatePlay();
        try{await startup;if(closing||version!=loadVersion)return;await player.Command("set_property","vf","");if(closing||version!=loadVersion)return;await player.Command("loadfile",current,"replace");}
        catch(Exception ex){if(!closing&&version==loadVersion)ShowFailure(ex.Message);}
    }
    async Task<object> Property(string name){try{return await player.Command("get_property",name);}catch(InvalidOperationException){return null;}}
    async void FileLoaded(){
        int version=loadVersion;
        try{
            string path=Convert.ToString(await Property("path"));if(closing||version!=loadVersion||!String.Equals(path,current,StringComparison.OrdinalIgnoreCase))return;
            double nextDuration=Number(await Property("duration")),nextRate=Number(await Property("estimated-vf-fps")),nextPosition=Number(await Property("time-pos"));string nextCodec=Convert.ToString(await Property("video-codec")),nextHardware=Convert.ToString(await Property("hwdec-current"));
            var video=await Property("video-params") as Dictionary<string,object>;int nextWidth=video==null?0:Convert.ToInt32(video["w"]),nextHeight=video==null?0:Convert.ToInt32(video["h"]);
            if(closing||version!=loadVersion)return;duration=nextDuration;frameRate=nextRate;position=nextPosition;codec=nextCodec;hardware=nextHardware;width=nextWidth;height=nextHeight;ready=true;seek.Enabled=duration>0;paused=false;await player.Command("set_property","pause",false);
            if(closing||version!=loadVersion)return;title.Text=Path.GetFileName(current)+"  ·  "+width+" × "+height;UpdateDetails();UpdatePlay();host.Focus();
        }catch(Exception ex){if(!closing&&version==loadVersion)ShowFailure(ex.Message);}
    }
    void OnPlayerMessage(Dictionary<string,object> data){
        object value;string kind=data.TryGetValue("event",out value)?Convert.ToString(value):"";
        if(kind=="file-loaded"){FileLoaded();return;}
        if(kind=="end-file"&&Convert.ToString(data["reason"])=="error"){ShowFailure("파일을 읽을 수 없습니다. "+player.LastError);return;}
        if(kind!="property-change"||!data.TryGetValue("data",out value)||value==null)return;
        string name=Convert.ToString(data["name"]);
        if(name=="time-pos")position=Number(value);
        if(name=="duration")duration=Number(value);
        if(name=="pause"&&!dragging&&!navigating){paused=Convert.ToBoolean(value);UpdatePlay();}
        if(name=="eof-reached"){ended=Convert.ToBoolean(value);if(ended){paused=true;UpdatePlay();}}
        if(name=="video-codec")codec=Convert.ToString(value);
        if(name=="hwdec-current"){hardware=Convert.ToString(value);if(ready)UpdateDetails();}
    }
    static double Number(object value){return value==null?0:Convert.ToDouble(value,System.Globalization.CultureInfo.InvariantCulture);}
    void ShowFailure(string error){if(closing)return;ready=false;paused=true;seek.Enabled=false;pendingSeek=seekTarget=null;frames.Clear();resumeAfterScrub=false;player.CancelNavigation();play.Text=Ui.T("재생 불가");details.Visible=true;details.Text=error;title.Text=Ui.T("재생할 수 없는 영상: ")+Path.GetFileName(current);}
    void UpdatePlay(){play.Text=ended?Ui.T("▶ 다시 재생"):paused?Ui.T("▶ 재생"):Ui.T("Ⅱ 일시정지");}
    void UpdateTimeline(){if(!ready)return;double shown=pendingSeek??seekTarget??position;if(!dragging&&duration>0)seek.Value=Math.Max(0,Math.Min(10000,(int)(shown/duration*10000)));timing.Text=TimeText(dragging?duration*seek.Value/10000.0:shown)+" / "+TimeText(duration);}
    static string TimeText(double seconds){var t=TimeSpan.FromSeconds(Math.Max(0,seconds));return ((int)t.TotalHours).ToString("00")+":"+t.Minutes.ToString("00")+":"+t.Seconds.ToString("00")+"."+t.Milliseconds.ToString("000");}
    void UpdateDetails(){try{var file=new FileInfo(current);details.Text=String.Format(Ui.T("파일: {0}  [{1}/{2}]\n크기: {3:N2} MB  ·  수정: {4:yyyy-MM-dd HH:mm:ss}  ·  영상: {5} × {6}  ·  길이: {7}"),file.Name,index+1,files.Length,file.Length/1048576.0,file.LastWriteTime,width,height,TimeText(duration))+"\nmpv / FFmpeg · "+codec+" · "+frameRate.ToString("0.###")+" fps · "+(String.IsNullOrEmpty(hardware)||hardware=="no"?"CPU":hardware);}catch(IOException){}}
    void Toggle(){if(!ready)return;if(ended){Restart();return;}resumeAfterScrub=false;paused=!paused;Send("set_property","pause",paused);UpdatePlay();}
    void Restart(){if(!ready)return;paused=true;Send("set_property","pause",true);resumeAfterScrub=true;QueueSeek(0);}
    void Seek(double delta){if(!ready||duration<=0)return;QueueSeek((pendingSeek??seekTarget??position)+delta);}
    void SeekAbsolute(){if(ready&&duration>0)QueueSeek(duration*seek.Value/10000.0);}
    void QueueSeek(double seconds){frames.Clear();pendingSeek=Math.Max(0,Math.Min(duration,seconds));ended=false;UpdateTimeline();}
    async void DrainNavigation(){
        if(navigating||!ready||closing||(!pendingSeek.HasValue&&frames.Count==0))return;
        navigating=true;int version=loadVersion;
        try{
            while(ready&&!closing&&version==loadVersion&&(pendingSeek.HasValue||frames.Count>0)){
                if(pendingSeek.HasValue){double target=pendingSeek.Value;pendingSeek=null;seekTarget=target;await player.Navigate(position,false,"seek",target,"absolute+exact");}
                else{int direction=frames.Dequeue();if((direction<0&&position<.00001)||(direction>0&&ended))continue;await player.Command("set_property","pause",true);await player.Navigate(position,true,direction<0?"frame-back-step":"frame-step");}
                if(closing||version!=loadVersion)return;position=Number(await Property("time-pos"));seekTarget=null;UpdateTimeline();
            }
            if(!closing&&version==loadVersion&&!dragging&&resumeAfterScrub){resumeAfterScrub=false;paused=false;await player.Command("set_property","pause",false);UpdatePlay();}
        }catch(TaskCanceledException){}
        catch(Exception ex){if(!closing&&version==loadVersion){pendingSeek=seekTarget=null;frames.Clear();details.Visible=true;details.Text=ex.Message;}}
        finally{navigating=false;if(version==loadVersion)seekTarget=null;}
    }
    void StepFrame(int direction){if(!ready||duration<=0||width<=0||(direction>0&&ended)||(direction<0&&position<=.000001))return;resumeAfterScrub=false;paused=true;if(direction<0)ended=false;Send("set_property","pause",true);if(frames.Count<120)frames.Enqueue(direction);UpdatePlay();}
    // Retained for existing time-grid calculations; decoder stepping does not use it.
    internal static double FrameTarget(double position,int direction,double fps,double length){double frame=Math.Floor(position*fps+.0001)+direction;return Math.Max(0,Math.Min(Math.Max(0,Math.Ceiling(length*fps)-1),frame))/fps;}
    void ApplyFlip(){string filter=flipHorizontal?(flipVertical?"hflip,vflip":"hflip"):(flipVertical?"vflip":"");Send("set_property","vf",filter);}
    void Next(int delta){int next=Math.Max(0,Math.Min(files.Length-1,index+delta));if(next!=index){index=next;LoadCurrent();}}
    protected override bool ProcessCmdKey(ref Message msg,Keys key){if(Shortcuts.Matches("fullscreen",key)){ToggleFullscreen();return true;}if(Shortcuts.Matches("details",key)){detailsButton.PerformClick();return true;}switch(key){case Keys.Escape:Close();return true;case Keys.Space:Toggle();return true;case Keys.Left:Seek(-5);return true;case Keys.Right:Seek(5);return true;case Keys.D:StepFrame(-1);return true;case Keys.F:StepFrame(1);return true;case Keys.PageDown:Next(1);return true;case Keys.PageUp:Next(-1);return true;case Keys.Home:if(ready)QueueSeek(0);return true;}return base.ProcessCmdKey(ref msg,key);}
}
}
