using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LumaView {
static class ExplorerThumbnail {
    const string ClassId="{4B64E150-E9B5-409A-A8F7-02D07D6D1BDD}";
    const string ClassKey=@"Software\Classes\CLSID\"+ClassId;
    const string Handler=@"\ShellEx\{E357FCCD-A995-4576-B01F-234630154E96}";
    static readonly string[] Extensions={".exr",".psd",".hdr",".tga"};
    static string Library{get{return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"DollView.Thumbnail.dll");}}
    static string[] Slots(){
        return Extensions.SelectMany(e=>new[]{e,@"SystemFileAssociations\"+e}).ToArray();
    }
    static string SlotKey(string slot){return @"Software\Classes\"+slot+Handler;}
    static string Read(string key){
        using(var value=Registry.CurrentUser.OpenSubKey(key))return value==null?null:value.GetValue("") as string;
    }
    static bool Same(string left,string right){return String.Equals(left,right,StringComparison.OrdinalIgnoreCase);}
    public static bool Enabled{get{return Same(Read(ClassKey+@"\InProcServer32"),Library)&&Slots().All(s=>Same(Read(SlotKey(s)),ClassId));}}

    [DllImport("shell32.dll")] static extern void SHChangeNotify(uint eventId,uint flags,IntPtr first,IntPtr second);
    public static void SetEnabled(bool enable){
        using(var guard=new Mutex(false,@"Local\DollView.ExplorerThumbnailRegistration")){
            try{if(!guard.WaitOne(TimeSpan.FromSeconds(10)))throw new IOException("DollView thumbnail registration is busy.");}
            catch(AbandonedMutexException){}
            try{
                if(enable){
                    if(!File.Exists(Library))throw new FileNotFoundException("DollView.Thumbnail.dll",Library);
                    string former=Read(ClassKey+@"\InProcServer32");
                    using(var server=Registry.CurrentUser.CreateSubKey(ClassKey+@"\InProcServer32")){
                        server.SetValue("",Library);server.SetValue("ThreadingModel","Apartment");
                    }
                    try{
                        using(var previous=Registry.CurrentUser.CreateSubKey(ClassKey+@"\Previous")){
                            foreach(string slot in Slots()){
                                using(var key=Registry.CurrentUser.CreateSubKey(SlotKey(slot))){
                                    object value=key.GetValue("",null,RegistryValueOptions.DoNotExpandEnvironmentNames);
                                    if(Same(value as string,ClassId))continue;
                                    string entry=slot.Replace('\\','/');
                                    if(value==null)previous.DeleteValue(entry,false);
                                    else previous.SetValue(entry,value,key.GetValueKind(""));
                                    key.SetValue("",ClassId);
                                }
                            }
                        }
                    }catch{
                        if(former==null)Unregister();
                        else using(var server=Registry.CurrentUser.CreateSubKey(ClassKey+@"\InProcServer32"))server.SetValue("",former);
                        throw;
                    }
                }else Unregister();
            }finally{guard.ReleaseMutex();SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero);}
        }
    }
    static void Unregister(){
        if(!Same(Read(ClassKey+@"\InProcServer32"),Library))return;
        using(var previous=Registry.CurrentUser.OpenSubKey(ClassKey+@"\Previous")){
            foreach(string slot in Slots()){
                string path=SlotKey(slot);
                using(var key=Registry.CurrentUser.OpenSubKey(path,true)){
                    if(key==null||!Same(key.GetValue("") as string,ClassId))continue;
                    string entry=slot.Replace('\\','/');
                    object saved=previous==null?null:previous.GetValue(entry,null,RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if(saved!=null){key.SetValue("",saved,previous.GetValueKind(entry));continue;}
                    key.DeleteValue("",false);
                    if(key.ValueCount!=0||key.SubKeyCount!=0)continue;
                }
                Registry.CurrentUser.DeleteSubKey(path,false);
            }
        }
        Registry.CurrentUser.DeleteSubKeyTree(ClassKey,false);
    }

    // Explorer supplies a seekable IStream. Read only the ranges the original decoder asks for.
    sealed class ShellStream : Stream {
        readonly BinaryReader input;
        readonly BinaryWriter output;
        readonly long length;
        long position;
        internal ShellStream(BinaryReader input,BinaryWriter output,long length){this.input=input;this.output=output;this.length=length;}
        public override bool CanRead{get{return true;}}
        public override bool CanSeek{get{return true;}}
        public override bool CanWrite{get{return false;}}
        public override long Length{get{return length;}}
        public override long Position{get{return position;}set{Seek(value,SeekOrigin.Begin);}}
        public override long Seek(long offset,SeekOrigin origin){
            long p=checked((origin==SeekOrigin.Current?position:origin==SeekOrigin.End?length:0)+offset);
            if(p<0)throw new IOException("Invalid thumbnail stream offset.");
            return position=p;
        }
        public override int Read(byte[] buffer,int offset,int count){
            if(position>=length||count==0)return 0;
            int request=(int)Math.Min(Math.Min(count,128*1024),length-position);
            output.Write((byte)'R');output.Write(position);output.Write(request);output.Flush();
            int bytes=input.ReadInt32();
            if(bytes<0||bytes>request)throw new IOException("Invalid thumbnail stream response.");
            for(int i=0;i<bytes;){
                int n=input.Read(buffer,offset+i,bytes-i);if(n==0)throw new EndOfStreamException();i+=n;
            }
            position+=bytes;return bytes;
        }
        public override void Flush(){}
        public override void SetLength(long value){throw new NotSupportedException();}
        public override void Write(byte[] buffer,int offset,int count){throw new NotSupportedException();}
    }

    // This mode runs in its own process, with no viewer window or settings initialization.
    internal static int Decode(string size){
        try{
            int side;if(!Int32.TryParse(size,out side)||side<1||side>1024)return 1;
            using(var input=new BinaryReader(Console.OpenStandardInput()))
            using(var output=new BinaryWriter(Console.OpenStandardOutput())){
                long length=input.ReadInt64();int chars=input.ReadInt32();
                if(length<4||chars<0||chars>260)return 1;
                string name=Encoding.Unicode.GetString(input.ReadBytes(chars*2));
                Func<Stream> open=()=>new BufferedStream(new ShellStream(input,output,length),64*1024);
                string ext;
                using(var source=open())using(var reader=new BinaryReader(source)){
                    uint signature=reader.ReadUInt32();
                    ext=signature==20000630?".exr":signature==0x53504238?".psd":
                        (signature&0xffff)==0x3f23?".hdr":".tga";
                }
                string hint=Path.GetFileNameWithoutExtension(name)+ext;
                using(var bitmap=(ext==".psd"?PsdDecoder.TryDecodeThumbnail(open):null)??ExtendedDecoder.DecodeSource(hint,side,side,open)){
                    double factor=Math.Min(1,Math.Min((double)side/bitmap.Width,(double)side/bitmap.Height));
                    int width=Math.Max(1,(int)Math.Round(bitmap.Width*factor)),height=Math.Max(1,(int)Math.Round(bitmap.Height*factor));
                    using(var image=new Bitmap(width,height,PixelFormat.Format32bppPArgb)){
                        using(var g=Graphics.FromImage(image)){
                            g.CompositingMode=CompositingMode.SourceCopy;g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                            g.DrawImage(bitmap,new Rectangle(0,0,width,height),0,0,bitmap.Width,bitmap.Height,GraphicsUnit.Pixel);
                        }
                        var data=image.LockBits(new Rectangle(0,0,width,height),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
                        try{
                            output.Write((byte)'B');output.Write(width);output.Write(height);
                            var row=new byte[width*4];
                            for(int y=0;y<height;y++){
                                Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),row,0,row.Length);
                                output.Write(row);
                            }
                        }finally{image.UnlockBits(data);}
                    }
                }
            }
            return 0;
        }catch(Exception){return 1;}
    }
}
}
