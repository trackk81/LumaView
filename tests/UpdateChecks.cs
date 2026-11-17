using System;using System.IO;using System.IO.Compression;using System.Linq;using System.Collections.Generic;using System.Diagnostics;using System.Threading;using System.Runtime.InteropServices;using LumaView;
class UpdateChecks {
 static string root,fixtures,worker;static int count;
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);count++;Console.WriteLine("PASS "+text);}
 static void Reject(Action action,string text){try{action();}catch(InvalidDataException){Check(true,text);return;}throw new Exception(text);}
 static string Fresh(string name){string path=Path.Combine(root,name+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;}
 static void Put(string path,string text){Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,text);}
 static UpdateJob Prepare(bool fail){
  string target=Fresh("app with spaces 한글"),work=Fresh("update cache 한글"),payload=Path.Combine(work,"payload-source");Directory.CreateDirectory(payload);
  File.Copy(Path.Combine(fixtures,"old.exe"),Path.Combine(target,"LumaView.exe"));Put(Path.Combine(target,"VERSION"),"1.0.0");Put(Path.Combine(target,"README.md"),"old readme");Put(Path.Combine(target,"user data","keep.txt"),"untouched");
  File.Copy(Path.Combine(fixtures,fail?"fail.exe":"new.exe"),Path.Combine(payload,"LumaView.exe"));
  foreach(string name in new[]{"LumaView.Updater.exe","Microsoft.Web.WebView2.Core.dll","Microsoft.Web.WebView2.WinForms.dll","WebView2Loader.dll","video-runtime/mpv.exe","codecs/magick.exe","model-viewer/index.html","README.md","new-component.txt"})Put(Path.Combine(payload,name),"new "+name);
  Put(Path.Combine(payload,"VERSION"),"1.0.1");string zip=Path.Combine(work,"update.zip");ZipFile.CreateFromDirectory(payload,zip);
  var files=UpdateFiles.Extract(zip,Path.Combine(work,"payload"),"1.0.1");
  var job=new UpdateJob{Id=Guid.NewGuid().ToString("N"),TargetRoot=target,WorkRoot=work,FromVersion="1.0.0",ToVersion="1.0.1",Language="en",RestartPath=Path.Combine(target,"user data"),Files=files};UpdateFiles.ValidateJob(job);return job;
 }
 static Dictionary<string,string> Snapshot(string target){return Directory.GetFiles(target,"*",SearchOption.AllDirectories).ToDictionary(p=>p.Substring(target.Length),UpdateFiles.Hash,StringComparer.OrdinalIgnoreCase);}
 static bool Same(string target,Dictionary<string,string> before){var after=Snapshot(target);return before.Count==after.Count&&before.All(p=>after.ContainsKey(p.Key)&&after[p.Key]==p.Value);}
 static void Archives(){
  foreach(string path in new[]{"../escape","a/../../escape","/absolute","C:/escape","a//b","a/./b","a/CON.txt","file:stream","NUL","tail.","tail ","\\server\\share","a/../b"})Reject(()=>UpdateFiles.Inside(root,path),"reject path "+path);
  Check(UpdateFiles.Quote("C:\\space here\\")=="\"C:\\space here\\\\\"","quote trailing slash in restart path");
  foreach(string mode in new[]{"traversal","duplicate","symlink"}){
   string work=Fresh(mode),zip=Path.Combine(work,"bad.zip"),dest=Path.Combine(work,"payload");
   using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create)){
    using(var output=new StreamWriter(archive.CreateEntry("first.txt").Open()))output.Write("safe");
    var entry=archive.CreateEntry(mode=="traversal"?"../escaped.txt":mode=="duplicate"?"FIRST.TXT":"link");if(mode=="symlink")entry.ExternalAttributes=unchecked((int)0xa1ff0000);
    using(var output=new StreamWriter(entry.Open()))output.Write("bad");
   }
   Reject(()=>UpdateFiles.Extract(zip,dest,"1.0.1"),"reject "+mode+" archive");Check(!File.Exists(Path.Combine(dest,"first.txt")),"validate entire "+mode+" archive before extraction");
  }
  var corrupt=Prepare(false);Put(Path.Combine(corrupt.WorkRoot,"payload","README.md"),"tampered");Reject(()=>UpdateFiles.ValidateJob(corrupt),"reject staged payload tampering");
  var mismatch=Prepare(false);mismatch.FromVersion="0.9.0";Reject(()=>UpdateFiles.ValidateJob(mismatch),"reject changed installed version");
  var downgrade=Prepare(false);downgrade.ToVersion="1.0.0";Reject(()=>UpdateFiles.ValidateJob(downgrade),"reject wrong payload version");
 }
 static void Transactions(){
  var job=Prepare(false);var transaction=new UpdateTransaction(job);transaction.Backup();transaction.Install(name=>{});
  Check(job.Files.All(f=>UpdateFiles.Hash(UpdateFiles.Inside(job.TargetRoot,f.Name))==f.Hash),"replace all application files in the same directory");Check(File.ReadAllText(Path.Combine(job.TargetRoot,"user data","keep.txt"))=="untouched","preserve unrelated user files");
  var rollback=Prepare(false);var before=Snapshot(rollback.TargetRoot);var change=new UpdateTransaction(rollback);change.Backup();bool interrupted=false;
  try{change.Install(name=>{if(name=="LumaView.exe")throw new IOException("injected failure before final executable");});}catch(IOException){interrupted=true;change.Rollback();}
  Check(interrupted&&Same(rollback.TargetRoot,before),"restore replaced files and remove new files after partial failure");
  var locked=Prepare(false);var original=Snapshot(locked.TargetRoot);var blocked=new UpdateTransaction(locked);blocked.Backup();bool failed=false;
  using(var file=new FileStream(Path.Combine(locked.TargetRoot,"VERSION"),FileMode.Open,FileAccess.Read,FileShare.Read)){
   try{blocked.Install(name=>{});}catch(IOException){failed=true;blocked.Rollback();}
  }
  Check(failed&&Same(locked.TargetRoot,original),"locked file failure rolls back without touching the locked original");
 }
 delegate bool EnumProc(IntPtr window,IntPtr data);
 [DllImport("user32.dll")]static extern bool EnumWindows(EnumProc callback,IntPtr data);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
 [DllImport("user32.dll")]static extern bool PostMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
 static void CloseTestWindows(int pid){EnumWindows((window,data)=>{uint owner;GetWindowThreadProcessId(window,out owner);if(owner==pid)PostMessage(window,0x10,IntPtr.Zero,IntPtr.Zero);return true;},IntPtr.Zero);}
 static void Worker(bool fail){
  var job=Prepare(fail);UpdateFiles.SaveJob(job);string helper=Path.Combine(job.WorkRoot,"LumaView.Updater.exe");File.Copy(worker,helper);
  using(var process=Process.Start(new ProcessStartInfo(helper,UpdateFiles.Quote(Path.Combine(job.WorkRoot,"job.json"))){UseShellExecute=false,CreateNoWindow=true})){
   var watch=Stopwatch.StartNew();while(!process.HasExited&&watch.Elapsed<TimeSpan.FromSeconds(25)){
    if(fail&&File.Exists(Path.Combine(job.WorkRoot,"result.json")))CloseTestWindows(process.Id);Thread.Sleep(100);
   }
   if(!process.HasExited){process.Kill();throw new Exception("Test worker timeout: "+job.WorkRoot);}
   string result=File.Exists(Path.Combine(job.WorkRoot,"result.json"))?File.ReadAllText(Path.Combine(job.WorkRoot,"result.json")):File.ReadAllText(Path.Combine(job.WorkRoot,"worker-error.txt"));
   Check(result.Contains(fail?"rolled_back":"success"),"worker "+(fail?"rolls back failed launch":"applies update and receives restart acknowledgement")+": "+result);
   Check(FileVersionInfo.GetVersionInfo(Path.Combine(job.TargetRoot,"LumaView.exe")).FileVersion==(fail?"1.0.0.0":"1.0.1.0"),"worker leaves expected executable version");
   Check(File.Exists(Path.Combine(job.TargetRoot,fail?"old-restarted.txt":"new-started.txt")),"worker restarts "+(fail?"previous":"new")+" executable");
   Check(File.ReadAllText(Path.Combine(job.TargetRoot,"user data","keep.txt"))=="untouched","worker preserves user data");
  }
 }
 [STAThread]static int Main(string[] args){try{root=FreshRoot(args[0]);fixtures=args[1];worker=args[2];Archives();Transactions();Worker(false);Worker(true);Console.WriteLine("PASS "+count+" automatic update checks; evidence: "+root);return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 static string FreshRoot(string parent){string path=Path.Combine(parent,"update-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;}
}
