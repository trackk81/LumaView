using System;
using System.IO;
using System.Drawing;
using System.Diagnostics;
using System.Threading.Tasks;

namespace LumaView {
static class ExtendedDecoder {
    public static readonly string[] Extensions={".exr",".psd",".psb",".webp",".tga",".hdr",".dpx",".dds"};
    public static Bitmap Decode(string path,int maxWidth=0,int maxHeight=0) {
        string ext=Path.GetExtension(path).ToLowerInvariant();
        if(ext==".exr"){
            var aov=ExrAov.TryDecode(path,maxWidth,maxHeight);
            if(aov!=null)return aov;
        }
        bool isPsd=ext==".psd"||ext==".psb";
        bool missingMergedData=false;
        if(isPsd){
            var psd=PsdDecoder.TryDecode(path,maxWidth,maxHeight,out missingMergedData);
            if(psd!=null)return psd;
        }
        string executable=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"codecs","magick.exe");
        if(!File.Exists(executable)){
            if(isPsd){
                var thumb=PsdDecoder.TryDecodeThumbnail(path);
                if(thumb!=null)return thumb;
            }
            throw new FileNotFoundException("codecs 폴더를 실행 파일 옆에 두세요. 확장 포맷 디코더가 없습니다.");
        }
        string coder=ext.TrimStart('.').ToUpperInvariant();
        string resize=maxWidth>0&&maxHeight>0
            ?"-resize "+maxWidth+"x"+maxHeight+"> "
            :"-resize \""+PsdDecoder.MaxDisplayPixels+"@>\" -resize \""+PsdDecoder.MaxDisplaySide+"x"+PsdDecoder.MaxDisplaySide+">\" ";
        Size? psdOrig=isPsd?PsdDecoder.TryReadSize(path):null;
        try{
            Bitmap bmp=null;
            if(isPsd&&missingMergedData){
                try{bmp=RunMagick(executable,path,"\""+coder+":-\" -delete 0 -layers merge -auto-orient -colorspace sRGB -depth 8 "+resize+"PNG32:-");}
                catch{}
            }
            if(bmp==null)bmp=RunMagick(executable,path,"\""+coder+":-[0]\" -auto-orient -colorspace sRGB -depth 8 "+resize+"PNG32:-");
            if(psdOrig.HasValue&&(psdOrig.Value.Width!=bmp.Width||psdOrig.Value.Height!=bmp.Height))Album.SetOriginalSize(bmp,psdOrig.Value);
            return bmp;
        }catch{
            if(isPsd){
                var thumb=PsdDecoder.TryDecodeThumbnail(path);
                if(thumb!=null)return thumb;
            }
            throw;
        }
    }
    static Bitmap RunMagick(string executable,string path,string operation){
        var start=new ProcessStartInfo(executable) {
            Arguments="-limit memory 1GiB -limit map 2GiB -limit disk 4GiB -limit width 300000 -limit height 300000 -limit time 60 "+operation,
            WorkingDirectory=Path.GetDirectoryName(executable),UseShellExecute=false,CreateNoWindow=true,
            RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true
        };
        using(var process=Process.Start(start))
        using(var output=new MemoryStream()) {
            var feed=Task.Run(async delegate {
                using(var source=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){
                    try{await source.CopyToAsync(process.StandardInput.BaseStream);}
                    finally{try{process.StandardInput.Close();}catch(IOException){}}
                }
            });
            var errors=process.StandardError.ReadToEndAsync();
            var copy=process.StandardOutput.BaseStream.CopyToAsync(output);
            if(!process.WaitForExit(65000)) { process.Kill();process.WaitForExit();try{copy.Wait();}catch{} throw new TimeoutException("이미지 디코딩 시간이 초과되었습니다 (65초)."); }
            copy.GetAwaiter().GetResult();
            try{feed.GetAwaiter().GetResult();}catch(IOException){if(process.ExitCode!=0||output.Length==0){}}
            string error=errors.GetAwaiter().GetResult();
            if(process.ExitCode!=0 || output.Length==0) throw new InvalidDataException("이 파일의 미리보기를 만들 수 없습니다.\n"+error.Substring(0,Math.Min(error.Length,1200)));
            output.Position=0;
            using(var decoded=Image.FromStream(output,true,true)) {
                return new Bitmap(decoded);
            }
        }
    }
}
}
