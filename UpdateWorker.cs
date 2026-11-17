using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LumaView {
static class UpdateWorker {
    static string Say(UpdateJob job,string ko,string en){return job.Language=="en"?en:ko;}
    static bool Running(int pid,long started){try{using(var process=Process.GetProcessById(pid))return !process.HasExited&&process.StartTime.ToUniversalTime().Ticks==started;}catch(ArgumentException){return false;}}
    static async Task WaitForExit(UpdateJob job,Action<string> report){
        var watch=Stopwatch.StartNew();
        while(job.ParentPid>0&&Running(job.ParentPid,job.ParentStarted)){if(watch.Elapsed>TimeSpan.FromSeconds(60))throw new TimeoutException("LumaView did not close. No application files were changed.");await Task.Delay(100);}
        string target=UpdateFiles.Inside(job.TargetRoot,"LumaView.exe");
        foreach(var process in Process.GetProcessesByName("LumaView"))using(process){
            bool same=false;try{same=String.Equals(process.MainModule.FileName,target,StringComparison.OrdinalIgnoreCase);}catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}
            if(!same)continue;report(Say(job,"다른 LumaView 창을 닫는 중…","Closing other LumaView windows…"));process.CloseMainWindow();
            var wait=Stopwatch.StartNew();while(!process.HasExited){if(wait.Elapsed>TimeSpan.FromSeconds(30))throw new TimeoutException(Say(job,"열려 있는 다른 LumaView 창을 닫은 뒤 업데이트를 다시 시도하세요.","Close other LumaView windows and retry the update."));await Task.Delay(100);}
        }
    }
    static Process StartApp(UpdateJob job,bool acknowledge){
        string arguments=acknowledge?"--update-complete "+job.Id:"";
        if(!String.IsNullOrEmpty(job.RestartPath))arguments+=(arguments.Length>0?" ":"")+UpdateFiles.Quote(job.RestartPath);
        return Process.Start(new ProcessStartInfo(UpdateFiles.Inside(job.TargetRoot,"LumaView.exe"),arguments){UseShellExecute=false,WorkingDirectory=job.TargetRoot});
    }
    [STAThread] static void Main(string[] args){
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        UpdateJob job=null;Mutex installLock=null;bool ownsLock=false;
        try{if(args.Length!=1)throw new ArgumentException("Expected an update job.");job=UpdateFiles.LoadJob(args[0]);installLock=new Mutex(false,UpdateFiles.LockName(job.TargetRoot));try{ownsLock=installLock.WaitOne(0);}catch(AbandonedMutexException){ownsLock=true;}if(!ownsLock)throw new IOException("Another update is already running for this installation.");}
        catch(Exception ex){if(installLock!=null)installLock.Dispose();try{string directory=args.Length==1?Path.GetDirectoryName(Path.GetFullPath(args[0])):null;if(directory!=null)File.WriteAllText(Path.Combine(directory,"worker-error.txt"),ex.Message);}catch{}return;}
        var window=new Form{Text="LumaView · v"+job.FromVersion+" → v"+job.ToVersion,ClientSize=new Size(490,180),Font=new Font("Segoe UI",10),StartPosition=FormStartPosition.CenterScreen,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,BackColor=Color.FromArgb(18,21,27),ForeColor=Color.FromArgb(231,236,244)};
        var label=new Label{Text=Say(job,"업데이트를 준비하고 있습니다…","Preparing your update…"),Dock=DockStyle.Fill,Padding=new Padding(24),AutoSize=false};
        var progress=new ProgressBar{Dock=DockStyle.Bottom,Height=8,Style=ProgressBarStyle.Marquee};window.Controls.Add(label);window.Controls.Add(progress);bool finished=false;
        window.FormClosing+=delegate(object sender,FormClosingEventArgs e){if(!finished)e.Cancel=true;};
        window.Shown+=async delegate{
            UpdateTransaction transaction=null;Process launched=null;bool parentClosed=false;Exception failure=null;
            try{
                File.WriteAllText(UpdateFiles.Inside(job.WorkRoot,"ready"),job.Id);
                await WaitForExit(job,text=>label.Text=text);parentClosed=true;
                // Recheck after the running app has released its files.
                await Task.Run(()=>UpdateFiles.ValidateJob(job));
                label.Text=Say(job,"기존 버전을 백업하고 있습니다…","Backing up the current version…");
                transaction=new UpdateTransaction(job);await Task.Run(()=>transaction.Backup());
                label.Text=Say(job,"프로그램 파일을 새 버전으로 교체하고 있습니다…","Replacing application files with the new version…");
                await Task.Run(()=>transaction.Install(name=>{}));
                label.Text=Say(job,"업데이트가 완료되었습니다. LumaView를 다시 여는 중…","Update applied. Reopening LumaView…");
                using(var started=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\LumaView.Update."+job.Id)){
                    launched=StartApp(job,true);var wait=Stopwatch.StartNew();
                    while(!started.WaitOne(0)){if(launched.HasExited||wait.Elapsed>TimeSpan.FromSeconds(60))throw new IOException(Say(job,"새 버전이 정상적으로 시작되지 않았습니다.","The new version did not start successfully."));await Task.Delay(100);}
                }
                UpdateFiles.Result(job,"success",Say(job,"업데이트 및 재실행 완료","Update and restart completed"));
            }catch(Exception ex){failure=ex;}
            if(failure!=null){
                Exception ex=failure;
                string error=ex.Message;
                try{
                    if(launched!=null&&!launched.HasExited){launched.CloseMainWindow();if(!launched.WaitForExit(3000)){launched.Kill();launched.WaitForExit(5000);}}
                    if(transaction!=null){label.Text=Say(job,"이전 버전으로 복구하고 있습니다…","Restoring the previous version…");await Task.Run(()=>transaction.Rollback());}
                    UpdateFiles.Result(job,"rolled_back",error);
                    if(parentClosed)using(var previous=StartApp(job,false)){}
                    error=Say(job,"업데이트를 완료하지 못해 기존 버전을 유지했습니다.\n","The update could not finish. The previous version was preserved.\n")+error;
                }catch(Exception recovery){error+="\n\n"+recovery.Message;UpdateFiles.Result(job,"recovery_required",error);}
                MessageBox.Show(window,error,"LumaView · v"+job.FromVersion+" → v"+job.ToVersion,MessageBoxButtons.OK,MessageBoxIcon.Warning);
            }
            if(launched!=null)launched.Dispose();finished=true;window.Close();
        };
        try{Application.Run(window);}finally{if(ownsLock)installLock.ReleaseMutex();installLock.Dispose();}
    }
}
}
