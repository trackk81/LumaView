using System;using System.IO;using System.Diagnostics;using System.Threading;using LumaView;
class PackagedUpdateCheck {
 static void Wait(Func<bool> ready,string message){var watch=Stopwatch.StartNew();while(!ready()&&watch.Elapsed<TimeSpan.FromSeconds(90))Thread.Sleep(100);if(!ready())throw new Exception(message);}
 static int Main(string[] args){Process original=null;try{
  string target=Path.GetFullPath(args[0]),zip=Path.GetFullPath(args[1]),work=Path.GetFullPath(args[2]),helper=Path.Combine(work,"LumaView.Updater.exe");Directory.CreateDirectory(work);
  string pictures=Path.Combine(target,"test pictures");Directory.CreateDirectory(pictures);
  var files=UpdateFiles.Extract(zip,Path.Combine(work,"payload"),args[3]);File.Copy(Path.Combine(work,"payload","LumaView.Updater.exe"),helper);
  original=Process.Start(new ProcessStartInfo(Path.Combine(target,"LumaView.exe"),UpdateFiles.Quote(pictures)){UseShellExecute=false});
  Wait(()=>{original.Refresh();return original.MainWindowHandle!=IntPtr.Zero;},"Previous app window did not open");
  string previous=FileVersionInfo.GetVersionInfo(Path.Combine(target,"LumaView.exe")).FileVersion;previous=previous.Substring(0,previous.LastIndexOf('.'));
  var job=new UpdateJob{Id=Guid.NewGuid().ToString("N"),TargetRoot=target,WorkRoot=work,FromVersion=previous,ToVersion=args[3],RestartPath=pictures,Language="en",ParentPid=original.Id,ParentStarted=original.StartTime.ToUniversalTime().Ticks,Files=files};UpdateFiles.SaveJob(job);
  using(var worker=Process.Start(new ProcessStartInfo(helper,UpdateFiles.Quote(Path.Combine(work,"job.json"))){UseShellExecute=false})){
   Wait(()=>File.Exists(Path.Combine(work,"ready"))||worker.HasExited,"Installer did not get ready");if(worker.HasExited)throw new Exception(File.ReadAllText(Path.Combine(work,"worker-error.txt")));
   original.CloseMainWindow();if(!original.WaitForExit(10000))throw new Exception("Previous app did not close");
   Wait(()=>File.Exists(Path.Combine(work,"result.json"))||worker.HasExited,"No update result");
   string result=File.ReadAllText(Path.Combine(work,"result.json"));if(!result.Contains("\"success\""))throw new Exception(result);
   if(!worker.WaitForExit(10000))throw new Exception("Installer did not finish");
   Console.WriteLine("PASS packaged application: "+previous+" -> "+args[3]+" in the same directory, old process closed, new window acknowledged");Console.WriteLine(result);
  }
  return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{
  if(original!=null){if(!original.HasExited){original.CloseMainWindow();if(!original.WaitForExit(3000))original.Kill();}original.Dispose();}
  foreach(var process in Process.GetProcessesByName("LumaView"))using(process){try{if(String.Equals(process.MainModule.FileName,Path.Combine(Path.GetFullPath(args[0]),"LumaView.exe"),StringComparison.OrdinalIgnoreCase)){process.CloseMainWindow();if(!process.WaitForExit(3000))process.Kill();}}catch(InvalidOperationException){}}
 }}
}
