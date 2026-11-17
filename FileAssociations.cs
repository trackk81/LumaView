using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LumaView {
static class FileAssociations {
    public static readonly string[] Extensions={".jpg",".jpeg",".png",".bmp",".gif",".tif",".tiff",".ico",".webp",".exr",".psd",".psb",".tga",".hdr",".dpx",".dds",".mp4",".m4v",".mov",".wmv",".avi",".mpg",".mpeg",".mkv",".webm",".obj",".glb",".gltf",".stl",".fbx"};
    const string Capabilities=@"Software\LumaView\Capabilities";
    [DllImport("shlwapi.dll",CharSet=CharSet.Unicode)] static extern uint AssocQueryString(uint flags,uint kind,string extension,string extra,StringBuilder output,ref uint size);
    [DllImport("shell32.dll")] static extern void SHChangeNotify(uint eventId,uint flags,IntPtr first,IntPtr second);
    public static string CurrentApp(string extension){uint length=512;var text=new StringBuilder((int)length);return AssocQueryString(0,4,extension,null,text,ref length)==0?text.ToString():Ui.T("확인 불가");}
    public static bool Registered(string extension){using(var key=Registry.CurrentUser.OpenSubKey(Capabilities+@"\FileAssociations"))return key!=null&&key.GetValue(extension)!=null;}
    public static string OpenCommand(string executable){return "\""+executable+"\" \"%1\"";}
    public static void Register(string[] extensions){
        if(extensions.Length==0)throw new InvalidOperationException(Ui.T("확장자를 선택해 주세요."));
        if(extensions.Any(e=>!Extensions.Contains(e)))throw new ArgumentException("extension");
        string executable=Application.ExecutablePath;
        string iconPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","LumaView.ico");if(!File.Exists(iconPath))iconPath=executable;
        using(var caps=Registry.CurrentUser.CreateSubKey(Capabilities)){caps.SetValue("ApplicationName","LumaView");caps.SetValue("ApplicationDescription","LumaView image, sequence and video viewer");caps.SetValue("ApplicationIcon","\""+iconPath+"\",0");}
        using(var registered=Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))registered.SetValue("LumaView",Capabilities);
        using(var app=Registry.CurrentUser.CreateSubKey(@"Software\Classes\Applications\LumaView.exe")){
            app.SetValue("FriendlyAppName","LumaView");using(var icon=app.CreateSubKey("DefaultIcon"))icon.SetValue("","\""+iconPath+"\",0");using(var command=app.CreateSubKey(@"shell\open\command"))command.SetValue("",OpenCommand(executable));
            using(var supported=app.CreateSubKey("SupportedTypes"))foreach(string extension in extensions)supported.SetValue(extension,"");
        }
        foreach(string extension in extensions){
            string progId="LumaView.File"+extension;
            using(var prog=Registry.CurrentUser.CreateSubKey(@"Software\Classes\"+progId)){
                prog.SetValue("","LumaView "+extension.ToUpperInvariant()+" file");
                using(var icon=prog.CreateSubKey("DefaultIcon"))icon.SetValue("","\""+iconPath+"\",0");
                using(var command=prog.CreateSubKey(@"shell\open\command"))command.SetValue("",OpenCommand(executable));
            }
            using(var associations=Registry.CurrentUser.CreateSubKey(Capabilities+@"\FileAssociations"))associations.SetValue(extension,progId);
            using(var openWith=Registry.CurrentUser.CreateSubKey(@"Software\Classes\"+extension+@"\OpenWithProgids"))openWith.SetValue(progId,new byte[0],RegistryValueKind.None);
        }
        SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero);
    }
    public static void OpenDefaults(){Process.Start(new ProcessStartInfo("ms-settings:defaultapps?registeredAppUser=LumaView"){UseShellExecute=true});}
    const string SelectedCapabilities=@"Software\LumaView\SelectedCapabilities";
    public static string PrepareSelectedTypes(string[] extensions){
        if(extensions==null||extensions.Length==0)throw new InvalidOperationException(Ui.T("확장자를 선택해 주세요."));
        if(extensions.Any(e=>!Extensions.Contains(e)))throw new ArgumentException("extension");
        // A separate, reusable capability profile scopes the Windows page to
        // the selection. The main registration and existing defaults stay intact.
        using(var caps=Registry.CurrentUser.CreateSubKey(SelectedCapabilities)){
            caps.SetValue("ApplicationName",Ui.T("LumaView — 선택한 확장자"));
            caps.SetValue("ApplicationDescription",String.Join(", ",extensions.Distinct()));
            using(var main=Registry.CurrentUser.OpenSubKey(Capabilities)){
                if(main!=null&&main.GetValue("ApplicationIcon") is string)caps.SetValue("ApplicationIcon",(string)main.GetValue("ApplicationIcon"));
            }
            using(var types=caps.CreateSubKey("FileAssociations")){
                foreach(string old in types.GetValueNames())if(!extensions.Contains(old))types.DeleteValue(old,false);
                foreach(string extension in extensions.Distinct())types.SetValue(extension,"LumaView.File"+extension);
            }
        }
        using(var registered=Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))registered.SetValue("LumaView.SelectedTypes",SelectedCapabilities);
        SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero);
        return "ms-settings:defaultapps?registeredAppUser=LumaView.SelectedTypes";
    }
    public static void OpenSelectedDefaults(string[] extensions){Process.Start(new ProcessStartInfo(PrepareSelectedTypes(extensions)){UseShellExecute=true});}
    public static bool HandleInstallerCommand(string[] args){
        if(args.Length==0||!new[]{"--register-associations","--unregister-associations","--prepare-default-associations","--open-selected-defaults"}.Contains(args[0]))return false;
        try{
            if(args[0]=="--register-associations")Register(Extensions);
            else if(args[0]=="--unregister-associations")UnregisterOwned();
            else{
                if(args.Length!=2)throw new ArgumentException("Expected a comma-separated extension list.");
                var selected=args[1].Split(',').Select(e=>e.Trim().ToLowerInvariant()).Distinct().ToArray();
                if(args[0]=="--prepare-default-associations")PrepareSelectedTypes(selected);
                else OpenSelectedDefaults(selected);
            }
        }catch(Exception ex){Console.Error.WriteLine(ex.Message);Environment.ExitCode=1;}
        return true;
    }
    static bool OwnsCommand(string keyPath){
        using(var key=Registry.CurrentUser.OpenSubKey(keyPath+@"\shell\open\command"))
            return key!=null&&String.Equals(key.GetValue("") as string,OpenCommand(Application.ExecutablePath),StringComparison.OrdinalIgnoreCase);
    }
    public static void UnregisterOwned(){
        // Never change UserChoice, or remove another installation's command.
        foreach(string extension in Extensions){
            string progId="LumaView.File"+extension,path=@"Software\Classes\"+progId;
            if(!OwnsCommand(path))continue;
            Registry.CurrentUser.DeleteSubKeyTree(path,false);
            using(var openWith=Registry.CurrentUser.OpenSubKey(@"Software\Classes\"+extension+@"\OpenWithProgids",true))if(openWith!=null)openWith.DeleteValue(progId,false);
        }
        string appPath=@"Software\Classes\Applications\LumaView.exe";
        if(OwnsCommand(appPath)){
            Registry.CurrentUser.DeleteSubKeyTree(appPath,false);
            Registry.CurrentUser.DeleteSubKeyTree(Capabilities,false);
            Registry.CurrentUser.DeleteSubKeyTree(SelectedCapabilities,false);
            using(var registered=Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications",true))if(registered!=null){
                if(String.Equals(registered.GetValue("LumaView") as string,Capabilities,StringComparison.OrdinalIgnoreCase))registered.DeleteValue("LumaView",false);
                if(String.Equals(registered.GetValue("LumaView.SelectedTypes") as string,SelectedCapabilities,StringComparison.OrdinalIgnoreCase))registered.DeleteValue("LumaView.SelectedTypes",false);
            }
        }
        SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero);
    }
}
sealed class AssociationSettings:UserControl {
    internal string[] SelectedExtensions {get{return list.CheckedItems.Cast<ListViewItem>().Select(i=>(string)i.Tag).ToArray();}}
    Form settingsForm;
    bool connecting;
    readonly ListView list=new ListView{Dock=DockStyle.Fill,View=View.Details,CheckBoxes=true,FullRowSelect=true,HideSelection=false};
    readonly Label notice=new Label{Dock=DockStyle.Top,Height=78,Padding=new Padding(8),Text=Ui.T("확장자를 체크하고 연결을 누르면 선택한 형식만 표시됩니다.\n열린 화면에서 LumaView를 선택하세요. 검색할 필요가 없습니다.")};
    public AssociationSettings(){
        Dock=DockStyle.Fill;list.Columns.Add(Ui.T("확장자"),90);list.Columns.Add(Ui.T("연결 후보"),120);list.Columns.Add(Ui.T("현재 기본 앱"),250);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=82,Padding=new Padding(6),WrapContents=true};
        var all=new Button{Text=Ui.T("전체 선택"),AutoSize=true};all.Click+=delegate{foreach(ListViewItem item in list.Items)item.Checked=true;};
        var register=new Button{Text=Ui.T("연결"),AutoSize=true};register.Click+=delegate{
            if(connecting)return;connecting=true;register.Enabled=false;
            try{
                string[] selected=list.CheckedItems.Cast<ListViewItem>().Select(i=>(string)i.Tag).ToArray();
                FileAssociations.Register(selected);
                RefreshRows();
                FileAssociations.OpenSelectedDefaults(selected);
                notice.Text=Ui.T("Windows에서 선택할 확장자: ")+String.Join(", ",selected)+"\n"+Ui.T("LumaView를 선택해 완료하세요. 연결 결과는 복귀 시 갱신됩니다.");
            }catch(Exception ex){MessageBox.Show(this,ex.Message,Ui.T("파일 연결"));}
            finally{connecting=false;register.Enabled=true;}
        };
        var defaults=new Button{Text=Ui.T("Windows 기본 앱 설정 열기"),AutoSize=true};defaults.Click+=delegate{try{FileAssociations.OpenDefaults();}catch(Exception ex){MessageBox.Show(this,ex.Message,Ui.T("파일 연결"));}};
        var refresh=new Button{Text=Ui.T("새로고침"),AutoSize=true};refresh.Click+=delegate{RefreshRows();};
        buttons.Controls.AddRange(new Control[]{all,register,defaults,refresh});Controls.Add(list);Controls.Add(notice);Controls.Add(buttons);
        RefreshRows();Ui.Apply(this);
        Load+=delegate{settingsForm=FindForm();if(settingsForm!=null)settingsForm.Activated+=RefreshOnReturn;};
    }
    void RefreshOnReturn(object sender,EventArgs e){if(!IsDisposed&&Visible&&!connecting)RefreshRows();}
    protected override void Dispose(bool disposing){if(disposing&&settingsForm!=null){settingsForm.Activated-=RefreshOnReturn;settingsForm=null;}base.Dispose(disposing);}
    void RefreshRows(){var selected=list.CheckedItems.Cast<ListViewItem>().Select(i=>(string)i.Tag).ToArray();list.Items.Clear();foreach(string extension in FileAssociations.Extensions){bool registered=FileAssociations.Registered(extension);list.Items.Add(new ListViewItem(new[]{extension,Ui.T(registered?"등록됨":"미등록"),FileAssociations.CurrentApp(extension)}){Tag=extension,Checked=selected.Contains(extension)});}}
}
}


