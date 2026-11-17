using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

// Optional network smoke test. Downloads and verifies the official installer;
// it never runs an installer or changes this PC's associations.
class ComponentDownloadCheck {
    static Type components;
    static Task Call(string method,params object[] args){return (Task)components.GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);}
    static int Main(string[] args){
        string folder=Path.Combine(Path.GetTempPath(),"LumaView-ComponentCheck-"+Guid.NewGuid().ToString("N"));
        string path=Path.Combine(folder,"WebView2.exe");
        try{
            components=Assembly.LoadFrom(Path.GetFullPath(args[0])).GetType("LumaView.ComponentSetup");
            Directory.CreateDirectory(folder);
            using(var cancel=new CancellationTokenSource(TimeSpan.FromMinutes(3))){
                Call("DownloadInstallerAsync",path,(Action<string>)(text=>{}),cancel.Token).GetAwaiter().GetResult();
                Console.WriteLine("Downloaded installer: "+new FileInfo(path).Length+" bytes");
                if(args.Length>1)File.Copy(path,args[1],true);
                Call("VerifyMicrosoftInstallerAsync",path,cancel.Token).GetAwaiter().GetResult();
                Console.WriteLine("PASS official WebView2 installer downloaded and Microsoft signature verified ("+new FileInfo(path).Length+" bytes)");
                File.WriteAllText(path,"This is not a signed installer.");
                try{Call("VerifyMicrosoftInstallerAsync",path,cancel.Token).GetAwaiter().GetResult();throw new Exception("Unsigned installer was accepted");}
                catch(InvalidDataException){Console.WriteLine("PASS invalid installer rejected before execution");}
            }
            return 0;
        }catch(Exception ex){Console.WriteLine(ex);return 1;}
        finally{if(File.Exists(path))File.Delete(path);if(Directory.Exists(folder))Directory.Delete(folder);}
    }
}
