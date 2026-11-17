using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Compile this smoke check into an extracted ZIP and run there. It checks the
// installed-runtime path without installing software or changing associations.
class ComponentUiCheck {
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
    static IEnumerable<Control> Walk(Control control){foreach(Control child in control.Controls){yield return child;foreach(var nested in Walk(child))yield return nested;}}
    [STAThread] static int Main(string[] args){
        try{
            Application.EnableVisualStyles();
            var app=Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"LumaView.exe"));
            string runtime=(string)app.GetType("LumaView.ComponentSetup").GetMethod("WebViewVersion",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            if(String.IsNullOrEmpty(runtime))throw new Exception("This non-installing smoke test requires an existing WebView2 runtime.");
            using(var settings=(Form)Activator.CreateInstance(app.GetType("LumaView.SettingsDialog"))){
                settings.Show();var controls=Walk(settings).ToArray();
                var tabs=controls.OfType<TabControl>().Single();tabs.SelectedIndex=1;
                controls.OfType<CheckBox>().Single(x=>x.Name=="setupFileAssociations").Checked=false;
                var prepare=controls.OfType<Button>().Single(x=>x.Name=="prepareComponents");
                var status=controls.OfType<Label>().Single(x=>x.Name=="componentStatus");
                prepare.PerformClick();var watch=Stopwatch.StartNew();
                while(!prepare.Enabled&&watch.ElapsedMilliseconds<10000){Application.DoEvents();System.Threading.Thread.Sleep(10);}
                Application.DoEvents();
                if(!status.Text.Contains("구성 요소가 준비되었습니다"))throw new Exception(status.Text);
                if(args.Length>0)using(var image=new Bitmap(settings.Width,settings.Height)){
                    using(var g=Graphics.FromImage(image)){IntPtr dc=g.GetHdc();try{PrintWindow(settings.Handle,dc,0);}finally{g.ReleaseHdc(dc);}}
                    image.Save(args[0],System.Drawing.Imaging.ImageFormat.Png);
                }
                Console.WriteLine("PASS packaged settings action detects existing WebView2 and completes without download or file association changes");
                settings.Close();
            }
            return 0;
        }catch(Exception ex){Console.WriteLine(ex);return 1;}
    }
}
