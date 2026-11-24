using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;

namespace LumaView {
sealed class GifPlayback : IDisposable {
    readonly Stream stream;
    readonly Image source;
    readonly long[] ends;
    readonly long plays;
    readonly Stopwatch clock=new Stopwatch();
    readonly Point[] corners={new Point(0,0),new Point(1,0),new Point(0,1)};
    readonly Point[] destination=new Point[3];
    int current;

    GifPlayback(Stream stream,Image source,int count){
        this.stream=stream;this.source=source;
        var delays=source.PropertyIdList.Contains(0x5100)?source.GetPropertyItem(0x5100).Value:new byte[0];
        ends=new long[count];
        long end=0;
        for(int i=0;i<count;i++){
            uint ticks=i<delays.Length/4?BitConverter.ToUInt32(delays,i*4):0;
            end+=ticks<2?100L:ticks*10L;
            ends[i]=end;
        }
        plays=ReadPlays(stream);
    }
    // GDI+ reports a loop count of one both for an absent extension (play once)
    // and for one requested repeat (play twice).
    static long ReadPlays(Stream input){
        long saved=input.Position;
        try{
            input.Position=10;
            int packed=input.ReadByte();
            input.Seek(2+((packed&128)!=0?3*(2<<(packed&7)):0),SeekOrigin.Current);
            byte[] app=new byte[11];
            int block;
            while((block=input.ReadByte())==0x21 || block==0x2c){
                if(block==0x2c){
                    input.Seek(8,SeekOrigin.Current);
                    int flags=input.ReadByte();
                    input.Seek(1+((flags&128)!=0?3*(2<<(flags&7)):0),SeekOrigin.Current);
                }else if(input.ReadByte()==0xff){
                    int length=input.ReadByte();
                    if(length!=app.Length)break;
                    if(input.Read(app,0,length)!=length)break;
                    string id=Encoding.ASCII.GetString(app);
                    if(id=="NETSCAPE2.0"||id=="ANIMEXTS1.0"){
                        if(input.ReadByte()==3&&input.ReadByte()==1){
                            int lo=input.ReadByte(),hi=input.ReadByte();
                            if(lo>=0&&hi>=0){int repeats=lo+(hi<<8);return repeats==0?0:repeats+1;}
                        }
                        break;
                    }
                }
                int size;
                while((size=input.ReadByte())>0)input.Seek(size,SeekOrigin.Current);
            }
            return 1;
        }finally{input.Position=saved;}
    }
    internal static GifPlayback Open(string path,out Bitmap first){
        first=null;
        var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        Image source=null;
        try{
            source=Image.FromStream(stream,true,false);
            int count=source.FrameDimensionsList.Contains(FrameDimension.Time.Guid)?source.GetFrameCount(FrameDimension.Time):1;
            var size=PsdDecoder.ComputeTargetSize(source.Width,source.Height,0,0);
            first=new Bitmap(size.Width,size.Height,PixelFormat.Format32bppPArgb);
            if(size!=source.Size)Album.SetOriginalSize(first,source.Size);
            using(var g=Graphics.FromImage(first))g.DrawImage(source,new Rectangle(Point.Empty,size),0,0,source.Width,source.Height,GraphicsUnit.Pixel);
            if(count<=1)return null;
            var playback=new GifPlayback(stream,source,count);
            source=null;stream=null;
            return playback;
        }catch{if(first!=null)first.Dispose();throw;}
        finally{if(source!=null)source.Dispose();if(stream!=null)stream.Dispose();}
    }
    internal Bitmap Transform(Bitmap previous,RotateFlipType transform){
        var transformed=new Point[3];
        for(int i=0;i<corners.Length;i++){
            var p=corners[i];
            if(transform==RotateFlipType.Rotate90FlipNone)transformed[i]=new Point(1-p.Y,p.X);
            else if(transform==RotateFlipType.Rotate270FlipNone)transformed[i]=new Point(p.Y,1-p.X);
            else if(transform==RotateFlipType.RotateNoneFlipX)transformed[i]=new Point(1-p.X,p.Y);
            else transformed[i]=new Point(p.X,1-p.Y);
        }
        bool swap=transform==RotateFlipType.Rotate90FlipNone||transform==RotateFlipType.Rotate270FlipNone;
        var target=new Bitmap(swap?previous.Height:previous.Width,swap?previous.Width:previous.Height,PixelFormat.Format32bppPArgb);
        try{
            var original=Album.OriginalSize(previous);
            if(original!=previous.Size)Album.SetOriginalSize(target,swap?new Size(original.Height,original.Width):original);
            DrawFrame(target,transformed);
            transformed.CopyTo(corners,0);
            return target;
        }catch{target.Dispose();throw;}
    }
    void DrawFrame(Bitmap target,Point[] orientation){
        for(int i=0;i<3;i++)destination[i]=new Point(orientation[i].X*target.Width,orientation[i].Y*target.Height);
        using(var g=Graphics.FromImage(target)){
            g.CompositingMode=CompositingMode.SourceCopy;
            g.PixelOffsetMode=PixelOffsetMode.Half;
            g.InterpolationMode=Album.OriginalSize(target)==target.Size?InterpolationMode.NearestNeighbor:InterpolationMode.HighQualityBilinear;
            g.DrawImage(source,destination,new Rectangle(0,0,source.Width,source.Height),GraphicsUnit.Pixel);
        }
    }
    internal void Pause(){clock.Stop();}
    internal int Advance(Bitmap target,out bool changed){
        clock.Start();
        long elapsed=clock.ElapsedMilliseconds;
        int delay=RenderAt(elapsed,target,out changed);
        if(delay==0){clock.Stop();return 0;}
        return (int)Math.Max(1,delay-(clock.ElapsedMilliseconds-elapsed));
    }
    internal int RenderAt(long elapsed,Bitmap target,out bool changed){
        long cycle=ends[ends.Length-1];
        bool finished=plays!=0&&elapsed/cycle>=plays;
        long offset=elapsed%cycle;
        int frame=finished?ends.Length-1:Array.BinarySearch(ends,offset+1);
        if(frame<0)frame=~frame;
        changed=frame!=current;
        if(changed){
            source.SelectActiveFrame(FrameDimension.Time,frame);
            DrawFrame(target,corners);
            current=frame;
        }
        return finished?0:(int)Math.Min(int.MaxValue,ends[frame]-offset);
    }
    public void Dispose(){clock.Stop();source.Dispose();stream.Dispose();}
}
}
