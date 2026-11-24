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
    public static readonly string[] Extensions={".jpg",".jpeg",".png",".bmp",".gif",".tif",".tiff",".ico",".webp",".exr",".psd",".psb",".tga",".hdr",".dpx",".dds",".pdf",".mp4",".m4v",".mov",".wmv",".avi",".mpg",".mpeg",".mkv",".webm",".obj",".glb",".gltf",".stl",".fbx"};
    const string Capabilities=@"Software\LumaView\Capabilities";
    [DllImport("shlwapi.dll",CharSet=CharSet.Unicode)] static extern uint AssocQueryString(uint flags,uint kind,string extension,string extra,StringBuilder output,ref uint size);
    [DllImport("shell32.dll")] static extern void SHChangeNotify(uint eventId,uint flags,IntPtr first,IntPtr second);
    public static string CurrentApp(string extension){uint length=512;var text=new StringBuilder((int)length);return AssocQueryString(0,4,extension,null,text,ref length)==0?text.ToString():Ui.T("확인 불가");}
    public static bool Registered(string extension){using(var key=Registry.CurrentUser.OpenSubKey(Capabilities+@"\FileAssociations"))return key!=null&&key.GetValue(extension)!=null;}
    public static string OpenCommand(string executable){return "\""+executable+"\" \"%1\"";}
    static string ThumbnailOverlay(string executable,string extension){
        string file="file"+extension;
        if(PdfPages.IsPdf(file))
            return "\""+Path.Combine(Path.GetDirectoryName(executable),"assets","DollView-PDF.ico")+"\",0";
        if(ModelFiles.Supported(file))
            return "\""+Path.Combine(Path.GetDirectoryName(executable),"assets","DollView-3D.ico")+"\",0";
        return Album.Supported(file)?"\""+executable+"\",0":null;
    }
    // Upgrade existing registrations in place. A portable copy must not point
    // another installation's thumbnail badges at its own assets.
    internal static void RefreshThumbnailOverlays(string executable){
        bool changed=false;
        foreach(string extension in Extensions){
            string overlay=ThumbnailOverlay(executable,extension);
            if(overlay==null)continue;
            string path=@"Software\Classes\LumaView.File"+extension;
            using(var key=Registry.CurrentUser.OpenSubKey(path,true)){
                if(key==null)continue;
                using(var command=key.OpenSubKey(@"shell\open\command")){
                    if(command==null||!String.Equals(command.GetValue("") as string,OpenCommand(executable),StringComparison.OrdinalIgnoreCase))continue;
                }
                if(!String.Equals(key.GetValue("TypeOverlay") as string,overlay,StringComparison.OrdinalIgnoreCase)){
                    key.SetValue("TypeOverlay",overlay);changed=true;
                }
                if(extension==".pdf")using(var icon=key.CreateSubKey("DefaultIcon")){
                    if(!String.Equals(icon.GetValue("") as string,overlay,StringComparison.OrdinalIgnoreCase)){
                        icon.SetValue("",overlay);changed=true;
                    }
                }
            }
        }
        if(changed)SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero);
    }
    internal static void MigrateLegacyCommand(string previous,string current){
        string oldCommand=OpenCommand(previous),newCommand=OpenCommand(current);
        var classes=Extensions.Select(extension=>"LumaView.File"+extension)
            .Concat(new[]{@"Applications\LumaView.exe"});
        bool changed=false;
        foreach(string name in classes){
            using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Classes\"+name+@"\shell\open\command",true)){
                if(key==null||!String.Equals(key.GetValue("") as string,oldCommand,StringComparison.OrdinalIgnoreCase))continue;
                key.SetValue("",newCommand);
                changed=true;
            }
        }
        if(changed)SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero);
    }
    public static void Register(string[] extensions){
        if(extensions.Length==0)throw new InvalidOperationException(Ui.T("확장자를 선택해 주세요."));
        if(extensions.Any(e=>!Extensions.Contains(e)))throw new ArgumentException("extension");
        string executable=Application.ExecutablePath;
        string iconPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","LumaView.ico");if(!File.Exists(iconPath))iconPath=executable;
        using(var caps=Registry.CurrentUser.CreateSubKey(Capabilities)){caps.SetValue("ApplicationName","DollView");caps.SetValue("ApplicationDescription","DollView image, sequence and video viewer");caps.SetValue("ApplicationIcon","\""+iconPath+"\",0");}
        using(var registered=Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))registered.SetValue("LumaView",Capabilities);
        using(var app=Registry.CurrentUser.CreateSubKey(@"Software\Classes\Applications\DollView.exe")){
            app.SetValue("FriendlyAppName","DollView");using(var icon=app.CreateSubKey("DefaultIcon"))icon.SetValue("","\""+iconPath+"\",0");using(var command=app.CreateSubKey(@"shell\open\command"))command.SetValue("",OpenCommand(executable));
            using(var supported=app.CreateSubKey("SupportedTypes"))foreach(string extension in extensions)supported.SetValue(extension,"");
        }
        foreach(string extension in extensions){
            string progId="LumaView.File"+extension;
            using(var prog=Registry.CurrentUser.CreateSubKey(@"Software\Classes\"+progId)){
                prog.SetValue("","DollView "+extension.ToUpperInvariant()+" file");
                string overlay=ThumbnailOverlay(executable,extension);
                if(overlay!=null)prog.SetValue("TypeOverlay",overlay);
                using(var icon=prog.CreateSubKey("DefaultIcon"))icon.SetValue("",extension==".pdf"?overlay:"\""+iconPath+"\",0");
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
            caps.SetValue("ApplicationName",Ui.T("DollView — 선택한 확장자"));
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
        if(args.Length==1&&(args[0]=="--enable-all-thumbnails"||args[0]=="--disable-all-thumbnails"||args[0]=="--unregister-all-thumbnails")){
            try{
                if(args[0]=="--unregister-all-thumbnails")ExplorerThumbnail.UnregisterAll();
                else ExplorerThumbnail.SetAllEnabled(args[0]=="--enable-all-thumbnails");
            }catch(Exception ex){Console.Error.WriteLine(ex.Message);Environment.ExitCode=1;}
            return true;
        }
        if(args.Length==1&&(args[0]=="--enable-pdf-thumbnails"||args[0]=="--disable-pdf-thumbnails"||args[0]=="--unregister-pdf-thumbnails")){
            try{
                if(args[0]=="--unregister-pdf-thumbnails")ExplorerThumbnail.UnregisterPdf();
                else ExplorerThumbnail.SetPdfEnabled(args[0]=="--enable-pdf-thumbnails");
            }catch(Exception ex){Console.Error.WriteLine(ex.Message);Environment.ExitCode=1;}
            return true;
        }
        if(args.Length==1&&(args[0]=="--enable-model-thumbnails"||args[0]=="--disable-model-thumbnails")){
            try{ExplorerThumbnail.SetModelsEnabled(args[0]=="--enable-model-thumbnails");}
            catch(Exception ex){Console.Error.WriteLine(ex.Message);Environment.ExitCode=1;}
            return true;
        }
        if(args.Length==1&&(args[0]=="--enable-explorer-thumbnails"||args[0]=="--disable-explorer-thumbnails")){
            try{ExplorerThumbnail.SetEnabled(args[0]=="--enable-explorer-thumbnails");}
            catch(Exception ex){Console.Error.WriteLine(ex.Message);Environment.ExitCode=1;}
            return true;
        }
        if(args.Length==0||!new[]{"--register-associations","--unregister-associations","--prepare-default-associations","--open-selected-defaults"}.Contains(args[0]))return false;
        try{
            if(args[0]=="--register-associations"&&args.Length==1)Register(Extensions);
            else if(args[0]=="--unregister-associations")UnregisterOwned();
            else{
                if(args.Length!=2)throw new ArgumentException("Expected a comma-separated extension list.");
                var selected=args[1].Split(',').Select(e=>e.Trim().ToLowerInvariant()).Distinct().ToArray();
                if(args[0]=="--register-associations"){
                    Register(selected);
                    PrepareSelectedTypes(selected);
                }
                else if(args[0]=="--prepare-default-associations")PrepareSelectedTypes(selected);
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
        string appPath=@"Software\Classes\Applications\DollView.exe";
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
    readonly ListView list=new ListView{Dock=DockStyle.Fill,View=View.Details,CheckBoxes=true,FullRowSelect=true,HideSelection=false,BorderStyle=BorderStyle.None,ShowItemToolTips=true};
    readonly WrapLabel notice=ModernUi.Text("",true);
    readonly ImageList rowHeight=new ImageList();
    readonly CheckBox thumbnails=new CheckBox{Name="explorerThumbnails",AutoSize=true,ThreeState=true,AutoCheck=false,Margin=new Padding(0,6,0,8)};
    public AssociationSettings(){
        Dock=DockStyle.Fill;
        var content=new SettingsStack();content.Controls.Add(ModernUi.Text(Ui.T("파일 연결"),false,21,true));
        thumbnails.Text=ComponentSetup.Say("Windows 탐색기 썸네일 (이미지 · PDF · 3D 모델)","Windows Explorer thumbnails (images, PDFs and 3D models)");
        thumbnails.Click+=delegate{
            try{ExplorerThumbnail.SetAllEnabled(thumbnails.CheckState!=CheckState.Checked);}
            catch(Exception ex){MessageBox.Show(this,ex.Message,Ui.T("파일 연결"),MessageBoxButtons.OK,MessageBoxIcon.Warning);}
            thumbnails.CheckState=ExplorerThumbnail.CombinedState;
        };
        content.Controls.Add(thumbnails);
        content.Controls.Add(ModernUi.Text(ComponentSetup.Say("묶음을 고르거나 아래에서 확장자를 조정하세요. 연결을 누르면 Windows 기본 앱 설정이 열립니다.","Choose a group or adjust individual extensions below. Click Connect to open Windows default app settings."),true));
        list.Columns.Add(Ui.T("확장자"),90);list.Columns.Add(Ui.T("연결 후보"),120);list.Columns.Add(Ui.T("현재 기본 앱"),250);
        rowHeight.ImageSize=new Size(1,32);list.SmallImageList=rowHeight;
        list.Resize+=delegate{int unit=Font.Height;list.Columns[0].Width=unit*5;list.Columns[1].Width=unit*7;list.Columns[2].Width=Math.Max(unit*8,list.ClientSize.Width-unit*12-SystemInformation.VerticalScrollBarWidth-4);};
        list.OwnerDraw=true;
        list.DrawColumnHeader+=delegate(object sender,DrawListViewColumnHeaderEventArgs e){using(var brush=new SolidBrush(Ui.Surface))e.Graphics.FillRectangle(brush,e.Bounds);TextRenderer.DrawText(e.Graphics,e.Header.Text,Font,Rectangle.Inflate(e.Bounds,-8,0),ModernUi.Muted,TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.NoPrefix);};
        list.DrawItem+=delegate(object sender,DrawListViewItemEventArgs e){e.DrawDefault=true;};list.DrawSubItem+=delegate(object sender,DrawListViewSubItemEventArgs e){e.DrawDefault=true;};
        var all=new ModernButton{Text=Ui.T("전체 선택")};all.Click+=delegate{foreach(ListViewItem item in list.Items)item.Checked=true;};
        var clear=new ModernButton{Text=ComponentSetup.Say("선택 해제","Clear")};clear.Click+=delegate{foreach(ListViewItem item in list.Items)item.Checked=false;};
        var register=new ModernButton{Text=ComponentSetup.Say("선택한 형식 연결","Connect selected"),Primary=true};register.Click+=delegate{
            if(connecting)return;connecting=true;register.Enabled=false;
            try{
                string[] selected=SelectedExtensions;FileAssociations.Register(selected);RefreshRows();FileAssociations.OpenSelectedDefaults(selected);
                notice.Text=Ui.T("Windows에서 선택할 확장자: ")+String.Join(", ",selected)+"\n"+Ui.T("DollView를 선택해 완료하세요. 연결 결과는 복귀 시 갱신됩니다.");
            }catch(Exception ex){notice.Text=ex.Message;}
            finally{connecting=false;register.Enabled=true;}
        };
        var refresh=new ModernButton{Text=Ui.T("새로고침")};refresh.Click+=delegate{RefreshRows();};
        content.Controls.Add(ModernUi.Actions(
            GroupButton(ComponentSetup.Say("이미지·PDF","Images / PDF"),Album.Supported),
            GroupButton(ComponentSetup.Say("영상","Videos"),MediaFiles.IsVideo),
            GroupButton(ComponentSetup.Say("3D 모델","3D models"),ModelFiles.Supported)));
        content.Controls.Add(ModernUi.Actions(all,clear,refresh));
        var listHost=new Panel{Height=330,MinimumSize=new Size(0,240),Margin=new Padding(0,16,0,16)};listHost.Controls.Add(list);content.Controls.Add(listHost);
        var defaults=new ModernButton{Text=ComponentSetup.Say("Windows 설정 열기","Windows settings")};defaults.Click+=delegate{try{FileAssociations.OpenDefaults();}catch(Exception ex){notice.Text=ex.Message;}};
        content.Controls.Add(ModernUi.Actions(register,defaults));notice.Margin=new Padding(0,16,0,0);content.Controls.Add(notice);Controls.Add(ModernUi.Scroll(content));
        notice.Text=ComponentSetup.Say("등록 상태와 현재 기본 앱을 함께 확인할 수 있습니다.","Registration and the current default app are shown separately.");
        RefreshRows();Ui.Apply(this);
        Load+=delegate{settingsForm=FindForm();if(settingsForm!=null)settingsForm.Activated+=RefreshOnReturn;};
    }
    ModernButton GroupButton(string caption,Func<string,bool> accepts){
        var button=new ModernButton{Text=caption};
        button.Click+=delegate{
            var group=list.Items.Cast<ListViewItem>().Where(item=>accepts("file"+(string)item.Tag)).ToArray();
            bool select=group.Any(item=>!item.Checked);
            list.BeginUpdate();
            try{foreach(var item in group)item.Checked=select;}
            finally{list.EndUpdate();}
        };
        return button;
    }
    void RefreshOnReturn(object sender,EventArgs e){if(!IsDisposed&&Visible&&!connecting)RefreshRows();}
    protected override void Dispose(bool disposing){if(disposing){if(settingsForm!=null){settingsForm.Activated-=RefreshOnReturn;settingsForm=null;}rowHeight.Dispose();}base.Dispose(disposing);}
    void RefreshRows(){
        thumbnails.CheckState=ExplorerThumbnail.CombinedState;
        var selected=SelectedExtensions;list.BeginUpdate();try{list.Items.Clear();foreach(string extension in FileAssociations.Extensions){bool registered=FileAssociations.Registered(extension);string current=FileAssociations.CurrentApp(extension);list.Items.Add(new ListViewItem(new[]{extension,Ui.T(registered?"등록됨":"미등록"),current}){Tag=extension,Checked=selected.Contains(extension),ToolTipText=extension+" · "+current});}}finally{list.EndUpdate();}
    }
}
}
