using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace LumaView {
static class LegacyLauncher {
    [STAThread]
    static int Main(string[] args){
        string root=AppDomain.CurrentDomain.BaseDirectory;
        string executable=Path.Combine(root,UpdateFiles.Executable);
        Guid updateId;
        bool updating=args.Length>=2&&args[0]=="--update-complete"&&Guid.TryParseExact(args[1],"N",out updateId);
        try{
            using(var ready=updating?EventWaitHandle.OpenExisting("Local\\LumaView.Update."+args[1]):null)
            using(var process=Process.Start(new ProcessStartInfo(executable,String.Join(" ",args.Select(UpdateFiles.Quote))){
                UseShellExecute=false,WorkingDirectory=root
            })){
                if(ready==null)return 0;
                var watch=Stopwatch.StartNew();
                while(!process.HasExited&&watch.Elapsed<TimeSpan.FromSeconds(50)){
                    if(ready.WaitOne(100)){Thread.Sleep(250);return 0;}
                }
                if(!process.HasExited)process.Kill();
                return 1;
            }
        }catch(Exception ex){
            if(!updating)MessageBox.Show(ex.Message,"DollView",MessageBoxButtons.OK,MessageBoxIcon.Error);
            return 1;
        }
    }
}
}
