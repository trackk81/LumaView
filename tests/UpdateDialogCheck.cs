using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

// Optional live GitHub/UI smoke check; run separately from deterministic CI tests.
class UpdateDialogCheck {
    delegate bool WindowProc(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] static extern bool EnumThreadWindows(uint thread, WindowProc callback, IntPtr state);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, WindowProc callback, IntPtr state);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder text, int size);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out Rect rect);
    static string Text(IntPtr h) { var b=new StringBuilder(6000); GetWindowText(h,b,b.Capacity); return b.ToString(); }

    [STAThread] static int Main(string[] args) {
        try {
            if(args.Length<2) throw new ArgumentException("Usage: UpdateDialogCheck.exe <DollView.exe> <expected GitHub version> [screenshot.png]");
            Application.EnableVisualStyles();
            var assembly=Assembly.LoadFrom(System.IO.Path.GetFullPath(args[0]));
            var flags=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
            var updater=assembly.GetType("LumaView.AppUpdater");
            string current=updater.GetProperty("CurrentVersion",flags).GetValue(null,null).ToString();
            string body=""; bool seen=false; uint thread=GetCurrentThreadId();
            var visible= new Stopwatch();
            using(var owner=new Form { Text="Version dialog test",Size=new Size(600,400),StartPosition=FormStartPosition.CenterScreen })
            using(var button=new Button { Text="Update" })
            using(var timer=new Timer { Interval=60 }) {
                owner.Controls.Add(button); owner.Show();
                timer.Tick+=delegate {
                    EnumThreadWindows(thread,(h,s)=> {
                        if(!Text(h).StartsWith("DollView v"+current)) return true;
                        if(!visible.IsRunning) visible.Start();
                        if(visible.ElapsedMilliseconds<500) return false;
                        var text=new StringBuilder();
                        EnumChildWindows(h,(child,state)=> { text.AppendLine(Text(child));return true; },IntPtr.Zero);
                        body=text.ToString(); seen=true; timer.Stop();
                        if(args.Length>2) {
                            Rect r; GetWindowRect(h,out r);
                            using(var image=new Bitmap(r.Right-r.Left,r.Bottom-r.Top)) {
                                using(var g=Graphics.FromImage(image)) {
                                    IntPtr dc=g.GetHdc(); try { PrintWindow(h,dc,0); } finally { g.ReleaseHdc(dc); }
                                }
                                image.Save(args[2],System.Drawing.Imaging.ImageFormat.Png);
                            }
                        }
                        // Escape closes both OK and Yes/No dialogs without opening a browser or downloading.
                        PostMessage(h,0x100,(IntPtr)27,IntPtr.Zero);
                        return false;
                    },IntPtr.Zero);
                };
                timer.Start();
                var task=(Task)updater.GetMethod("CheckAsync",flags).Invoke(null,new object[]{owner,button});
                var watch=Stopwatch.StartNew();
                while(!task.IsCompleted&&watch.ElapsedMilliseconds<40000) { Application.DoEvents();System.Threading.Thread.Sleep(5); }
                if(!task.IsCompleted) throw new Exception("Update check did not finish");
                task.GetAwaiter().GetResult();
                if(!seen||!body.Contains("v"+current)||!body.Contains("v"+args[1])) throw new Exception("Missing versions: "+body);
                if(!button.Enabled) throw new Exception("Update button was not restored");
                Console.WriteLine(body);
                Console.WriteLine("PASS real GitHub check displays current and latest release versions");
                owner.Close();
            }
            return 0;
        } catch(Exception e) { Console.WriteLine(e);return 1; }
    }
}
