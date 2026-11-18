using System;
using System.IO;
using System.Drawing;
using System.Diagnostics;
using System.Threading.Tasks;

namespace LumaView {
static class ExtendedDecoder {
    public static readonly string[] Extensions={".exr",".psd",".psb",".webp",".tga",".hdr",".dpx",".dds"};
    public static Bitmap Decode(string path,int maxWidth=0,int maxHeight=0) {
        if(String.Equals(Path.GetExtension(path),".exr",StringComparison.OrdinalIgnoreCase)){
            var aov=ExrAov.TryDecode(path,maxWidth,maxHeight);
            if(aov!=null)return aov;
        }
        string executable=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"codecs","magick.exe");
        if(!File.Exists(executable)) throw new FileNotFoundException("codecs 폴더를 실행 파일 옆에 두세요. 확장 포맷 디코더가 없습니다.");
        string coder=Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
            var start=new ProcessStartInfo(executable) {
                Arguments="-limit memory 512MiB -limit map 1GiB -limit disk 2GiB -limit width 32000 -limit height 32000 -limit time 60 \""+coder+":-[0]\" -auto-orient -colorspace sRGB -depth 8 "+(maxWidth>0&&maxHeight>0?"-resize "+maxWidth+"x"+maxHeight+"> ":"")+"PNG32:-",
                WorkingDirectory=Path.GetDirectoryName(executable),UseShellExecute=false,CreateNoWindow=true,
                RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true
            };
            using(var process=Process.Start(start))
            using(var output=new MemoryStream()) {
                var feed=Task.Run(async delegate { using(var source=File.OpenRead(path)){try{await source.CopyToAsync(process.StandardInput.BaseStream);}finally{process.StandardInput.Close();}} });
                var errors=process.StandardError.ReadToEndAsync();
                var copy=process.StandardOutput.BaseStream.CopyToAsync(output);
                if(!process.WaitForExit(65000)) { process.Kill();process.WaitForExit();try{copy.Wait();}catch{} throw new TimeoutException("이미지 디코딩 시간이 초과되었습니다 (65초)."); }
                copy.GetAwaiter().GetResult();
                try{feed.GetAwaiter().GetResult();}catch(IOException){if(process.ExitCode==0)throw;}
                string error=errors.GetAwaiter().GetResult();
                if(process.ExitCode!=0 || output.Length==0) throw new InvalidDataException("이 파일의 미리보기를 만들 수 없습니다.\n"+error.Substring(0,Math.Min(error.Length,1200)));
                output.Position=0;
                using(var decoded=Image.FromStream(output,true,true)) {
                    if((long)decoded.Width*decoded.Height>100000000) throw new InvalidDataException("최대 1억 픽셀까지 지원합니다.");
                    return new Bitmap(decoded);
                }
            }

    }
}
}

