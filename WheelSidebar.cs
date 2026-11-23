using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LumaView {
// Shared by the two viewing surfaces so a held drag can cross an image/video transition.
sealed class MediaDialInput {
    int wheelDelta;
    int? anchorY;
    Point? zoomAnchor;
    int lastY;
    public void Reset(){wheelDelta=0;anchorY=null;zoomAnchor=null;}
    public void ResumeDrag(Control surface){
        if(!anchorY.HasValue)return;
        if((Control.MouseButtons&MouseButtons.Middle)!=0)surface.Capture=true;
        else anchorY=null;
    }
    public void Attach(Control surface,Func<bool> enabled,Action<int> navigate,Action<double,Point> zoom=null){
        surface.MouseWheel+=delegate(object sender,MouseEventArgs e){
            if(!enabled()||e.Delta==0)return;
            if((Control.ModifierKeys&Keys.Control)!=0){
                if(zoom!=null)zoom(e.Delta>0?1.2:1/1.2,e.Location);
                return;
            }
            wheelDelta+=e.Delta;
            int steps=wheelDelta/SystemInformation.MouseWheelScrollDelta;
            wheelDelta%=SystemInformation.MouseWheelScrollDelta;
            navigate(-steps);
        };
        surface.MouseDown+=delegate(object sender,MouseEventArgs e){
            if(e.Button!=MouseButtons.Middle||!enabled())return;
            surface.Focus();surface.Capture=true;
            lastY=surface.PointToScreen(e.Location).Y;
            if(zoom!=null&&(Control.ModifierKeys&Keys.Control)!=0){
                zoomAnchor=e.Location;anchorY=null;
                return;
            }
            zoomAnchor=null;anchorY=lastY;navigate(0);
        };
        surface.MouseMove+=delegate(object sender,MouseEventArgs e){
            if(!enabled())return;
            if((e.Button&MouseButtons.Middle)==0){anchorY=null;zoomAnchor=null;return;}
            if(!zoomAnchor.HasValue&&!anchorY.HasValue)return;
            if(zoomAnchor.HasValue&&(zoom==null||!surface.Capture))return;
            int y=surface.PointToScreen(e.Location).Y;
            if(y==lastY)return;
            int travel=lastY-y;
            lastY=y;
            if(zoomAnchor.HasValue){
                // Dragging up 120 logical pixels doubles the scale around the original press.
                zoom(Math.Pow(2,travel*96.0/(120*surface.DeviceDpi)),zoomAnchor.Value);
                return;
            }
            int stride=Math.Max(1,48*surface.DeviceDpi/96);
            int steps=(y-anchorY.Value)/stride;
            anchorY+=steps*stride;
            navigate(steps);
        };
        surface.MouseUp+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Middle){anchorY=null;zoomAnchor=null;surface.Capture=false;}};
        if(zoom!=null)surface.MouseCaptureChanged+=delegate{if(!surface.Capture)zoomAnchor=null;};
    }
}
sealed class WheelSidebar : IDisposable {
    const int Neighbors=5, HoldMs=1500, FadeMs=260, TurnMs=150;
    const double Angle=1.0/(Neighbors+.5); // Keep the full dial within a readable arc.
    readonly Control canvas;
    readonly Action<Rectangle> invalidate;
    readonly Stopwatch clock=Stopwatch.StartNew();
    readonly Timer timer=new Timer();
    readonly Dictionary<string,Bitmap> thumbnails=new Dictionary<string,Bitmap>(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> wanted=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    string[] files=new string[0];
    int index=-1,direction;
    long hideAt,turnStart;
    bool visible,loading,disposed;
    Bitmap layer,selectedImage;
    string selectedPath;
    public float Opacity {get;private set;}

    public WheelSidebar(Control target,Action<Rectangle> invalidateArea=null){
        canvas=target;invalidate=invalidateArea??target.Invalidate;
        timer.Tick+=delegate{Tick();};
    }
    internal static ToolStripButton ToggleButton(Action changed){
        var button=new ToolStripButton(Ui.T("사이드바")){
            ToolTipText=Ui.T("휠 또는 가운데 버튼 드래그로 주변 파일 보기"),
            DisplayStyle=ToolStripItemDisplayStyle.Text,CheckOnClick=true,
            Checked=Ui.WheelSidebarEnabled};
        button.Click+=delegate{
            Ui.WheelSidebarEnabled=button.Checked;
            try{Ui.Save(Ui.Language,Ui.Theme);}catch(IOException){}catch(UnauthorizedAccessException){}
            changed();
        };
        return button;
    }
    public void Reveal(string[] paths,int current,Bitmap image){
        if(disposed||current<0||current>=paths.Length)return;
        visible=true;Opacity=1;hideAt=clock.ElapsedMilliseconds+HoldMs;
        Select(paths,current,image);
        InvalidateArea();Schedule();
    }
    public void Select(string[] paths,int current,Bitmap image){
        if(!visible||current<0||current>=paths.Length)return;
        if(index==current&&selectedPath==paths[current]&&ReferenceEquals(selectedImage,image))return;
        int previous=index;files=paths;index=current;selectedPath=paths[current];selectedImage=image;
        if(previous!=current){
            direction=previous>=0&&Math.Abs(current-previous)==1?Math.Sign(current-previous):0;
            turnStart=clock.ElapsedMilliseconds;
        }
        wanted.Clear();
        for(int i=Math.Max(0,index-Neighbors-2);i<=Math.Min(files.Length-1,index+Neighbors+2);i++)wanted.Add(files[i]);
        foreach(string key in thumbnails.Keys.Where(k=>!wanted.Contains(k)).ToArray()){
            if(thumbnails[key]!=null)thumbnails[key].Dispose();
            thumbnails.Remove(key);
        }
        // The selected image has already been decoded by the viewer.
        if(image!=null){
            Bitmap thumb;
            if(thumbnails.TryGetValue(selectedPath,out thumb)&&thumb!=null)thumb.Dispose();
            thumbnails[selectedPath]=ScaleThumbnail(image);
        }
        InvalidateLayer();LoadNearby();Schedule();
    }
    static Bitmap ScaleThumbnail(Bitmap image){
        double scale=Math.Min(1,Math.Min(192.0/image.Width,160.0/image.Height));
        var result=new Bitmap(Math.Max(1,(int)Math.Round(image.Width*scale)),Math.Max(1,(int)Math.Round(image.Height*scale)),PixelFormat.Format32bppPArgb);
        using(var g=Graphics.FromImage(result)){
            g.InterpolationMode=InterpolationMode.Bilinear;
            g.DrawImage(image,new Rectangle(Point.Empty,result.Size),0,0,image.Width,image.Height,GraphicsUnit.Pixel);
        }
        return result;
    }
    static Bitmap ReadThumbnail(string path){
        try{
            if(MediaFiles.IsVideo(path))return MediaFiles.VideoTile(path,true);
            if(!ExtendedDecoder.Extensions.Contains(Path.GetExtension(path).ToLowerInvariant())){
                using(var shell=MediaFiles.ReadShellThumbnail(path)){
                    if(shell!=null)return ScaleThumbnail(shell);
                }
                using(var image=Album.Decode(path))return ScaleThumbnail(image);
            }
            using(var image=ExtendedDecoder.Decode(path,192,160))return ScaleThumbnail(image);
        }catch(Exception){return null;} // A missing or damaged neighbor must not interrupt navigation.
    }
    string NextMissing(){
        if(!thumbnails.ContainsKey(files[index]))return files[index];
        for(int distance=1;distance<=Neighbors+1;distance++){
            int before=index-distance,after=index+distance;
            if(before>=0&&!thumbnails.ContainsKey(files[before]))return files[before];
            if(after<files.Length&&!thumbnails.ContainsKey(files[after]))return files[after];
        }
        return null;
    }
    async void LoadNearby(){
        if(loading)return;
        loading=true;
        try{
            while(visible&&!disposed){
                string path=NextMissing();
                if(path==null)break;
                var bitmap=await Task.Run(()=>ReadThumbnail(path));
                if(disposed||!visible||!wanted.Contains(path)||thumbnails.ContainsKey(path)){if(bitmap!=null)bitmap.Dispose();continue;}
                thumbnails[path]=bitmap;InvalidateLayer();
            }
        }finally{loading=false;}
    }
    void InvalidateLayer(){
        if(layer!=null){layer.Dispose();layer=null;}
        InvalidateArea();
    }
    void InvalidateArea(){if(!disposed)invalidate(Bounds);}
    void Schedule(){
        if(!visible)return;
        long now=clock.ElapsedMilliseconds;
        timer.Interval=direction!=0||now>=hideAt?20:(int)Math.Max(1,hideAt-now);
        if(!timer.Enabled)timer.Start();
    }
    void Tick(){
        long now=clock.ElapsedMilliseconds;
        if(now>=hideAt+FadeMs){Hide(false);return;}
        if(direction!=0){
            if(now-turnStart>=TurnMs)direction=0;
            InvalidateLayer();
        }
        Opacity=1-(float)Math.Max(0,now-hideAt)/FadeMs;
        InvalidateArea();Schedule();
    }
    public void Hide(bool clearCache=true){
        timer.Stop();visible=false;Opacity=0;index=-1;direction=0;files=new string[0];wanted.Clear();selectedImage=null;selectedPath=null;
        if(clearCache){
            foreach(var bitmap in thumbnails.Values)if(bitmap!=null)bitmap.Dispose();
            thumbnails.Clear();
        }
        InvalidateLayer();
    }
    Rectangle Bounds{
        get{
            float dpi=canvas.DeviceDpi/96f;
            int h=(int)Math.Min(820*dpi,canvas.ClientSize.Height*.92),w=(int)Math.Min(124*dpi,Math.Min(canvas.ClientSize.Width*.22,h*.32));
            return new Rectangle(canvas.ClientSize.Width-w-Math.Max(3,(int)(12*dpi)),(canvas.ClientSize.Height-h)/2,w,h);
        }
    }
    public Point Location{get{return Bounds.Location;}}
    public Bitmap Layer(){
        if(!visible||disposed)return null;
        var bounds=Bounds;
        int w=bounds.Width,h=bounds.Height;
        if(w<18||h<60)return null;
        if(layer!=null&&layer.Width==w&&layer.Height==h)return layer;
        if(layer!=null)layer.Dispose();
        layer=new Bitmap(w,h,PixelFormat.Format32bppPArgb);
        double progress=Math.Min(1,(clock.ElapsedMilliseconds-turnStart)/(double)TurnMs);
        double shift=direction*Math.Pow(1-progress,3);
        using(var g=Graphics.FromImage(layer)){
            g.SmoothingMode=SmoothingMode.AntiAlias;g.InterpolationMode=InterpolationMode.HighQualityBilinear;
            for(int distance=Neighbors+1;distance>=1;distance--){
                DrawCard(g,index-distance,-distance+shift,false);
                DrawCard(g,index+distance,distance+shift,false);
            }
            DrawCard(g,index,0,true);
        }
        FadeEnds(layer);
        return layer;
    }
    void DrawCard(Graphics g,int fileIndex,double slot,bool center){
        if(fileIndex<0||fileIndex>=files.Length||Math.Abs(slot)>=Neighbors+1)return;
        float w=layer.Width,h=layer.Height;
        double a=slot*Angle,radius=(h*.5-2)/Math.Sin((Neighbors+.5)*Angle);
        float mid=h*.5f+(float)(radius*Math.Sin(a));
        float height=(float)(2*radius*Math.Sin(Angle*.5)*Math.Cos(a))*.93f;
        float width=(w-4)*(float)(.70+.30*Math.Cos(a));
        var rect=new RectangleF(w-width-2,mid-height/2,width,height);
        using(var path=ModernUi.Rounded(rect,Math.Min(6,height/4))){
            var state=g.Save();
            g.SetClip(path,CombineMode.Intersect);
            using(var back=new SolidBrush(Color.FromArgb(235,30,34,40)))g.FillRectangle(back,rect);
            Bitmap bitmap;
            if(thumbnails.TryGetValue(files[fileIndex],out bitmap)&&bitmap!=null){
                // Fit the photo into the front face, then foreshorten that face on the dial.
                float faceHeight=(float)(2*radius*Math.Sin(Angle*.5))*.93f;
                double scale=Math.Min((w-8)/bitmap.Width,(faceHeight-4)/bitmap.Height);
                float iw=(float)(bitmap.Width*scale)*width/(w-4),ih=(float)(bitmap.Height*scale)*height/faceHeight;
                g.DrawImage(bitmap,new RectangleF(rect.Left+(width-iw)/2,mid-ih/2,iw,ih));
            }
            g.Restore(state);
            using(var pen=new Pen(center?Color.FromArgb(255,131,213,240):Color.FromArgb(110,122,136,150),center?2:1))g.DrawPath(pen,path);
        }
    }
    static void FadeEnds(Bitmap bitmap){
        // Scale premultiplied color and alpha together so both ends dissolve into the photo.
        var data=bitmap.LockBits(new Rectangle(Point.Empty,bitmap.Size),ImageLockMode.ReadWrite,PixelFormat.Format32bppPArgb);
        try{
            var row=new byte[bitmap.Width*4];
            // Start in the gap between the first and second cards on each side of the dial.
            double fadeStart=Math.Sin(1.5*Angle)/Math.Sin((Neighbors+.5)*Angle);
            for(int y=0;y<bitmap.Height;y++){
                double distance=Math.Abs((2*y+1.0)/bitmap.Height-1);
                if(distance<=fadeStart)continue;
                double depth=(distance-fadeStart)/(1-fadeStart);
                double opacity=(1-depth)*(1-.5*depth);
                int alpha=(int)Math.Round(255*opacity);
                IntPtr address=IntPtr.Add(data.Scan0,y*data.Stride);
                Marshal.Copy(address,row,0,row.Length);
                for(int i=0;i<row.Length;i++)row[i]=(byte)((row[i]*alpha+127)/255);
                Marshal.Copy(row,0,address,row.Length);
            }
        }finally{bitmap.UnlockBits(data);}
    }
    public void Dispose(){if(disposed)return;disposed=true;Hide();timer.Dispose();}
}
}
