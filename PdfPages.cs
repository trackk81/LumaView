using System;
using System.Drawing;
using System.IO;
using System.Threading;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace LumaView {
static class PdfPages {
    static readonly object gate=new object();
    static string cachedPath;
    static long length;
    static DateTime modified;
    static IRandomAccessStream source;
    static PdfDocument document;
    static readonly Timer idle=new Timer(delegate{
        lock(gate){
            document=null;
            if(source!=null){source.Dispose();source=null;}
        }
    },null,Timeout.Infinite,Timeout.Infinite);

    public static bool IsPdf(string path){return path!=null&&path.EndsWith(".pdf",StringComparison.OrdinalIgnoreCase);}

    // Call under gate. Pages are rendered on demand and share one open document.
    static PdfDocument Open(string path){
        idle.Change(15000,Timeout.Infinite);
        var file=new FileInfo(path);
        long size=file.Length;
        DateTime stamp=file.LastWriteTimeUtc;
        if(document!=null&&String.Equals(cachedPath,file.FullName,StringComparison.OrdinalIgnoreCase)&&length==size&&modified==stamp)return document;
        var input=new FileStream(file.FullName,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete).AsRandomAccessStream();
        try{
            var next=PdfDocument.LoadFromStreamAsync(input).AsTask().GetAwaiter().GetResult();
            if(next.PageCount==0)throw new InvalidDataException(ComponentSetup.Say("PDF에 페이지가 없습니다.","The PDF contains no pages."));
            if(source!=null)source.Dispose();
            source=input;document=next;cachedPath=file.FullName;length=size;modified=stamp;
            return next;
        }catch{input.Dispose();throw;}
    }
    public static int Count(string path){lock(gate)return checked((int)Open(path).PageCount);}
    internal static Bitmap FirstPageThumbnail(Stream sourceStream,int side){
        using(var input=sourceStream.AsRandomAccessStream()){
            var pdf=PdfDocument.LoadFromStreamAsync(input).AsTask().GetAwaiter().GetResult();
            return RenderPage(pdf,0,side);
        }
    }
    public static Bitmap Render(string path,int pageIndex,int maxSide){
        int count;
        return Render(path,pageIndex,maxSide,out count);
    }
    public static Bitmap Render(string path,int pageIndex,int maxSide,out int count){
        lock(gate){
            var pdf=Open(path);count=checked((int)pdf.PageCount);
            int index=pageIndex<0?count-1:Math.Min(pageIndex,count-1);
            return RenderPage(pdf,index,maxSide);
        }
    }
    static Bitmap RenderPage(PdfDocument pdf,int index,int maxSide){
        using(var page=pdf.GetPage((uint)index))
        using(var output=new InMemoryRandomAccessStream()){
            double w=page.Size.Width,h=page.Size.Height;
            double scale=Math.Min(4,maxSide/Math.Max(w,h));
            var options=new PdfPageRenderOptions{DestinationWidth=(uint)Math.Max(1,Math.Round(w*scale)),DestinationHeight=(uint)Math.Max(1,Math.Round(h*scale))};
            page.RenderToStreamAsync(output,options).AsTask().GetAwaiter().GetResult();
            output.Seek(0);
            using(var stream=output.AsStreamForRead())
            using(var decoded=Image.FromStream(stream)){
                return new Bitmap(decoded);
            }
        }
    }
}
}
