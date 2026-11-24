using System;
using System.Collections.Generic;
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
    const string Handler=@"\ShellEx\{E357FCCD-A995-4576-B01F-234630154E96}";
    static string Library{get{return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"DollView.Thumbnail.dll");}}
    static string SlotKey(string slot){return @"Software\Classes\"+slot+Handler;}
    static string Read(string key){
        using(var value=Registry.CurrentUser.OpenSubKey(key))return value==null?null:value.GetValue("") as string;
    }
    static bool Same(string left,string right){return String.Equals(left,right,StringComparison.OrdinalIgnoreCase);}

    // Roll back all provider and preference changes together if a registration fails.
    sealed class RegistryChanges {
        readonly Stack<Action> undo=new Stack<Action>();
        readonly HashSet<string> emptyKeys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public void Prune(string path){emptyKeys.Add(path);}
        public void Write(string path,string name,object value,RegistryValueKind kind){
            using(var key=value==null?Registry.CurrentUser.OpenSubKey(path,true):Registry.CurrentUser.CreateSubKey(path)){
                if(key==null)return;
                var old=key.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames);
                if(old==null&&value==null)return;
                var oldKind=old==null?RegistryValueKind.Unknown:key.GetValueKind(name);
                if(Object.Equals(old,value)&&oldKind==kind)return;
                Prune(path);
                if(value==null)key.DeleteValue(name,false);else key.SetValue(name,value,kind);
                undo.Push(delegate{
                    using(var restore=Registry.CurrentUser.CreateSubKey(path)){
                        if(old==null)restore.DeleteValue(name,false);else restore.SetValue(name,old,oldKind);
                    }
                });
            }
        }
        public void Rollback(){while(undo.Count>0)undo.Pop()();}
        public void Clean(){
            foreach(string path in emptyKeys.OrderByDescending(p=>p.Length)){
                using(var key=Registry.CurrentUser.OpenSubKey(path)){
                    if(key==null||key.ValueCount!=0||key.SubKeyCount!=0)continue;
                }
                Registry.CurrentUser.DeleteSubKey(path,false);
            }
        }
    }
    sealed class Registration {
        readonly string id,keyPath;
        readonly bool needsFile;
        readonly string[] slots;
        public Registration(string id,bool needsFile,params string[] extensions){
            this.id=id;this.needsFile=needsFile;keyPath=@"Software\Classes\CLSID\"+id;
            slots=extensions.SelectMany(e=>new[]{e,@"SystemFileAssociations\"+e}).ToArray();
        }
        public bool Enabled{get{return Same(Read(keyPath+@"\InProcServer32"),Library)&&slots.All(s=>Same(Read(SlotKey(s)),id));}}
        public bool RegisteredElsewhere{get{string server=Read(keyPath+@"\InProcServer32");return server!=null&&!Same(server,Library);}}
        public void Apply(bool enable,RegistryChanges edits){
            string server=keyPath+@"\InProcServer32",history=keyPath+@"\Previous";
            if(!enable&&!Same(Read(server),Library))return;
            edits.Prune(keyPath);
            edits.Write(server,"",enable?Library:null,RegistryValueKind.String);
            edits.Write(server,"ThreadingModel",enable?"Apartment":null,RegistryValueKind.String);
            // A glTF can keep its meshes in a sibling .bin. Only its shell adapter needs
            // the item path; the parser and rasterizer always run in the child job.
            if(needsFile)edits.Write(keyPath,"DisableProcessIsolation",enable?(object)1:null,RegistryValueKind.DWord);
            foreach(string slot in slots){
                string path=SlotKey(slot),entry=slot.Replace('\\','/');
                using(var key=Registry.CurrentUser.OpenSubKey(path)){
                    var value=key==null?null:key.GetValue("",null,RegistryValueOptions.DoNotExpandEnvironmentNames);
                    bool owned=Same(value as string,id);
                    if(enable){
                        if(owned)continue;
                        edits.Write(history,entry,value,value==null?RegistryValueKind.String:key.GetValueKind(""));
                        edits.Write(path,"",id,RegistryValueKind.String);
                    }else{
                        using(var previous=Registry.CurrentUser.OpenSubKey(history)){
                            var old=previous==null?null:previous.GetValue(entry,null,RegistryValueOptions.DoNotExpandEnvironmentNames);
                            if(owned)edits.Write(path,"",old,old==null?RegistryValueKind.String:previous.GetValueKind(entry));
                            edits.Write(history,entry,null,RegistryValueKind.String);
                        }
                    }
                }
            }
            edits.Prune(history);
        }
    }
    static readonly Registration images=new Registration("{4B64E150-E9B5-409A-A8F7-02D07D6D1BDD}",false,".exr",".psd",".hdr",".tga");
    static readonly Registration pdfPages=new Registration("{4B64E153-E9B5-409A-A8F7-02D07D6D1BDD}",false,".pdf");
    static readonly Registration models=new Registration("{4B64E151-E9B5-409A-A8F7-02D07D6D1BDD}",false,".obj",".fbx",".stl",".glb");
    static readonly Registration gltf=new Registration("{4B64E152-E9B5-409A-A8F7-02D07D6D1BDD}",true,".gltf");
    static readonly Registration[] all={images,pdfPages,models,gltf};
    internal static CheckState CombinedState{get{
        int enabled=all.Count(registration=>registration.Enabled);
        return enabled==all.Length?CheckState.Checked:enabled==0?CheckState.Unchecked:CheckState.Indeterminate;
    }}
    const string SettingsKey=@"Software\LumaView\ExplorerThumbnails";
    const string PdfPreference="PdfExplorerThumbnails";
    public static void SetPdfEnabled(bool enable){
        ChangeRegistrations(enable,edits=>ApplyPdf(enable,edits));
    }
    static void ApplyPdf(bool enable,RegistryChanges edits){
        if(!enable&&pdfPages.RegisteredElsewhere)return;
        pdfPages.Apply(enable,edits);
        edits.Write(SettingsKey,PdfPreference,enable?1:0,RegistryValueKind.DWord);
    }
    internal static void UnregisterPdf(){SetRegistrations(false,pdfPages);}
    static bool ShouldAddPdf(){
        if(!images.Enabled||Registry.GetValue(@"HKEY_CURRENT_USER\"+SettingsKey,PdfPreference,null)!=null)return false;
        foreach(string slot in new[]{".pdf",@"SystemFileAssociations\.pdf"}){
            using(var key=Registry.ClassesRoot.OpenSubKey(slot+Handler))if(key!=null&&key.GetValue("")!=null)return false;
        }
        return true;
    }
    internal static void UpgradePdfThumbnails(){
        if(!ShouldAddPdf())return;
        ChangeRegistrations(true,delegate(RegistryChanges edits){
            if(!ShouldAddPdf())return;
            pdfPages.Apply(true,edits);
            edits.Write(SettingsKey,PdfPreference,1,RegistryValueKind.DWord);
        });
    }
    public static void SetEnabled(bool enable){SetRegistrations(enable,images);}
    static void CheckModelRenderer(bool enable){
        if(enable&&!File.Exists(ModelFiles.ThumbnailRenderer))throw new FileNotFoundException("DollView.Convert.exe",ModelFiles.ThumbnailRenderer);
    }
    public static void SetModelsEnabled(bool enable){
        CheckModelRenderer(enable);
        SetRegistrations(enable,models,gltf);
    }
    internal static void SetAllEnabled(bool enable){
        CheckModelRenderer(enable);
        ChangeRegistrations(enable,delegate(RegistryChanges edits){
            images.Apply(enable,edits);
            ApplyPdf(enable,edits);
            models.Apply(enable,edits);
            gltf.Apply(enable,edits);
        });
    }
    internal static void UnregisterAll(){SetRegistrations(false,all);}
    [DllImport("shell32.dll")] static extern void SHChangeNotify(uint eventId,uint flags,IntPtr first,IntPtr second);
    static void SetRegistrations(bool enable,params Registration[] registrations){
        ChangeRegistrations(enable,delegate(RegistryChanges edits){foreach(var registration in registrations)registration.Apply(enable,edits);});
    }
    static void ChangeRegistrations(bool enable,Action<RegistryChanges> change){
        if(enable&&!File.Exists(Library))throw new FileNotFoundException("DollView.Thumbnail.dll",Library);
        using(var guard=new Mutex(false,@"Local\DollView.ExplorerThumbnailRegistration")){
            try{if(!guard.WaitOne(TimeSpan.FromSeconds(10)))throw new IOException("DollView thumbnail registration is busy.");}
            catch(AbandonedMutexException){}
            try{
                var edits=new RegistryChanges();
                try{change(edits);}
                catch{edits.Rollback();throw;}
                finally{edits.Clean();}
            }finally{guard.ReleaseMutex();SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero);}
        }
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
    static Bitmap DecodeImage(Func<Stream> open,string name,int side){
        string ext;
        using(var source=open())using(var reader=new BinaryReader(source)){
            uint signature=reader.ReadUInt32();
            ext=signature==20000630?".exr":signature==0x53504238?".psd":(signature&0xffff)==0x3f23?".hdr":".tga";
        }
        string hint=Path.GetFileNameWithoutExtension(name)+ext;
        return (ext==".psd"?PsdDecoder.TryDecodeThumbnail(open):null)??ExtendedDecoder.DecodeSource(hint,side,side,open);
    }
    internal static int Decode(string size,bool pdf){
        try{
            int side;if(!Int32.TryParse(size,out side)||side<1||side>1024)return 1;
            using(var input=new BinaryReader(Console.OpenStandardInput()))
            using(var output=new BinaryWriter(Console.OpenStandardOutput())){
                long length=input.ReadInt64();int chars=input.ReadInt32();
                if(length<4||chars<0||chars>260)return 1;
                string name=Encoding.Unicode.GetString(input.ReadBytes(chars*2));
                Func<Stream> open=()=>new BufferedStream(new ShellStream(input,output,length),64*1024);
                using(var pdfStream=pdf?open():null)
                using(var bitmap=pdf?PdfPages.FirstPageThumbnail(pdfStream,side):DecodeImage(open,name,side)){
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
