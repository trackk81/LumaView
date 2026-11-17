using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using MediaState=System.Windows.Controls.MediaState;
using System.Windows.Media;
using MediaElement=System.Windows.Controls.MediaElement;
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
        if(full&&!startupOpening)ExitFullscreen();
        string dir=Path.GetDirectoryName(Path.GetFullPath(path));if(!String.Equals(dir,browsingFolder,StringComparison.OrdinalIgnoreCase))Browse(dir);
        foreach(ListViewItem item in fileList.Items){item.Selected=String.Equals((string)item.Tag,path,StringComparison.OrdinalIgnoreCase);if(item.Selected)item.EnsureVisible();}
        playbackActive=true;try{using(var player=new VideoViewer(path,Screen.FromControl(this).Bounds))player.ShowDialog(this);}finally{playbackActive=false;if(startupOpening){startupOpening=false;ExitFullscreen();}}
        fileList.Focus();UpdateBrowserInfo();
    }
}
sealed class VideoViewer : Form {
    readonly MediaElement media=new MediaElement();
    FullscreenChrome chrome;
    bool flipHorizontal,flipVertical;
    readonly ElementHost host=new ElementHost{Dock=DockStyle.Fill};
    readonly ToolStrip tools=new ToolStrip();
    readonly System.Windows.Forms.Label title=new System.Windows.Forms.Label();
    readonly System.Windows.Forms.Label details=new System.Windows.Forms.Label();
    readonly System.Windows.Forms.Label timing=new System.Windows.Forms.Label();
    readonly TrackBar seek=new TrackBar{Minimum=0,Maximum=10000,TickStyle=TickStyle.None,Dock=DockStyle.Fill,Enabled=false};
    readonly Timer timer=new Timer{Interval=250};
    readonly Timer seekTimer=new Timer{Interval=40};
    double? pendingSeek;
    int loadVersion;
    ToolStripButton play;
    bool ready,paused,ended,dragging;
    double duration;
    double frameRate=30;
    bool estimatedFrameRate;
    double? framePosition;
    string[] files;
    int index;
    string current;
    public VideoViewer(string path,Rectangle bounds){
        Text=Ui.T("LumaView 영상");BackColor=Color.Black;ForeColor=Color.White;Font=new Font("Malgun Gothic",9);KeyPreview=true;FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;Bounds=bounds;
        files=Directory.GetFiles(Path.GetDirectoryName(Path.GetFullPath(path))).Where(MediaFiles.IsVideo).OrderBy(p=>p,new NaturalSort()).ToArray();index=Array.FindIndex(files,p=>String.Equals(p,Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase));
        if(index<0){files=new[]{Path.GetFullPath(path)};index=0;}
        tools.GripStyle=ToolStripGripStyle.Hidden;tools.BackColor=Color.FromArgb(38,42,48);tools.ForeColor=Color.White;tools.Padding=new Padding(10,8,10,8);tools.RenderMode=ToolStripRenderMode.System;
        Button(Ui.T("목록으로 (Esc)"),delegate{Close();});Button(Ui.T("◀ 이전 영상"),delegate{Next(-1);});Button(Ui.T("다음 영상 ▶"),delegate{Next(1);});tools.Items.Add(new ToolStripSeparator());
        play=Button(Ui.T("Ⅱ 일시정지"),Toggle);Button(Ui.T("−5초"),delegate{Seek(-5);});Button(Ui.T("+5초"),delegate{Seek(5);});Button(Ui.T("처음으로"),delegate{if(ready){pendingSeek=null;seekTimer.Stop();framePosition=null;media.ScrubbingEnabled=false;media.Position=TimeSpan.Zero;ended=false;media.Play();paused=false;play.Text=Ui.T("Ⅱ 일시정지");}});
        Button(Ui.T("◀ 프레임 (D)"),delegate{StepFrame(-1);});Button(Ui.T("프레임 ▶ (F)"),delegate{StepFrame(1);});
        var flipMenu=new ToolStripDropDownButton(Ui.T("반전")){ToolTipText=Ui.T("화면에서만 반전합니다. 원본은 변경하지 않습니다.")};
        flipMenu.DropDownItems.Add(Ui.T("좌우 반전"),null,delegate{flipHorizontal=!flipHorizontal;ApplyFlip();});
        flipMenu.DropDownItems.Add(Ui.T("상하 반전"),null,delegate{flipVertical=!flipVertical;ApplyFlip();});
        tools.Items.Add(flipMenu);
        var mute=Button(Ui.T("음소거"),delegate{media.IsMuted=!media.IsMuted;});mute.CheckOnClick=true;
        tools.Items.Add(new ToolStripLabel(Ui.T("볼륨")));var volume=new TrackBar{Minimum=0,Maximum=100,Value=50,TickStyle=TickStyle.None,Width=110,Height=26,BackColor=tools.BackColor};volume.ValueChanged+=delegate{media.Volume=volume.Value/100.0;};tools.Items.Add(new ToolStripControlHost(volume));
        var info=Button(Ui.T("상세 정보"),delegate{details.Visible=!details.Visible;});info.CheckOnClick=true;
        title.Dock=DockStyle.Top;title.Height=28;title.Padding=new Padding(12,4,0,0);title.BackColor=Color.FromArgb(24,27,32);
        details.Dock=DockStyle.Top;details.Height=64;details.Padding=new Padding(12,6,0,0);details.BackColor=title.BackColor;details.Visible=false;
        var bottom=new System.Windows.Forms.Panel{Dock=DockStyle.Bottom,Height=48,BackColor=tools.BackColor,Padding=new Padding(8)};
        timing.Dock=DockStyle.Right;timing.Width=230;timing.TextAlign=ContentAlignment.MiddleCenter;bottom.Controls.Add(seek);bottom.Controls.Add(timing);
        seek.MouseDown+=delegate{dragging=true;};seek.MouseUp+=delegate{dragging=false;SeekAbsolute();};seek.Scroll+=delegate{if(!dragging)SeekAbsolute();};
        media.LoadedBehavior=MediaState.Manual;media.UnloadedBehavior=MediaState.Manual;media.Stretch=Stretch.Uniform;media.Volume=.5;media.Focusable=false;media.ScrubbingEnabled=false;
        seekTimer.Tick+=delegate{FlushSeek();};
        media.PreviewKeyDown+=delegate(object sender,System.Windows.Input.KeyEventArgs e){
            switch(e.Key){
                case System.Windows.Input.Key.Space:Toggle();e.Handled=true;break;
                case System.Windows.Input.Key.Left:Seek(-5);e.Handled=true;break;
                case System.Windows.Input.Key.Right:Seek(5);e.Handled=true;break;
                case System.Windows.Input.Key.D:StepFrame(-1);e.Handled=true;break;
                case System.Windows.Input.Key.F:StepFrame(1);e.Handled=true;break;
                case System.Windows.Input.Key.Escape:Close();e.Handled=true;break;
            }
        };        host.Child=media;Controls.Add(host);Controls.Add(details);Controls.Add(title);Controls.Add(tools);Controls.Add(bottom);
        chrome=new FullscreenChrome(this,tools,bottom,delegate(Keys key){var message=new Message();return ProcessCmdKey(ref message,key);});chrome.AddTop(details);chrome.AddTop(title);
        media.MediaOpened+=delegate{ready=true;duration=media.NaturalDuration.HasTimeSpan?media.NaturalDuration.TimeSpan.TotalSeconds:0;seek.Enabled=duration>0;title.Text=Path.GetFileName(current)+"  ·  "+media.NaturalVideoWidth+" × "+media.NaturalVideoHeight;UpdateDetails();};
        media.MediaEnded+=delegate{ended=true;paused=true;play.Text=Ui.T("▶ 다시 재생");};
        media.MediaFailed+=delegate(object sender,System.Windows.ExceptionRoutedEventArgs e){ready=false;paused=true;seek.Enabled=false;media.Stop();play.Text=Ui.T("재생 불가");title.Text=Ui.T("재생할 수 없는 영상: ")+Path.GetFileName(current);details.Visible=true;details.Text="이 파일을 읽을 수 없거나 Windows에 필요한 코덱이 없습니다. 다른 영상은 이전/다음 버튼으로 선택할 수 있습니다.\n"+e.ErrorException.Message;};
        media.MouseLeftButtonDown+=delegate{Toggle();};
        timer.Tick+=delegate{if(ready){if(!dragging && duration>0)seek.Value=Math.Max(0,Math.Min(10000,(int)(media.Position.TotalSeconds/duration*10000)));timing.Text=TimeText(framePosition??media.Position.TotalSeconds)+" / "+TimeText(duration);}};
        Shown+=delegate{Bounds=bounds;LoadCurrent();timer.Start();chrome.Enable();};
        FormClosed+=delegate{loadVersion++;seekTimer.Dispose();chrome.Dispose();timer.Stop();timer.Dispose();media.Close();host.Child=null;};
    }
    ToolStripButton Button(string label,Action action){var b=new ToolStripButton(label){DisplayStyle=ToolStripItemDisplayStyle.Text,Padding=new Padding(4,3,4,3)};b.Click+=delegate{action();};tools.Items.Add(b);return b;}
    void ApplyFlip(){media.RenderTransformOrigin=new System.Windows.Point(.5,.5);media.RenderTransform=new ScaleTransform(flipHorizontal?-1:1,flipVertical?-1:1);}
    void QueueSeek(double seconds){pendingSeek=seconds;if(!seekTimer.Enabled)seekTimer.Start();}
    void FlushSeek(){seekTimer.Stop();if(!pendingSeek.HasValue||!ready)return;double seconds=pendingSeek.Value;pendingSeek=null;media.ScrubbingEnabled=paused;media.Position=TimeSpan.FromTicks((long)Math.Round(seconds*TimeSpan.TicksPerSecond));}
    async void ReadFrameRate(string path,int version){double rate=await System.Threading.Tasks.Task.Run(delegate{try{return MediaFiles.FrameRate(path);}catch{return 0.0;}});if(IsDisposed||version!=loadVersion)return;estimatedFrameRate=rate<=0;frameRate=estimatedFrameRate?30:rate;if(ready)UpdateDetails();}
    static string TimeText(double seconds){var t=TimeSpan.FromSeconds(Math.Max(0,seconds));return ((int)t.TotalHours).ToString("00")+":"+t.Minutes.ToString("00")+":"+t.Seconds.ToString("00")+"."+t.Milliseconds.ToString("000");}
    void LoadCurrent(){seekTimer.Stop();pendingSeek=null;loadVersion++;media.Close();media.ScrubbingEnabled=false;flipHorizontal=flipVertical=false;ApplyFlip();current=files[index];framePosition=null;frameRate=30;estimatedFrameRate=true;ReadFrameRate(current,loadVersion);ready=false;paused=false;ended=false;duration=0;seek.Value=0;seek.Enabled=false;timing.Text=Ui.T("불러오는 중…");title.Text=Path.GetFileName(current);details.Text="";play.Text=Ui.T("Ⅱ 일시정지");media.Source=new Uri(current);media.Play();}
    void UpdateDetails(){try{var file=new FileInfo(current);details.Text=String.Format(Ui.T("파일: {0}  [{1}/{2}]\n크기: {3:N2} MB  ·  수정: {4:yyyy-MM-dd HH:mm:ss}  ·  영상: {5} × {6}  ·  길이: {7}"),file.Name,index+1,files.Length,file.Length/1048576.0,file.LastWriteTime,media.NaturalVideoWidth,media.NaturalVideoHeight,TimeText(duration))+"  ·  "+frameRate.ToString("0.###")+" fps"+(estimatedFrameRate?Ui.T(" (정보 없음: 30 fps 기준)"):"");}catch(IOException){details.Text=Ui.T("파일 정보를 읽을 수 없습니다.");}}
    void Toggle(){if(!ready)return;FlushSeek();framePosition=null;if(ended){media.Position=TimeSpan.Zero;ended=false;}if(paused){media.ScrubbingEnabled=false;media.Play();paused=false;play.Text=Ui.T("Ⅱ 일시정지");}else{media.ScrubbingEnabled=true;media.Pause();paused=true;play.Text=Ui.T("▶ 재생");}}
    void Seek(double delta){if(!ready || duration<=0)return;framePosition=null;QueueSeek(Math.Max(0,Math.Min(duration,(pendingSeek??media.Position.TotalSeconds)+delta)));ended=false;}
    void SeekAbsolute(){if(ready && duration>0){framePosition=null;QueueSeek(duration*seek.Value/10000.0);ended=false;}}
    internal static double FrameTarget(double position,int direction,double fps,double length){
        double frame=Math.Floor(position*fps+0.0001)+direction;
        double last=Math.Max(0,Math.Ceiling(length*fps)-1);
        return Math.Max(0,Math.Min(last,frame))/fps;
    }
    void StepFrame(int direction){
        if(!ready||duration<=0)return;
        media.ScrubbingEnabled=true;media.Pause();paused=true;ended=false;play.Text=Ui.T("▶ 재생");
        double target=FrameTarget(framePosition??media.Position.TotalSeconds,direction,frameRate,duration);
        framePosition=target;
        // Preserve sub-millisecond precision and allow rapid repeated key presses.
        QueueSeek(target);
        seek.Value=Math.Max(0,Math.Min(10000,(int)(target/duration*10000)));
        timing.Text=TimeText(target)+" / "+TimeText(duration);
    }
    void Next(int delta){int next=Math.Max(0,Math.Min(files.Length-1,index+delta));if(next!=index){index=next;LoadCurrent();}}
    protected override bool ProcessCmdKey(ref Message msg,Keys key){switch(key){case Keys.Escape:case Keys.F11:Close();return true;case Keys.Space:Toggle();return true;case Keys.Left:Seek(-5);return true;case Keys.Right:Seek(5);return true;case Keys.D:StepFrame(-1);return true;case Keys.F:StepFrame(1);return true;case Keys.PageDown:Next(1);return true;case Keys.PageUp:Next(-1);return true;case Keys.Home:framePosition=null;if(ready)QueueSeek(0);return true;}return base.ProcessCmdKey(ref msg,key);}
}
}

