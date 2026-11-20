using System.Linq;
using System;using System.IO;using System.Diagnostics;using System.Threading;using LumaView;
class PackagedUpdateCheck {
 static void Wait(Func<bool> ready,string message){var watch=Stopwatch.StartNew();while(!ready()&&watch.Elapsed<TimeSpan.FromSeconds(90))Thread.Sleep(100);if(!ready())throw new Exception(message);}
 static int Main(string[] args){Process original=null;try{
  string target=Path.GetFullPath(args[0]),zip=Path.GetFullPath(args[1]),work=Path.GetFullPath(args[2]),helper=Path.Combine(work,"DollView.Updater.exe");Directory.CreateDirectory(work);
  string installedName=File.Exists(Path.Combine(target,"DollView.exe"))?"DollView":"LumaView";
  string executable=Path.Combine(target,installedName+".exe");
  string pictures=Path.Combine(target,"test pictures");Directory.CreateDirectory(pictures);
  var files=UpdateFiles.Extract(zip,Path.Combine(work,"payload"),args[3]);
  File.Copy(Path.Combine(target,installedName+".Updater.exe"),helper);
  original=Process.Start(new ProcessStartInfo(executable,UpdateFiles.Quote(pictures)){UseShellExecute=false});
  Wait(()=>{original.Refresh();return original.MainWindowHandle!=IntPtr.Zero;},"Previous app window did not open");
  string previous=FileVersionInfo.GetVersionInfo(executable).FileVersion;previous=previous.Substring(0,previous.LastIndexOf('.'));
  var job=new UpdateJob{Id=Guid.NewGuid().ToString("N"),TargetRoot=target,WorkRoot=work,FromVersion=previous,ToVersion=args[3],RestartPath=pictures,Language="en",ParentPid=original.Id,ParentStarted=original.StartTime.ToUniversalTime().Ticks,Files=files};UpdateFiles.SaveJob(job);
  using(var worker=Process.Start(new ProcessStartInfo(helper,UpdateFiles.Quote(Path.Combine(work,"job.json"))){UseShellExecute=false})){
   Wait(()=>File.Exists(Path.Combine(work,"ready"))||worker.HasExited,"Installer did not get ready");if(worker.HasExited)throw new Exception(File.ReadAllText(Path.Combine(work,"worker-error.txt")));
   original.CloseMainWindow();if(!original.WaitForExit(10000))throw new Exception("Previous app did not close");
   Wait(()=>File.Exists(Path.Combine(work,"result.json"))||worker.HasExited,"No update result");
   string result=File.ReadAllText(Path.Combine(work,"result.json"));if(!result.Contains("\"success\""))throw new Exception(result);
   if(!worker.WaitForExit(10000))throw new Exception("Installer did not finish");
   string renamed=Path.Combine(target,"DollView.exe");
   var version=FileVersionInfo.GetVersionInfo(renamed);
   if(version.ProductName!="DollView"||version.FileVersion!=args[3]+".0")throw new Exception("Renamed executable version mismatch");
   if(!Process.GetProcessesByName("DollView").Any(p=>{using(p){try{return String.Equals(p.MainModule.FileName,renamed,StringComparison.OrdinalIgnoreCase);}catch(System.ComponentModel.Win32Exception){return false;}catch(InvalidOperationException){return false;}}}))throw new Exception("DollView process did not start");
   Wait(()=>!File.Exists(Path.Combine(target,"LumaView.exe"))&&!File.Exists(Path.Combine(target,"LumaView.Updater.exe")),"Legacy executables were not removed after migration");
   Console.WriteLine("PASS "+installedName+" updater: "+previous+" -> "+args[3]+" in the same directory, DollView.exe launched and acknowledged");Console.WriteLine(result);
  }
  return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{
  if(original!=null){if(!original.HasExited){original.CloseMainWindow();if(!original.WaitForExit(3000))original.Kill();}original.Dispose();}
  foreach(var process in Process.GetProcessesByName("DollView").Concat(Process.GetProcessesByName("LumaView")))using(process){try{if(String.Equals(process.MainModule.FileName,Path.Combine(Path.GetFullPath(args[0]),process.ProcessName+".exe"),StringComparison.OrdinalIgnoreCase)){process.CloseMainWindow();if(!process.WaitForExit(3000))process.Kill();}}catch(InvalidOperationException){}}
 }}
}
