using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Reflection;using System.Diagnostics;using System.Runtime.InteropServices;using System.Windows.Forms;
class SettingsInteractionChecks {
 static Assembly app;static Exception failure;static readonly BindingFlags flags=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 delegate bool WindowCallback(IntPtr h,IntPtr state);
 [DllImport("user32.dll")]static extern bool EnumThreadWindows(uint thread,WindowCallback callback,IntPtr state);
 [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
 static IEnumerable<Control> Walk(Control parent){foreach(Control child in parent.Controls){yield return child;foreach(var c in Walk(child))yield return c;}}
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS "+text);}
 static void Pump(){var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<50){Application.DoEvents();System.Threading.Thread.Sleep(2);}}
 static ContextMenuStrip Popup(){ContextMenuStrip menu=null;EnumThreadWindows(GetCurrentThreadId(),(h,s)=>{var control=Control.FromHandle(h) as ContextMenuStrip;if(control!=null&&control.Visible)menu=control;return true;},IntPtr.Zero);if(menu==null)throw new Exception("Choice popup did not open");return menu;}
 static void Choose(Control control,int index){((Button)control).PerformClick();Pump();var menu=Popup();menu.Items[index].PerformClick();Pump();Check(!menu.Visible,"choice popup closes after selecting "+control.Name);}
 [STAThread]static int Main(string[] args){try{
  Application.EnableVisualStyles();Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);app=Assembly.LoadFrom(Path.GetFullPath(args[0]));var ui=app.GetType("LumaView.Ui");
  string scratch=Path.Combine(Path.GetTempPath(),"LumaView-settings-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scratch);ui.GetField("SettingsPath",flags).SetValue(null,Path.Combine(scratch,"settings.ini"));
  ui.GetField("Language",flags).SetValue(null,"ko");ui.GetField("Theme",flags).SetValue(null,"dark");
  using(var viewer=(Form)Activator.CreateInstance(app.GetType("LumaView.Viewer"),new object[]{scratch})){
   viewer.Show();Pump();
   foreach(int language in new[]{1,0,1,0})using(var timer=new Timer{Interval=80}){
    timer.Tick+=delegate{timer.Stop();var settings=Application.OpenForms.Cast<Form>().First(f=>f.GetType().Name=="SettingsDialog");try{
     var controls=Walk(settings).ToArray();var languageControl=controls.Single(c=>c.Name=="settingsLanguage");var theme=controls.Single(c=>c.Name=="settingsTheme");
     Choose(languageControl,language);Choose(languageControl,language);Choose(theme,2);Choose(theme,0);
     ((Button)controls.Single(c=>c.Name=="settingsSave")).PerformClick();
    }catch(Exception ex){failure=ex;settings.DialogResult=DialogResult.Cancel;}};
    timer.Start();app.GetType("LumaView.Viewer").GetMethod("ShowSettings",flags).Invoke(viewer,null);if(failure!=null)throw failure;
    Check((string)ui.GetField("Language",flags).GetValue(null)==(language==1?"en":"ko"),"save language and refresh existing viewer");
    ui.GetMethod("Load",flags).Invoke(null,null);Check((string)ui.GetField("Language",flags).GetValue(null)==(language==1?"en":"ko"),"saved language survives reload");
    Check(Walk(viewer).Any(c=>c.Text==(language==1?"Settings":"설정")),"main settings button follows saved language");
   }
   viewer.Close();
  }
  Console.WriteLine("PASS repeated Korean/English, popup reopen, theme choices, save and reload in isolated settings file");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
