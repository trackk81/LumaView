using System;using System.IO;using System.Linq;using System.Reflection;using System.Drawing;using System.Windows.Forms;using System.Web.Script.Serialization;using System.Runtime.InteropServices;using Microsoft.Win32;using System.Collections.Generic;
class ReleaseChecks {
static Assembly app;static BindingFlags stat=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
static object Call(string type,string name,params object[] args){try{return app.GetType("LumaView."+type).GetMethod(name,stat).Invoke(null,args);}catch(TargetInvocationException e){throw e.InnerException;}}
static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
static void Reject(Action action,string message){try{action();}catch(InvalidDataException){Console.WriteLine("PASS "+message);return;}throw new Exception(message);}
[DllImport("advapi32.dll")]static extern int RegOverridePredefKey(IntPtr key,IntPtr replacement);
static IntPtr CurrentUser=new IntPtr(unchecked((int)0x80000001));
static void Associations(){
 string root=@"Software\LumaView-ReleaseTests\"+Guid.NewGuid().ToString("N");
 using(var isolated=Registry.CurrentUser.CreateSubKey(root)){
  if(RegOverridePredefKey(CurrentUser,isolated.Handle.DangerousGetHandle())!=0)throw new Exception("Registry isolation failed");
  try{
   const string choice=@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.jpg\UserChoice";
   using(var key=Registry.CurrentUser.CreateSubKey(choice))key.SetValue("ProgId","AnotherViewer.Jpeg");
   Call("FileAssociations","Register",new object[]{new[]{".jpg",".png"}});
   Check((bool)Call("FileAssociations","Registered",".jpg"),"register selected file types in isolated registry");
   Call("FileAssociations","PrepareSelectedTypes",new object[]{new[]{".png"}});
   using(var key=Registry.CurrentUser.OpenSubKey(@"Software\LumaView\SelectedCapabilities\FileAssociations"))Check(key.GetValueNames().SequenceEqual(new[]{".png"}),"Windows defaults page contains only chosen extensions");
   Call("FileAssociations","UnregisterOwned");
   Check(!(bool)Call("FileAssociations","Registered",".jpg"),"uninstall removes this installation's registration");
   using(var key=Registry.CurrentUser.OpenSubKey(choice))Check((string)key.GetValue("ProgId")=="AnotherViewer.Jpeg","existing Windows UserChoice survives register/unregister");
   Call("FileAssociations","Register",new object[]{new[]{".jpg"}});
   foreach(string path in new[]{@"Software\Classes\LumaView.File.jpg",@"Software\Classes\Applications\LumaView.exe"})using(var key=Registry.CurrentUser.CreateSubKey(path+@"\shell\open\command"))key.SetValue("","\"C:\\Newer\\LumaView.exe\" \"%1\"");
   Call("FileAssociations","UnregisterOwned");
   using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Classes\LumaView.File.jpg"))Check(key!=null,"uninstall preserves newer installation registration");
  }finally{RegOverridePredefKey(CurrentUser,IntPtr.Zero);}
 }
 Registry.CurrentUser.DeleteSubKeyTree(root,false);
}
static string Release(string version,string repository="trackk81/LumaView",bool prerelease=false){string tag="v"+version,exe="LumaView-Setup-"+version+"-x64.exe";return new JavaScriptSerializer().Serialize(new {tag_name=tag,draft=false,prerelease=prerelease,assets=new[]{new{name=exe,browser_download_url="https://github.com/"+repository+"/releases/download/"+tag+"/"+exe,size=12345},new{name="SHA256SUMS.txt",browser_download_url="https://github.com/"+repository+"/releases/download/"+tag+"/SHA256SUMS.txt",size=100}}});}
static void Updater(){
 Check(Call("AppUpdater","ParseRelease",Release("0.2.0"))!=null,"parse valid GitHub installer release");
 Check(Call("AppUpdater","ParseRelease",Release("0.2.0","trackk81/LumaView",true))==null,"exclude prerelease updates");
 Reject(()=>Call("AppUpdater","ParseRelease",Release("0.2.0","other/OtherApp")),"reject installer URL from another repository");
 Reject(()=>Call("AppUpdater","ParseRelease",Release("0.2.0").Replace("SHA256SUMS.txt","absent.txt")),"reject release missing checksums");
 string hash=new string('a',64);Check((string)Call("AppUpdater","ExpectedHash",hash+"  setup.exe\n","setup.exe")==hash,"read exact installer checksum");
 Reject(()=>Call("AppUpdater","ExpectedHash",hash+"  setup.exe\n"+hash+"  setup.exe","setup.exe"),"reject duplicate checksums");
 string temp=Path.GetTempFileName();try{File.WriteAllText(temp,"test");Check(!(bool)Call("AppUpdater","VerifyHash",temp,hash),"reject modified installer bytes");}finally{File.Delete(temp);}
}
static IEnumerable<Control> Walk(Control root){foreach(Control child in root.Controls){yield return child;foreach(var nested in Walk(child))yield return nested;}}
static void Layout(){
 using(var form=(Form)Activator.CreateInstance(app.GetType("LumaView.Viewer"),new object[]{null})){
  form.Show();for(int n=0;n<25;n++){Application.DoEvents();System.Threading.Thread.Sleep(10);}
  var update=Walk(form).OfType<Button>().Single(x=>x.Name=="updateButton");
  Check(update.Visible&&update.Enabled,"update button appears in normal gallery");
  var siblings=update.Parent.Controls.Cast<Control>().ToArray();int index=Array.IndexOf(siblings,update);Check(index>0&&(siblings[index-1].Text=="설정"||siblings[index-1].Text=="Settings")&&update.Left>=siblings[index-1].Right,"update button sits directly beside settings");
  foreach(string theme in new[]{"dark","light","custom"}){app.GetType("LumaView.Ui").GetField("Theme").SetValue(null,theme);Call("Ui","Apply",form);Check(update.ForeColor!=update.BackColor,"update button readable in "+theme+" theme");}
  app.GetType("LumaView.Ui").GetField("Language").SetValue(null,"en");Call("Ui","Apply",form);Check(update.Text=="Update","update label translated to English");
  form.Close();
 }
}
[STAThread]static int Main(string[] args){try{app=Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"LumaView.exe"));Application.EnableVisualStyles();Associations();Updater();if(!args.Contains("--no-ui"))Layout();Console.WriteLine("PASS release registration, updater validation and UI checks.");return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
}
