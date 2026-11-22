using System;using System.IO;using System.Linq;using System.Reflection;using System.Drawing;using System.Windows.Forms;using System.Web.Script.Serialization;using System.Runtime.InteropServices;using Microsoft.Win32;using System.Collections.Generic;
using System.Threading;using System.Threading.Tasks;
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
   Call("FileAssociations","HandleInstallerCommand",new object[]{new[]{"--register-associations",".jpg,.png"}});
   Check((bool)Call("FileAssociations","Registered",".jpg"),"register selected file types in isolated registry");
   Check(!(bool)Call("FileAssociations","Registered",".mp4"),"installer selection excludes other extensions");
   using(var key=Registry.CurrentUser.OpenSubKey(@"Software\LumaView\SelectedCapabilities\FileAssociations"))Check(key.GetValueNames().OrderBy(x=>x).SequenceEqual(new[]{".jpg",".png"}),"installer passes selected types to Windows settings");
   Call("FileAssociations","PrepareSelectedTypes",new object[]{new[]{".png"}});
   using(var key=Registry.CurrentUser.OpenSubKey(@"Software\LumaView\SelectedCapabilities\FileAssociations"))Check(key.GetValueNames().SequenceEqual(new[]{".png"}),"Windows defaults page contains only chosen extensions");
   Call("FileAssociations","UnregisterOwned");
   Check(!(bool)Call("FileAssociations","Registered",".jpg"),"uninstall removes this installation's registration");
   using(var key=Registry.CurrentUser.OpenSubKey(choice))Check((string)key.GetValue("ProgId")=="AnotherViewer.Jpeg","existing Windows UserChoice survives register/unregister");
   Call("FileAssociations","Register",new object[]{new[]{".jpg"}});
   foreach(string path in new[]{@"Software\Classes\LumaView.File.jpg",@"Software\Classes\Applications\DollView.exe"})using(var key=Registry.CurrentUser.CreateSubKey(path+@"\shell\open\command"))key.SetValue("","\"C:\\Newer\\DollView.exe\" \"%1\"");
   Call("FileAssociations","UnregisterOwned");
   using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Classes\LumaView.File.jpg"))Check(key!=null,"uninstall preserves newer installation registration");
  }finally{RegOverridePredefKey(CurrentUser,IntPtr.Zero);}
 }
 Registry.CurrentUser.DeleteSubKeyTree(root,false);
}
static string Release(string version,string repository="trackk81/LumaView",bool prerelease=false){string tag="v"+version,exe="LumaView-"+version+"-win-x64.zip";return new JavaScriptSerializer().Serialize(new {tag_name=tag,draft=false,prerelease=prerelease,assets=new[]{new{name=exe,browser_download_url="https://github.com/"+repository+"/releases/download/"+tag+"/"+exe,size=12345},new{name="SHA256SUMS.txt",browser_download_url="https://github.com/"+repository+"/releases/download/"+tag+"/SHA256SUMS.txt",size=100}}});}
static void Updater(){
 string[] names={"DollView-Setup-0.2.21-x64.exe","DollView-0.2.21-win-x64.zip","DollView-SHA256SUMS.txt","LumaView-0.2.21-win-x64.zip","SHA256SUMS.txt"};
 var release=new JavaScriptSerializer().Serialize(new{tag_name="v0.2.21",draft=false,prerelease=false,assets=names.Select(name=>new{name=name,size=100,browser_download_url="https://github.com/trackk81/LumaView/releases/download/v0.2.21/"+name}).ToArray()});
 var update=Call("AppUpdater","ParseRelease",release);
 var asset=update.GetType().GetField("Package").GetValue(update);
 Check((string)asset.GetType().GetProperty("name").GetValue(asset,null)==names[1],"updater selects portable ZIP when setup and both ZIPs are available");
 string zipHash=new string('a',64),setupHash=new string('b',64);
 Check((string)Call("AppUpdater","ExpectedHash",setupHash+"  "+names[0]+"\n"+zipHash+"  "+names[1],names[1])==zipHash,"updater reads ZIP digest from shared installer/ZIP checksums");
 Check(Call("AppUpdater","ParseRelease",Release("0.2.0"))!=null,"parse valid GitHub ZIP release");
 Check(Call("AppUpdater","ParseRelease",Release("0.2.0","trackk81/LumaView",true))==null,"exclude prerelease updates");
 Reject(()=>Call("AppUpdater","ParseRelease",Release("0.2.0","other/OtherApp")),"reject ZIP URL from another repository");
 Reject(()=>Call("AppUpdater","ParseRelease",Release("0.2.0").Replace("SHA256SUMS.txt","absent.txt")),"reject release missing checksums");
 var current=new Version(0,2,1);var older=new Version(0,2,0);
 foreach(string language in new[]{"ko","en"}){
  app.GetType("LumaView.Ui").GetField("Language").SetValue(null,language);
  string versions=(string)Call("AppUpdater","VersionsText",current,older,true);
  Check(versions.Contains("v0.2.1")&&versions.Contains("v0.2.0"),"show running and GitHub versions in "+language);
  string equal=(string)Call("AppUpdater","NoUpdateStatus",current,new Version(0,2,1));string newer=(string)Call("AppUpdater","NoUpdateStatus",current,older);Check(equal!=newer,"distinguish latest and locally newer version in "+language);
  Check((string)Call("AppUpdater","VersionsText",current,null,false)!=(string)Call("AppUpdater","VersionsText",current,null,true),"distinguish check failure from no release in "+language);
 }
 app.GetType("LumaView.Ui").GetField("Language").SetValue(null,"ko");
 Check((Version)Call("AppUpdater","ReleaseVersion",Release("0.3.0").Replace("SHA256SUMS.txt","absent.txt"))==new Version(0,3,0),"retain GitHub version when release asset validation fails");
 var running=(Version)app.GetType("LumaView.AppUpdater").GetProperty("CurrentVersion",stat).GetValue(null,null);
 Check(((string)Call("AppUpdater","WindowTitle",new string('x',300))).StartsWith("DollView v"+running),"version precedes long paths in window titles");
 string hash=new string('a',64);Check((string)Call("AppUpdater","ExpectedHash",hash+"  portable.zip\n","portable.zip")==hash,"read exact ZIP checksum");
 Reject(()=>Call("AppUpdater","ExpectedHash",hash+"  portable.zip\n"+hash+"  portable.zip","portable.zip"),"reject duplicate checksums");
 string temp=Path.GetTempFileName();try{File.WriteAllText(temp,"test");Check(!(bool)Call("AppUpdater","VerifyHash",temp,hash),"reject modified ZIP bytes");}finally{File.Delete(temp);}
}
static void FbxTextures(){
 string package=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"fbx-fixture-"+Guid.NewGuid().ToString("N"));
 string source=Path.Combine(package,"source"),textures=Path.Combine(package,"textures"),sub=Path.Combine(source,"props");
 Directory.CreateDirectory(sub);
 try{
  Directory.CreateDirectory(textures);
  string atlas=Path.Combine(textures,"Atlas.jpg"),alpha=Path.Combine(textures,"props_alpha.png");
  File.WriteAllText(atlas,"test");File.WriteAllText(alpha,"test");
  Func<string,string> resolve=reference=>(string)Call("FbxTextureFiles","Resolve",Path.Combine(source,"Export.fbx"),reference);
  Check(resolve(@"..\Atlas.psd")==atlas&&resolve(@"..\props\props_alpha.psd")==alpha,"find exported FBX textures in a sibling textures directory");
  string wing=Path.Combine(textures,"wing.tga.png");
  File.WriteAllText(wing,"test");
  Check(resolve("wing.tga")==wing,"find double-extension FBX color and opacity textures");
  File.WriteAllText(Path.Combine(textures,"wing.png"),"test");
  Check(resolve("wing.tga")==null,"skip ambiguous TGA exports");
  string exact=Path.Combine(sub,"props_alpha.png");File.WriteAllText(exact,"test");
  Check(resolve(@"props\props_alpha.png")==exact,"prefer the original FBX subdirectory when names collide");
  File.WriteAllText(Path.Combine(textures,"Atlas.png"),"test");
  Check(resolve(@"..\Atlas.psd")==null,"skip ambiguous PSD replacements");
  File.WriteAllText(Path.Combine(package,"private.png"),"test");
  Check(resolve(@"..\private.png")==null&&resolve("LM_Final.tga")==null,"unavailable and unrelated images stay unresolved");
 }finally{
  try{Directory.Delete(package,true);}
  catch(IOException){Console.WriteLine("Test fixture retained at "+package);}
  catch(UnauthorizedAccessException){Console.WriteLine("Test fixture retained at "+package);}
 }
}
static IEnumerable<Control> Walk(Control root){foreach(Control child in root.Controls){yield return child;foreach(var nested in Walk(child))yield return nested;}}
static void Components(){
 string version="";int installs=0,connections=0;string[] registered=null;
 Func<string> detect=()=>version;
 Func<Task> install=()=>{installs++;version="140.0.0.0";return Task.FromResult(0);};
 Action<string[]> connect=selected=>{connections++;registered=selected;};
 Action<string[],Func<Task>,CancellationToken> run=(selected,installer,token)=>((Task)Call("ComponentSetup","PrepareCoreAsync",selected,detect,installer,connect,token)).GetAwaiter().GetResult();
 run(new[]{".png",".glb",".png"},install,CancellationToken.None);
 Check(installs==1&&connections==1&&registered.SequenceEqual(new[]{".png",".glb"}),"missing WebView2 installs once before selected file types connect");
 run(new[]{".jpg"},install,CancellationToken.None);
 Check(installs==1&&connections==2&&registered.SequenceEqual(new[]{".jpg"}),"installed WebView2 skips download and connects selected files");
 run(new string[0],install,CancellationToken.None);Check(connections==2,"component-only setup does not change file associations");
 version="";
 try{run(new[]{".jpg"},()=>{throw new IOException("Offline");},CancellationToken.None);throw new Exception("Offline install should fail");}catch(IOException){}
 Check(connections==2,"offline setup cannot claim success or change file associations");
 try{run(new[]{".jpg"},()=>Task.FromResult(0),CancellationToken.None);throw new Exception("Missing runtime should fail");}catch(InvalidOperationException){}
 Check(connections==2,"installer exit alone is insufficient without runtime detection");
 using(var stop=new CancellationTokenSource()){
  Func<Task> cancel=()=>{stop.Cancel();return Task.FromResult(0);};
  try{run(new[]{".jpg"},cancel,stop.Token);throw new Exception("Cancelled setup should fail");}catch(OperationCanceledException){}
 }
 Check(connections==2,"cancelled setup never continues to file associations");
 int before=installs;
 try{run(new[]{".not-supported"},install,CancellationToken.None);throw new Exception("Invalid selection should fail");}catch(ArgumentException){}
 Check(installs==before&&connections==2,"invalid file type selection rejected before any installation");
}
static void Layout(){
 using(var form=(Form)Activator.CreateInstance(app.GetType("LumaView.Viewer"),new object[]{null})){
  form.Show();for(int n=0;n<25;n++){Application.DoEvents();System.Threading.Thread.Sleep(10);}
  Check(form.Text.StartsWith("DollView v"),"gallery title displays running version first");
  var update=Walk(form).OfType<Button>().Single(x=>x.Name=="updateButton");
  Check(update.Visible&&update.Enabled,"update button appears in normal gallery");
  var siblings=update.Parent.Controls.Cast<Control>().ToArray();int index=Array.IndexOf(siblings,update);Check(index>0&&(siblings[index-1].Text=="설정"||siblings[index-1].Text=="Settings")&&update.Left>=siblings[index-1].Right,"update button sits directly beside settings");
  foreach(string theme in new[]{"dark","light","custom"}){app.GetType("LumaView.Ui").GetField("Theme").SetValue(null,theme);Call("Ui","Apply",form);Check(update.ForeColor!=update.BackColor,"update button readable in "+theme+" theme");}
  app.GetType("LumaView.Ui").GetField("Language").SetValue(null,"en");Call("Ui","Apply",form);Check(update.Text=="Update","update label translated to English");
  form.Close();
 }
 foreach(string language in new[]{"ko","en"}){
  app.GetType("LumaView.Ui").GetField("Language").SetValue(null,language);
  using(var settings=(Form)Activator.CreateInstance(app.GetType("LumaView.SettingsDialog"))){
   settings.Show();var button=Walk(settings).OfType<Button>().Single(x=>x.Name=="prepareComponents");
   settings.GetType().GetMethod("SelectPage",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(settings,new object[]{1});Application.DoEvents();
   var status=Walk(settings).OfType<Label>().Single(x=>x.Name=="componentStatus");
   Check(button.Visible&&button.Enabled&&button.Parent.ClientRectangle.Contains(button.Bounds),"download/connect action visible and usable in "+language);
   Check(status.Bottom<=status.Parent.ClientSize.Height&&status.PointToScreen(System.Drawing.Point.Empty).Y>=button.PointToScreen(new System.Drawing.Point(0,button.Height)).Y,"component setup status fits below action in "+language);
   settings.GetType().GetMethod("SelectPage",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(settings,new object[]{2});Application.DoEvents();
   var controls=Walk(settings).Where(c=>c.Visible).ToArray();
   var images=controls.OfType<Button>().Single(b=>b.Text==(language=="ko"?"이미지":"Images"));
   var videos=controls.OfType<Button>().Single(b=>b.Text==(language=="ko"?"영상":"Videos"));
   var models=controls.OfType<Button>().Single(b=>b.Text==(language=="ko"?"3D 모델":"3D models"));
   var list=controls.OfType<ListView>().Single();
   images.PerformClick();
   Check(list.CheckedItems.Count==16&&list.CheckedItems.Cast<ListViewItem>().All(i=>(bool)Call("Album","Supported","x"+i.Text)),"image group selects only supported images in "+language);
   videos.PerformClick();models.PerformClick();
   Check(list.CheckedItems.Count==list.Items.Count,"groups can be combined in "+language);
   images.PerformClick();
   Check(list.CheckedItems.Count==14,"clearing an image group preserves other groups in "+language);
   settings.Close();
  }
 }
}
[STAThread]static int Main(string[] args){try{app=Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"DollView.exe"));Application.EnableVisualStyles();Associations();Updater();Components();FbxTextures();if(!args.Contains("--no-ui"))Layout();Console.WriteLine("PASS release registration, updater, component setup and UI checks.");return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
}

