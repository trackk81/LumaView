using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace LumaView {
static class Program {
    [STAThread] static void Main(string[] args) {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length > 0 && args[0] == "--self-test") { Tests.Run(); return; }
        Ui.Load();
        if (FileAssociations.HandleInstallerCommand(args)) return;
        var appIcon=System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Application.Idle+=delegate{foreach(Form window in Application.OpenForms)if(window.Icon!=appIcon)window.Icon=appIcon;};
        string updateId=null;Guid parsed;
        if(args.Length>=2&&args[0]=="--update-complete"&&Guid.TryParseExact(args[1],"N",out parsed)){updateId=args[1];args=args.Skip(2).ToArray();}
        using(var viewer=new Viewer(args.Length>0?args[0]:null)){
            if(updateId!=null)viewer.Shown+=delegate{try{using(var ready=System.Threading.EventWaitHandle.OpenExisting("Local\\LumaView.Update."+updateId)){ready.Set();viewer.ShowUpdateComplete();}}catch(System.Threading.WaitHandleCannotBeOpenedException){}};
            Application.Run(viewer);
        }
        if(AppUpdater.ExitForUpdate)Environment.Exit(0);
    }
}
class NaturalSort : System.Collections.Generic.IComparer<string> {
    [DllImport("shlwapi.dll", CharSet=CharSet.Unicode)] static extern int StrCmpLogicalW(string a, string b);
    public int Compare(string a, string b) { return StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b)); }
}
class Album : IDisposable {
    internal static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Bitmap,object> OpaquePhotos =
        new System.Runtime.CompilerServices.ConditionalWeakTable<Bitmap,object>();
    public string[] Files = new string[0];
    public int Index = -1;
    public Bitmap Image;

    public string Current { get { return Index >= 0 ? Files[Index] : ""; } }
    public static bool Supported(string path) { return new[]{".jpg",".jpeg",".png",".bmp",".gif",".tif",".tiff",".ico"}.Concat(ExtendedDecoder.Extensions).Contains(Path.GetExtension(path).ToLowerInvariant()); }
    public static Bitmap Decode(string path) {
        if(ExtendedDecoder.Extensions.Contains(Path.GetExtension(path).ToLowerInvariant())) return ExtendedDecoder.Decode(path);
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var image = System.Drawing.Image.FromStream(stream, true, true)) {
            if ((long)image.Width * image.Height > 100000000) throw new InvalidDataException("이미지가 너무 큽니다 (최대 1억 픽셀).");
            var bitmap = new Bitmap(image);
            try {
                if (image.PropertyIdList.Contains(0x112)) {
                    int orientation = BitConverter.ToUInt16(image.GetPropertyItem(0x112).Value, 0);
                    RotateFlipType[] transforms = {RotateFlipType.RotateNoneFlipNone,RotateFlipType.RotateNoneFlipNone,RotateFlipType.RotateNoneFlipX,RotateFlipType.Rotate180FlipNone,RotateFlipType.Rotate180FlipX,RotateFlipType.Rotate90FlipX,RotateFlipType.Rotate90FlipNone,RotateFlipType.Rotate270FlipX,RotateFlipType.Rotate270FlipNone};
                    if (orientation >= 1 && orientation <= 8) bitmap.RotateFlip(transforms[orientation]);
                }
            } catch (ArgumentException) { }
            if(image.RawFormat.Guid==ImageFormat.Jpeg.Guid)OpaquePhotos.Add(bitmap,new object());
            return bitmap;
        }
    }
    public void Open(string path) {
        path = Path.GetFullPath(path);
        bool directory = Directory.Exists(path);
        var files = Directory.GetFiles(directory ? path : Path.GetDirectoryName(path)).Where(Supported).OrderBy(p=>p,new NaturalSort()).ToArray();
        if (files.Length == 0) throw new InvalidDataException("폴더에 지원하는 이미지가 없습니다.");
        int index = directory ? 0 : Array.FindIndex(files,p=>String.Equals(p,path,StringComparison.OrdinalIgnoreCase));
        if (index < 0) throw new InvalidDataException("지원하지 않는 파일 형식입니다.");
        var next = Decode(files[index]);
        if (Image != null) Image.Dispose();
        Image = next; Files = files; Index = index;
    }
    public void Select(int index) {
        if (Files.Length == 0) return;
        index = Math.Max(0,Math.Min(Files.Length-1,index));
        var next = Decode(Files[index]);
        if (Image != null) Image.Dispose();
        Image=next; Index=index;
    }
    public void Dispose() { if(Image!=null) { Image.Dispose(); Image=null; } }
}
class Canvas : Control {
    public bool FlipHorizontal,FlipVertical;
    Bitmap image;
    public Bitmap Image {
        get{return image;}
        set{if(ReferenceEquals(image,value))return;image=value;InvalidateGpuImage();}
    }
    GpuCanvas gpu;
    bool gpuUnavailable;
    [DllImport("user32.dll")]
    static extern bool ValidateRect(IntPtr window,IntPtr rectangle);
    internal void InvalidateGpuImage(){if(gpu!=null)gpu.InvalidateImage();}
    protected override void OnHandleDestroyed(EventArgs e){
        if(gpu!=null){gpu.Dispose();gpu=null;}
        gpuUnavailable=false;
        base.OnHandleDestroyed(e);
    }
    protected override void WndProc(ref Message message){
        object marker;
        if(message.Msg==0x000F && message.WParam==IntPtr.Zero && Visible && Image!=null &&
            Width>0 && Height>0 && !gpuUnavailable && !ShowDetails && !FlipHorizontal && !FlipVertical &&
            Album.OpaquePhotos.TryGetValue(Image,out marker)){
            try{
                if(gpu==null)gpu=new GpuCanvas();
                float scale=(float)ViewScale;
                if(gpu.Render(Handle,ClientSize,Image,ImageBounds(scale),scale,BackColor)){
                    ValidateRect(Handle,IntPtr.Zero);
                    message.Result=IntPtr.Zero;
                    return;
                }
                Invalidate();
            }catch(ExternalException){DisableGpu();}
            catch(ArgumentException){DisableGpu();}
            catch(NotSupportedException){DisableGpu();}
            catch(DllNotFoundException){DisableGpu();}
            catch(EntryPointNotFoundException){DisableGpu();}
            catch(OutOfMemoryException){DisableGpu();}
        }
        base.WndProc(ref message);
    }
    void DisableGpu(){
        gpuUnavailable=true;
        if(gpu!=null){gpu.Dispose();gpu=null;}
    }
    RectangleF ImageBounds(float scale){
        float width=Image.Width*scale,height=Image.Height*scale;
        return new RectangleF((Width-width)/2+Pan.X,(Height-height)/2+Pan.Y,width,height);
    }
    public bool Fit = true;
    public double Zoom = 1;
    public bool ShowDetails; public string DetailsText="";
    int detailsTopInset;
    internal void SetDetailsTopInset(int inset){
        if(detailsTopInset==inset)return;
        detailsTopInset=inset;
        if(ShowDetails&&!IsDisposed)Invalidate();
    }
    public PointF Pan; public int CheckerSize=24; public bool Checker=true; public Color BackgroundOne=Color.White; public Color BackgroundTwo=Color.FromArgb(205,205,205);
    Point last;
    public event Action Changed;
    public Canvas() {
        DoubleBuffered=true; BackColor=Color.FromArgb(24,26,30); Dock=DockStyle.Fill; TabStop=true;
        Resize += delegate { if(Fit) Reset(); else Invalidate(); };
        MouseDown += delegate(object s,MouseEventArgs e) { if(e.Button==MouseButtons.Left) { last=e.Location; Capture=true; Cursor=Cursors.Hand; Focus(); } };
        MouseUp += delegate { Capture=false; Cursor=Cursors.Default; };
        MouseMove += delegate(object s,MouseEventArgs e) { if(Capture && Image!=null && e.Location!=last) { Pan=new PointF(Pan.X+e.X-last.X,Pan.Y+e.Y-last.Y); last=e.Location; Invalidate(); } };
        MouseWheel += delegate(object s,MouseEventArgs e) { ChangeZoom(e.Delta>0?1.2:1/1.2,e.Location); };
    }
    public double ViewScale { get { return Image==null?1:Fit?Math.Min((double)Math.Max(1,Width-32)/Image.Width,(double)Math.Max(1,Height-32)/Image.Height):Zoom; } }
    public void Reset() { Pan=PointF.Empty; Invalidate(); if(Changed!=null) Changed(); }
    public void ChangeZoom(double factor,Point anchor) {
        if(Image==null) return;
        double old=ViewScale; Zoom=Math.Max(.01,Math.Min(32,old*factor)); Fit=false;
        double ratio=Zoom/old;
        Pan=new PointF((float)(anchor.X-Width/2.0-(anchor.X-Width/2.0-Pan.X)*ratio),(float)(anchor.Y-Height/2.0-(anchor.Y-Height/2.0-Pan.Y)*ratio));
        Invalidate(); if(Changed!=null) Changed();
    }
    void DrawVisibleImage(PaintEventArgs e, RectangleF bounds, float scale){
        var visible=RectangleF.Intersect(bounds,e.ClipRectangle);
        if(visible.IsEmpty)return;
        var graphics=e.Graphics;
        var state=graphics.Save();
        try{
            // Keep filter samples beyond the viewport, so panning does not create a seam.
            var region=visible;
            if(FlipHorizontal)region.X=bounds.Right-(visible.Right-bounds.Left);
            if(FlipVertical)region.Y=bounds.Bottom-(visible.Bottom-bounds.Top);
            int margin=(int)Math.Ceiling(3/Math.Min(1f,scale));
            int x=Math.Max(0,(int)Math.Floor((region.Left-bounds.Left)/scale)-margin);
            int y=Math.Max(0,(int)Math.Floor((region.Top-bounds.Top)/scale)-margin);
            int right=Math.Min(Image.Width,(int)Math.Ceiling((region.Right-bounds.Left)/scale)+margin);
            int bottom=Math.Min(Image.Height,(int)Math.Ceiling((region.Bottom-bounds.Top)/scale)+margin);
            if(right<=x||bottom<=y)return;
            if(System.Drawing.Image.IsAlphaPixelFormat(Image.PixelFormat) ||
                (Image.PixelFormat&PixelFormat.Indexed)!=0){
                using(var tile=new Bitmap(CheckerSize*2,CheckerSize*2)){
                    using(var tileGraphics=Graphics.FromImage(tile)){
                        tileGraphics.Clear(BackgroundOne);
                        if(Checker)using(var brush=new SolidBrush(BackgroundTwo)){
                            tileGraphics.FillRectangle(brush,CheckerSize,0,CheckerSize,CheckerSize);
                            tileGraphics.FillRectangle(brush,0,CheckerSize,CheckerSize,CheckerSize);
                        }
                    }
                    using(var brush=new TextureBrush(tile))graphics.FillRectangle(brush,visible);
                }
            }
            if(FlipHorizontal||FlipVertical){
                graphics.TranslateTransform(bounds.X+bounds.Width/2,bounds.Y+bounds.Height/2);
                graphics.ScaleTransform(FlipHorizontal?-1:1,FlipVertical?-1:1);
                graphics.TranslateTransform(-bounds.X-bounds.Width/2,-bounds.Y-bounds.Height/2);
            }
            graphics.InterpolationMode=scale>=4?InterpolationMode.NearestNeighbor:InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
            var destination=new RectangleF(bounds.X+x*scale,bounds.Y+y*scale,(right-x)*scale,(bottom-y)*scale);
            graphics.DrawImage(Image,destination,new RectangleF(x,y,right-x,bottom-y),GraphicsUnit.Pixel);
        }finally{graphics.Restore(state);}
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        if(Image==null) {
            using(var title=new Font("Segoe UI",28,FontStyle.Bold))
            using(var subtitle=new Font("Malgun Gothic",11))
            using(var accent=new SolidBrush(Color.FromArgb(117,207,186)))
            using(var muted=new SolidBrush(Color.FromArgb(156,163,175))) {
                var sf=new StringFormat { Alignment=StringAlignment.Center };
                e.Graphics.DrawString("DollView",title,accent,new RectangleF(0,Height/2-70,Width,60),sf);
                e.Graphics.DrawString("이미지를 끌어 놓거나 파일을 열어 주세요\nCtrl+O  ·  JPG / PNG / BMP / GIF / TIFF",subtitle,muted,new RectangleF(0,Height/2,Width,80),sf);
            } return;
        }
        float scale=(float)ViewScale;
        var r=ImageBounds(scale);
        DrawVisibleImage(e,r,scale);
        if(ShowDetails && !String.IsNullOrEmpty(DetailsText)) {
            using(var font=new Font("Malgun Gothic",11,FontStyle.Regular))
            using(var bg=new SolidBrush(Color.FromArgb(195,15,17,20))) {
                int width=Math.Max(80,Math.Min(760,Width-48));
                var size=e.Graphics.MeasureString(DetailsText,font,width);
                var box=new RectangleF(14,14+detailsTopInset,Math.Min(width,size.Width)+24,size.Height+22);
                e.Graphics.FillRectangle(bg,box);
                e.Graphics.DrawString(DetailsText,font,Brushes.White,new RectangleF(26,24+detailsTopInset,width,size.Height+4));
            }
        }
    }
}
partial class Viewer : Form {
    internal string UpdateRestartPath {get{return browsingFolder;}}
    internal void ShowUpdateComplete(){info.Text=Ui.Language=="en"?"Updated to v"+AppUpdater.CurrentVersion:"v"+AppUpdater.CurrentVersion+" 업데이트 완료";}
    readonly Album album=new Album();
    readonly Canvas canvas=new Canvas();
    readonly ToolStrip toolbar=new ViewerToolStrip();
    readonly StatusStrip status=new StatusStrip();
    readonly ToolStripStatusLabel info=new ToolStripStatusLabel();
    readonly Timer slide=new Timer();
    ToolStripButton play;
    bool full;
    bool imageFullscreen,fullscreenAfterLoad;
    ToolStripButton imageModeButton;
    FullscreenChrome chrome;
    Rectangle oldBounds;
    Size galleryMinimumSize;
    FormWindowState oldState;
    bool startupOpening;
    public Viewer(string path) {
        startupOpening=path!=null && File.Exists(path);
        AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
        Text=AppUpdater.AppTitle; Size=new Size(1200,800); MinimumSize=new Size(720,440); StartPosition=FormStartPosition.CenterScreen;
        Font=new Font("Segoe UI",10); BackColor=Ui.Background; KeyPreview=true; AllowDrop=true;
        toolbar.GripStyle=ToolStripGripStyle.Hidden; toolbar.Padding=new Padding(10,8,10,8); toolbar.BackColor=Color.FromArgb(38,42,48); toolbar.ForeColor=Color.White; toolbar.RenderMode=ToolStripRenderMode.System;
        Add(Ui.T("탐색기"),ToggleExplorer,"Ctrl+B"); Add(Ui.T("목록"),ShowPicker,"Enter"); Add(Ui.T("열기"),Choose,"Ctrl+O"); Add(Ui.T("폴더"),Folder,"Ctrl+Shift+O"); Sep();
        Add(Ui.T("◀ 이전"),delegate{Navigate(-1);},"Left"); Add(Ui.T("다음 ▶"),delegate{Navigate(1);},"Right / Space"); Sep();
        Add("−",delegate{Zoom(1/1.2);},"-"); Add("+",delegate{Zoom(1.2);},"+");
        Add(Ui.T("화면 맞춤"),delegate{canvas.Fit=true;canvas.Reset();},"F"); Add("100%",delegate{canvas.Fit=false;canvas.Zoom=1;canvas.Reset();},"1"); Sep();
        Add("↶",delegate{Rotate(false);},"Shift+R"); Add(Ui.T("↷ 회전"),delegate{Rotate(true);},"R"); Sep();
        var flipMenu=new ToolStripDropDownButton(Ui.T("반전")){ToolTipText=Ui.T("화면에서만 반전합니다. 원본은 변경하지 않습니다.")};
        flipMenu.DropDownItems.Add(Ui.T("좌우 반전"),null,delegate{FlipImage(true);});
        flipMenu.DropDownItems.Add(Ui.T("상하 반전"),null,delegate{FlipImage(false);});
        toolbar.Items.Add(flipMenu);Sep();
        play=Add(Ui.T("▶ 슬라이드쇼"),ToggleSlide,"F5");
        var interval=new ToolStripComboBox { DropDownStyle=ComboBoxStyle.DropDownList,AutoSize=false,Width=65 };
        interval.Items.AddRange(new object[]{Ui.T("1초"),Ui.T("3초"),Ui.T("5초"),Ui.T("10초")}); interval.SelectedIndex=1;
        interval.SelectedIndexChanged+=delegate{slide.Interval=new[]{1000,3000,5000,10000}[interval.SelectedIndex];}; toolbar.Items.Add(interval);
        AddDetailsButton();
        imageModeButton=Add(ComponentSetup.Say("전체화면","Fullscreen"),Fullscreen,"Alt+Enter");
        Add(Ui.T("목록으로"),ExitImageView,"Esc");
        status.Padding=new Padding(14,6,14,6);info.AutoToolTip=true;
        info.Spring=true; info.TextAlign=ContentAlignment.MiddleLeft; status.Items.Add(info); status.BackColor=Color.FromArgb(38,42,48); status.ForeColor=Color.FromArgb(205,211,220); status.SizingGrip=false;
        SetupWorkspace(); toolbar.Dock=DockStyle.Top; Controls.Add(toolbar); Controls.Add(status); SetupBackgroundMenu(); SetupFileCommands(); toolbar.Visible=false;
        canvas.Changed+=UpdateInfo; canvas.DoubleClick+=delegate{Fullscreen();};
        slide.Interval=3000; slide.Tick+=delegate{ if(album.Files.Length>1) Select((album.Index+1)%album.Files.Length); else StopSlide(); };
        DragEnter+=delegate(object s,DragEventArgs e){ if(e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect=DragDropEffects.Copy; };
        DragDrop+=delegate(object s,DragEventArgs e){var paths=(string[])e.Data.GetData(DataFormats.FileDrop);if(paths!=null && paths.Length>0) Open(paths[0]);};
        FormClosed+=delegate{if(chrome!=null)chrome.Dispose();slide.Dispose();album.Dispose();};
        Ui.Apply(this);Ui.Apply(toolbar);
        UpdateInfo(); if(path!=null) Shown+=delegate{
            if(startupOpening && groupSequences && Album.Supported(path)){
                try{sequenceGroups=Sequences.Group(Directory.GetFiles(Path.GetDirectoryName(Path.GetFullPath(path))));}
                catch(IOException){}catch(UnauthorizedAccessException){}
            }
            enterAfterLoad=startupOpening;
            Open(path);
        };
        if(startupOpening){
            browsingFolder=Path.GetDirectoryName(Path.GetFullPath(path));
            if(Album.Supported(path))EnterImageView(true);
        }
    }
    ToolStripButton Add(string text,Action action,string shortcut) { var b=new ToolStripButton(text){DisplayStyle=ToolStripItemDisplayStyle.Text,ToolTipText=text+" ("+shortcut+")",Padding=new Padding(4,3,4,3)};b.Click+=delegate{action();if(full)canvas.Focus();else fileList.Focus();};toolbar.Items.Add(b);return b; }
    void Sep(){toolbar.Items.Add(new ToolStripSeparator());}
    void Choose(){using(var d=new OpenFileDialog{Filter="이미지, 영상 및 3D 모델|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.ico;*.exr;*.psd;*.psb;*.webp;*.tga;*.hdr;*.dpx;*.dds;*.mp4;*.m4v;*.mov;*.wmv;*.avi;*.mpg;*.mpeg;*.mkv;*.webm;*.obj;*.glb;*.gltf;*.stl;*.fbx|모든 파일|*.*"})if(d.ShowDialog(this)==DialogResult.OK)Open(d.FileName);}
    void Folder(){using(var d=new FolderBrowserDialog{Description=Ui.T("이미지가 있는 폴더 선택")})if(d.ShowDialog(this)==DialogResult.OK)Open(d.SelectedPath);}
    void Guard(Action action){try{action();}catch(Exception ex){StopSlide();MessageBox.Show(this,ex.Message,Ui.T("이미지를 열 수 없습니다"),MessageBoxButtons.OK,MessageBoxIcon.Information);}}
    void Open(string path){if(busy)return;StopSlide();if(Directory.Exists(path)){if(full)ExitImageView();Browse(path);return;}if(ModelFiles.Supported(path)){OpenModel(path);return;}if(OpenSequence(path))return;if(MediaFiles.IsVideo(path)){OpenVideo(path);return;}if(!full)enterAfterLoad=true;LoadAsync(path,-1);}
    void Select(int index){if(!busy && album.Files.Length>0)LoadAsync(null,Math.Max(0,Math.Min(album.Files.Length-1,index)));}
    void Loaded(){canvas.Image=album.Image;canvas.Fit=true;canvas.Reset();Text=AppUpdater.WindowTitle(Path.GetFileName(album.Current)); SyncExplorer();}
    void Navigate(int delta){if(!full){if(fileList.Items.Count==0)return;int index=fileList.SelectedIndices.Count>0?fileList.SelectedIndices[0]:0;index=Math.Max(0,Math.Min(fileList.Items.Count-1,index+delta));fileList.Items[index].Selected=true;fileList.Items[index].Focused=true;fileList.Items[index].EnsureVisible();return;}if(album.Files.Length>0)Select(album.Index+delta);}
    void Zoom(double factor){canvas.ChangeZoom(factor,new Point(canvas.Width/2,canvas.Height/2));}
    void Rotate(bool clockwise){if(busy || album.Image==null)return;album.Image.RotateFlip(clockwise?RotateFlipType.Rotate90FlipNone:RotateFlipType.Rotate270FlipNone);canvas.InvalidateGpuImage();canvas.Reset();}
    void FlipImage(bool horizontal){if(busy||album.Image==null)return;album.Image.RotateFlip(horizontal?RotateFlipType.RotateNoneFlipX:RotateFlipType.RotateNoneFlipY);canvas.InvalidateGpuImage();canvas.Invalidate();canvas.Focus();}
    void ToggleSlide(){if(!full){if(fileList.SelectedItems.Count>0){startSlideAfterLoad=true;OpenSelectedImage();}return;}if(slide.Enabled){StopSlide();return;}if(album.Files.Length==0){Choose();if(album.Files.Length==0)return;}slide.Start();play.Text=Ui.T("Ⅱ 일시정지");UpdateInfo();}
    void StopSlide(){slide.Stop();if(play!=null)play.Text=Ui.T("▶ 슬라이드쇼");UpdateInfo();}
    void UpdateInfo(){RefreshDetails();if(!full){UpdateBrowserInfo();return;}info.Text=album.Image==null?Ui.T("준비  ·  원본 파일을 변경하지 않습니다"):String.Format("  {0} / {1}     {2}     {3} × {4} px     {5:0}%     {6}",album.Index+1,album.Files.Length,Path.GetFileName(album.Current),album.Image.Width,album.Image.Height,canvas.ViewScale*100,slide.Enabled?Ui.T("슬라이드쇼 재생 중"):"");}
    protected override bool ProcessCmdKey(ref Message msg,Keys key){
        if(!folderAddress.Focused){if(key==(Keys.Control|Keys.C)){CopyFile();return true;}if(key==(Keys.Control|Keys.V)){PasteFiles();return true;}if(key==Keys.F2){RenameFile();return true;}}
        if(key==Keys.Escape){enterAfterLoad=false;fullscreenAfterLoad=false;if(imageFullscreen)SetImageFullscreen(false);else if(full)ExitImageView();StopSlide();return true;}
        if(key==(Keys.Alt|Keys.Enter)){Fullscreen();return true;}
        if(key==(Keys.Control|Keys.B)){ToggleExplorer();return true;}
        if(key==Keys.Enter && !folderAddress.Focused){ShowPicker();return true;}
        if(!full && folderAddress.Focused) return base.ProcessCmdKey(ref msg,key);
        if(!full && (fileList.Focused || folders.Focused) && (key==Keys.Left || key==Keys.Right || key==Keys.Up || key==Keys.Down || key==Keys.Home || key==Keys.End || key==Keys.PageUp || key==Keys.PageDown || key==Keys.Space)) return base.ProcessCmdKey(ref msg,key);
        if(key==Keys.Enter){ShowPicker();return true;}
        switch(key){
            case Keys.Control|Keys.O:Choose();return true;
            case Keys.Control|Keys.Shift|Keys.O:Folder();return true;
            case Keys.Right:case Keys.Space:case Keys.PageDown:Navigate(1);return true;
            case Keys.Left:case Keys.Back:case Keys.PageUp:Navigate(-1);return true;
            case Keys.Home:Select(0);return true;
            case Keys.End:Select(album.Files.Length-1);return true;
            case Keys.Add:case Keys.Oemplus:case Keys.Shift|Keys.Oemplus:Zoom(1.2);return true;
            case Keys.Subtract:case Keys.OemMinus:Zoom(1/1.2);return true;
            case Keys.F:canvas.Fit=true;canvas.Reset();return true;
            case Keys.D1:canvas.Fit=false;canvas.Zoom=1;canvas.Reset();return true;
            case Keys.I:ToggleDetails();return true;
            case Keys.R:Rotate(true);return true;
            case Keys.Shift|Keys.R:Rotate(false);return true;
            case Keys.F5:ToggleSlide();return true;
        }return base.ProcessCmdKey(ref msg,key);
    }
}
static class Tests {
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);}
    public static void Run(){
        string dir=Path.Combine(Path.GetTempPath(),"LumaView-test-"+Guid.NewGuid());Directory.CreateDirectory(dir);
        
        try{
            using(var b=new Bitmap(120,80)){using(var g=Graphics.FromImage(b)){g.Clear(Color.Coral);g.FillRectangle(Brushes.Teal,0,0,30,80);}foreach(string name in new[]{"image10.png","image2.png","image1.png"})b.Save(Path.Combine(dir,name),ImageFormat.Png);b.Save(Path.Combine(dir,"image3.jpg"),ImageFormat.Jpeg);}
            string copies=Path.Combine(dir,"copies");Directory.CreateDirectory(copies);
            string original=Path.Combine(dir,"image1.png");
            string copied=FileCommands.CopyInto(original,copies);
            string duplicate=FileCommands.CopyInto(original,copies);
            Check(copied!=duplicate && File.ReadAllBytes(copied).SequenceEqual(File.ReadAllBytes(original)),"file paste preserves bytes and avoids overwrite");
            string selfCopy=FileCommands.CopyInto(copied,copies);
            Check(selfCopy!=copied && File.Exists(copied),"same folder copy preserves original");
            Console.WriteLine("PASS: file paste bytes, collision naming, same-folder copy");
            using(var a=new Album()){
                a.Open(dir);Check(Path.GetFileName(a.Current)=="image1.png","natural sort first");a.Select(1);Check(Path.GetFileName(a.Current)=="image2.png","natural sort numeric");Check(a.Image.Width==120 && a.Image.Height==80,"dimensions");
                a.Image.RotateFlip(RotateFlipType.Rotate90FlipNone);Check(a.Image.Width==80&&a.Image.Height==120,"rotate");
                using(var raw=Album.Decode(a.Current))Check(raw.Width==120,"original unchanged");
                using(var f=new FileStream(a.Current,FileMode.Open,FileAccess.Read,FileShare.None))Check(f.Length>0,"source unlocked");
                string previous=a.Current;try{a.Open(Path.Combine(dir,"broken.png"));throw new Exception("expected decode failure");}catch(InvalidDataException){}Check(a.Current==previous && a.Image!=null,"failed open preserves current");
                a.Select(999);Check(a.Index==a.Files.Length-1,"upper bound");a.Select(-1);Check(a.Index==0,"lower bound");
                a.Open(Path.Combine(dir,"image3.jpg"));Check(a.Image.Width==120,"jpeg decode");
                using(var c=new Canvas()){c.Size=new Size(632,432);c.Image=a.Image;Check(Math.Abs(c.ViewScale-5)<.001,"fit scale");c.ChangeZoom(2,new Point(316,216));Check(!c.Fit&&Math.Abs(c.ViewScale-10)<.001,"zoom");}
            }
            foreach(string ext in ExtendedDecoder.Extensions) {
    string fixture=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"samples","alpha"+ext);
    using(var decoded=Album.Decode(fixture)){Check(decoded.Width==320 && decoded.Height==200,"extended dimensions "+ext);Check(decoded.GetPixel(150,100).R>150,"extended colors "+ext);if(ext==".exr" || ext==".psd" || ext==".psb" || ext==".webp")Check(decoded.GetPixel(0,0).A==0,"extended alpha "+ext);}
}
using(var transparent=new Bitmap(80,80)) using(var c=new Canvas()) using(var render=new Bitmap(160,160)) {
    c.Size=new Size(160,160);c.Image=transparent;c.Fit=false;c.Zoom=1;c.DrawToBitmap(render,new Rectangle(0,0,160,160));
    Check(render.GetPixel(48,48).ToArgb()==Color.White.ToArgb(),"bright checker");Check(render.GetPixel(72,48).R==205,"24px checker");
    c.Checker=false;c.BackgroundOne=Color.Red;c.DrawToBitmap(render,new Rectangle(0,0,160,160));Check(render.GetPixel(72,48).ToArgb()==Color.Red.ToArgb(),"solid background");
}
Console.WriteLine("PASS: EXR/PSD/PSB/WebP/TGA/HDR/DPX/DDS decode, dimensions/color, supported alpha, bright 24px checker and solid background");
Console.WriteLine("PASS: natural sort, PNG/JPEG decode, dimensions, rotation, original preservation, unlocked file, failed-open transaction, navigation bounds, fit, zoom\r\nFixtures: "+dir);
        }catch(Exception e){Console.WriteLine("FAIL: "+e);Environment.ExitCode=1;}
    }
}
}


