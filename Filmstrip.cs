using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LumaView {
class Filmstrip : Control {
    readonly Dictionary<int,Bitmap> cache=new Dictionary<int,Bitmap>();
    readonly Dictionary<int,Rectangle> hits=new Dictionary<int,Rectangle>();
    readonly Timer debounce=new Timer{Interval=100};
    string[] files=new string[0];
    Color background=Color.White;
    int current=-1,version;
    bool loading,closed;
    public event Action<int> Selected;
    public event Action OpenSelected;
    public Filmstrip(){
        DoubleBuffered=true;BackColor=Color.Black;TabStop=false;
        debounce.Tick+=delegate{debounce.Stop();LoadNearby();};
        MouseDoubleClick+=delegate{if(OpenSelected!=null)OpenSelected();};
        MouseWheel+=delegate(object s,MouseEventArgs e){if(files.Length>0 && Selected!=null)Selected(Math.Max(0,Math.Min(files.Length-1,current+(e.Delta<0?1:-1))));};
    }
    public void SetFiles(string[] paths,Color color){files=(string[])paths.Clone();background=color;}
    public void SelectIndex(int index){if(index<0 || index>=files.Length)return;current=index;version++;Invalidate();debounce.Stop();debounce.Start();}
    async void LoadNearby(){
        if(loading){debounce.Start();return;}if(current<0 || closed)return;
        loading=true;int request=version,center=current;
        try{
            var order=new[]{0,-1,1,-2,2,-3,3,-4,4};
            foreach(int offset in order){
                int index=center+offset;if(closed || request!=version)return;
                if(index<0 || index>=files.Length || cache.ContainsKey(index))continue;
                string path=files[index];Color color=background;
                Bitmap result=await Task.Run(delegate{
                    if(ModelFiles.Supported(path))return ModelFiles.Tile(path);
                    if(MediaFiles.IsVideo(path))return MediaFiles.VideoTile(path);
                    var thumb=new Bitmap(180,120);
                    using(var g=Graphics.FromImage(thumb)){
                        g.Clear(Color.FromArgb(30,30,30));
                        try{using(var source=Album.Decode(path)){
                            double scale=Math.Min(174.0/source.Width,114.0/source.Height);
                            int w=Math.Max(1,(int)(source.Width*scale)),h=Math.Max(1,(int)(source.Height*scale));var rect=new Rectangle((180-w)/2,(120-h)/2,w,h);
                            using(var brush=new SolidBrush(color))g.FillRectangle(brush,rect);
                            g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(source,rect);
                        }}catch{using(var font=new Font("Malgun Gothic",9))g.DrawString(Ui.T("미리보기 없음"),font,Brushes.Silver,new RectangleF(0,45,180,40),new StringFormat{Alignment=StringAlignment.Center});}
                    }return thumb;
                });
                if(closed){result.Dispose();return;}
                cache[index]=result;
                foreach(int old in cache.Keys.Where(i=>Math.Abs(i-current)>12).ToArray()){cache[old].Dispose();cache.Remove(old);}
                Invalidate();
            }
        }finally{loading=false;}
    }
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);hits.Clear();if(current<0)return;
        e.Graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
        int cardWidth=Math.Min(140,Math.Max(90,Width/7));int gap=Math.Max(68,cardWidth-20);
        // Far cards first; nearer cards overlap them like a horizontal filmstrip.
        foreach(int offset in new[]{-4,4,-3,3,-2,2,-1,1,0}){
            int index=current+offset;if(index<0 || index>=files.Length)continue;
            int distance=Math.Abs(offset),height=Height-20-Math.Min(distance,3)*8;
            int width=cardWidth-(offset==0?0:12);int x=Width/2+offset*gap-width/2,y=(Height-height)/2;
            var rect=new Rectangle(x,y,width,height);if(rect.Right<0 || rect.Left>Width)continue;
            using(var b=new SolidBrush(Color.FromArgb(34,34,34)))e.Graphics.FillRectangle(b,rect);
            Bitmap thumb;if(cache.TryGetValue(index,out thumb)){
                var content=Rectangle.Inflate(rect,-5,-8);
                if(offset==0)e.Graphics.DrawImage(thumb,content);
                else {int tilt=Math.Min(9,distance*3);Point[] points=offset<0?new[]{new Point(content.Left,content.Top+tilt),new Point(content.Right,content.Top),new Point(content.Left,content.Bottom-tilt)}:new[]{new Point(content.Left,content.Top),new Point(content.Right,content.Top+tilt),new Point(content.Left,content.Bottom)};e.Graphics.DrawImage(thumb,points);}
            }else TextRenderer.DrawText(e.Graphics,"…",Font,rect,Color.Gray,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            using(var pen=new Pen(offset==0?Color.FromArgb(255,173,66):Color.FromArgb(60,60,60),offset==0?3:1))e.Graphics.DrawRectangle(pen,rect);
            hits[index]=rect;
        }
    }
    protected override void OnMouseDown(MouseEventArgs e){
        // Resolve overlapping edges in favor of the center/nearest card.
        if(e.Button==MouseButtons.Left && e.Clicks==1){foreach(var hit in hits.OrderBy(pair=>Math.Abs(pair.Key-current))){if(hit.Value.Contains(e.Location)){if(Selected!=null)Selected(hit.Key);return;}}}
        base.OnMouseDown(e);
    }
    protected override void Dispose(bool disposing){if(disposing){closed=true;version++;debounce.Dispose();foreach(var image in cache.Values)image.Dispose();cache.Clear();}base.Dispose(disposing);}
}
}

