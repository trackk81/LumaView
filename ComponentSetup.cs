using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LumaView {
static class ComponentSetup {
    internal const string WebViewDownload="https://go.microsoft.com/fwlink/p/?LinkId=2124703";
    internal static string Say(string ko,string en){return Ui.Language=="en"?en:ko;}
    internal static string WebViewVersion(){
        try{
            var sdk=Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Microsoft.Web.WebView2.Core.dll"));
            return (string)sdk.GetType("Microsoft.Web.WebView2.Core.CoreWebView2Environment").GetMethod("GetAvailableBrowserVersionString",new[]{typeof(string)}).Invoke(null,new object[]{null})??"";
        }
        catch(Exception){return "";}
    }
    internal static int NetRelease(){
        using(var root=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry32))
        using(var key=root.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
            return key==null?0:Convert.ToInt32(key.GetValue("Release",0));
    }
    internal static bool HasFiles(params string[] names){return names.All(n=>File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,n)));}
    internal static string[][] BundledComponents(){return new[]{
        new[]{Say("영상 재생 · mpv/FFmpeg","Video · mpv/FFmpeg"),"video-runtime/mpv.exe","video-runtime/d3dcompiler_43.dll","video-runtime/mpv/fonts.conf"},
        new[]{Say("확장 이미지 코덱","Extended image codecs"),"codecs/magick.exe"},
        new[]{Say("3D 뷰어 · HDR 맵 4종","3D viewer · 4 HDR maps"),"Microsoft.Web.WebView2.Core.dll","Microsoft.Web.WebView2.WinForms.dll","WebView2Loader.dll","model-viewer/index.html","model-viewer/viewer.js","model-viewer/effects.js","model-viewer/environment.js","model-viewer/environments/studio_small_09.hdr","model-viewer/environments/studio_small_03.hdr","model-viewer/environments/venice_sunset.hdr","model-viewer/environments/forest_slope.hdr"}
    };}
    internal static async Task PrepareAsync(string[] extensions,Action<string> report,CancellationToken token){
        var missing=BundledComponents().Where(c=>!HasFiles(c.Skip(1).ToArray())).Select(c=>c[0]).ToArray();
        if(missing.Length>0)throw new FileNotFoundException(Say("배포 파일이 누락되었습니다: ","Bundled files are missing: ")+String.Join(", ",missing)+Say("\n받은 ZIP을 전부 압축 해제한 폴더에서 실행하세요.","\nRun the app from a fully extracted release ZIP."));
        await PrepareCoreAsync(extensions,WebViewVersion,()=>InstallWebViewAsync(report,token),selected=>{
            report(Say("파일 연결을 등록하고 Windows 설정을 여는 중…","Registering file types and opening Windows Settings…"));
            FileAssociations.Register(selected);FileAssociations.OpenSelectedDefaults(selected);
        },token);
    }
    // Keep orchestration separate from downloads and Windows changes so failed
    // or cancelled setup cannot silently continue to file registration.
    internal static async Task PrepareCoreAsync(string[] extensions,Func<string> detect,Func<Task> install,Action<string[]> connect,CancellationToken token){
        if(extensions==null||extensions.Any(e=>!FileAssociations.Extensions.Contains(e)))throw new ArgumentException("extensions");
        var selected=extensions.Distinct().ToArray();token.ThrowIfCancellationRequested();
        if(String.IsNullOrEmpty(detect())){
            await install();token.ThrowIfCancellationRequested();
            if(String.IsNullOrEmpty(detect()))throw new InvalidOperationException(Say("설치 후에도 WebView2를 찾지 못했습니다. PC를 다시 시작하거나 관리자에게 설치 정책을 확인해 주세요.","WebView2 is still unavailable after installation. Restart Windows or check installation policies with your administrator."));
        }
        token.ThrowIfCancellationRequested();if(selected.Length>0)connect(selected);
    }
    internal static async Task DownloadInstallerAsync(string destination,Action<string> report,CancellationToken token){
        ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(token)){
        timeout.CancelAfter(TimeSpan.FromMinutes(3));token=timeout.Token;
        using(var client=new HttpClient{Timeout=TimeSpan.FromMinutes(2)}){
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LumaView/"+AppUpdater.CurrentVersion);
            using(var response=await client.GetAsync(WebViewDownload,HttpCompletionOption.ResponseHeadersRead,token)){
                response.EnsureSuccessStatusCode();
                if(response.RequestMessage.RequestUri.Scheme!="https")throw new InvalidDataException("HTTPS is required for component downloads.");
                const int limit=16*1024*1024;
                if(response.Content.Headers.ContentLength>limit)throw new InvalidDataException("Installer download is too large.");
                using(var input=await response.Content.ReadAsStreamAsync())
                using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true)){
                    byte[] buffer=new byte[65536];int count;long total=0;int shown=-1;
                    while((count=await input.ReadAsync(buffer,0,buffer.Length,token))>0){
                        total+=count;if(total>limit)throw new InvalidDataException("Installer download is too large.");
                        await output.WriteAsync(buffer,0,count,token);
                        int kb=(int)(total/1024);if(kb/256!=shown){shown=kb/256;report(Say("WebView2 설치 프로그램 다운로드 중: ","Downloading WebView2 installer: ")+kb+" KB");}
                    }
                    if(total==0||(response.Content.Headers.ContentLength.HasValue&&total!=response.Content.Headers.ContentLength.Value))throw new InvalidDataException("Incomplete installer download.");
                }
            }
        }
        }
    }
    internal static async Task VerifyMicrosoftInstallerAsync(string path,CancellationToken token){
        // Windows validates the signature, timestamp and trust chain. Checking
        // the signer name alone would accept a forged embedded certificate.
        string script="$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; $s=Get-AuthenticodeSignature -LiteralPath '"+path.Replace("'","''")+"'; if ($s.Status -ne 'Valid' -or $null -eq $s.SignerCertificate -or $s.SignerCertificate.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName,$false) -ne 'Microsoft Corporation') { exit 1 }; exit 0";
        var start=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe"),"-NoLogo -NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(script))){UseShellExecute=false,CreateNoWindow=true};
        // An app launched by PowerShell 7 can inherit its incompatible module
        // paths. Resolve the signature cmdlet only from Windows' own modules.
        start.EnvironmentVariables["PSModulePath"]=Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\Modules");
        using(var process=Process.Start(start)){
            var watch=Stopwatch.StartNew();
            try{while(!process.HasExited){token.ThrowIfCancellationRequested();if(watch.Elapsed>TimeSpan.FromSeconds(60))throw new TimeoutException(Say("설치 파일 서명 확인 시간이 초과되었습니다.","Installer signature verification timed out."));await Task.Delay(100,token);}}
            catch{if(!process.HasExited)process.Kill();throw;}
            if(process.ExitCode!=0)throw new InvalidDataException(Say("Microsoft 서명을 검증하지 못해 설치를 중단했습니다. 인터넷 연결을 확인하고 다시 시도하세요.","Installation stopped because the Microsoft signature could not be verified. Check your connection and try again."));
        }
    }
    internal static async Task InstallWebViewAsync(Action<string> report,CancellationToken token){
        string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LumaView","ComponentSetup",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);string installer=Path.Combine(folder,"MicrosoftEdgeWebview2Setup.exe");
        bool installerRunning=false;
        try{
            report(Say("WebView2를 Microsoft에서 내려받는 중…","Downloading WebView2 from Microsoft…"));
            await DownloadInstallerAsync(installer,report,token);
            report(Say("Microsoft 서명 확인 중…","Verifying the Microsoft signature…"));
            await VerifyMicrosoftInstallerAsync(installer,token);token.ThrowIfCancellationRequested();
            report(Say("WebView2 설치 중… 잠시 기다려 주세요. 창을 닫아도 시작된 설치는 계속됩니다.","Installing WebView2… Please wait. An installation already started will continue if this window closes."));
            using(var process=Process.Start(new ProcessStartInfo(installer,"/silent /install"){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=folder})){
                installerRunning=true;var watch=Stopwatch.StartNew();
                while(!process.HasExited){token.ThrowIfCancellationRequested();if(watch.Elapsed>TimeSpan.FromMinutes(10))throw new TimeoutException(Say("설치가 아직 진행 중입니다. 잠시 후 ‘다시 확인’을 누르세요.","Installation is still running. Wait a little, then click Check again."));await Task.Delay(250,token);}
                installerRunning=false;
                for(int attempt=0;attempt<20&&String.IsNullOrEmpty(WebViewVersion());attempt++)await Task.Delay(250,token);
                if(String.IsNullOrEmpty(WebViewVersion()))throw new InvalidOperationException(Say("WebView2 설치를 완료하지 못했습니다. 종료 코드: ","WebView2 installation did not complete. Exit code: ")+process.ExitCode+Say("\n인터넷 연결과 회사 PC 설치 정책을 확인한 뒤 다시 시도하세요.","\nCheck your connection and device installation policies, then try again."));
            }
        }finally{if(!installerRunning){try{File.Delete(installer);Directory.Delete(folder);}catch(IOException){}catch(UnauthorizedAccessException){}}}
    }
}

sealed class ComponentSettings:UserControl {
    readonly Func<string[]> selection;
    readonly TableLayoutPanel rows=new TableLayoutPanel{Dock=DockStyle.Top,Height=190,ColumnCount=2,RowCount=5,Padding=new Padding(6)};
    readonly CheckBox connect=new CheckBox{Text=ComponentSetup.Say("파일 연결도 함께 설정","Set up file associations too"),Checked=true,AutoSize=true,Location=new Point(12,208),Name="setupFileAssociations"};
    readonly Label selected=new Label{Location=new Point(12,240),Size=new Size(545,44)};
    readonly Button prepare=new Button{Text=ComponentSetup.Say("다운로드 및 연결","Download and connect"),Location=new Point(12,300),Size=new Size(215,36),Name="prepareComponents"};
    readonly Button refresh=new Button{Text=ComponentSetup.Say("다시 확인","Check again"),Location=new Point(240,300),Size=new Size(130,36)};
    readonly Label status=new Label{Location=new Point(12,349),Size=new Size(545,85),AutoEllipsis=true,Name="componentStatus"};
    readonly CancellationTokenSource cancel=new CancellationTokenSource();
    bool busy;
    internal ComponentSettings(Func<string[]> selectedExtensions){
        selection=selectedExtensions;Dock=DockStyle.Fill;AutoScroll=true;
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,47));rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,53));
        for(int i=0;i<5;i++)rows.RowStyles.Add(new RowStyle(SizeType.Percent,20));
        Controls.Add(rows);Controls.Add(connect);Controls.Add(selected);Controls.Add(prepare);Controls.Add(refresh);Controls.Add(status);
        status.Text=ComponentSetup.Say("필요한 WebView2를 자동 설치합니다. 파일 연결은 열린 Windows 설정에서 LumaView를 최종 선택하세요.","Missing WebView2 is installed automatically. Finish file associations by choosing LumaView in Windows Settings.");
        prepare.Click+=async delegate{await Prepare();};refresh.Click+=delegate{RefreshStatus();};
        connect.CheckedChanged+=delegate{prepare.Text=connect.Checked?ComponentSetup.Say("다운로드 및 연결","Download and connect"):ComponentSetup.Say("다운로드 및 설치","Download and install");RefreshSelection();};
        VisibleChanged+=delegate{if(Visible&&!busy)RefreshStatus();};RefreshStatus();Ui.Apply(this);
    }
    void AddRow(int row,string name,string value){
        rows.Controls.Add(new Label{Text=name,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,row);
        rows.Controls.Add(new Label{Text=value,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true},1,row);
    }
    internal void RefreshStatus(){
        foreach(Control child in rows.Controls.Cast<Control>().ToArray())child.Dispose();rows.Controls.Clear();
        string net;try{int release=ComponentSetup.NetRelease();net=release>=533320?"4.8.1+":release>=528040?"4.8":ComponentSetup.Say("4.8 이상 설치 필요","4.8 or later required");}catch{net=ComponentSetup.Say("확인하지 못함","Could not verify");}
        AddRow(0,".NET Framework",net);
        string web=ComponentSetup.WebViewVersion();AddRow(1,"3D · Microsoft WebView2",String.IsNullOrEmpty(web)?ComponentSetup.Say("설치 필요 · 자동 다운로드","Missing · automatic download"):ComponentSetup.Say("준비됨 · ","Ready · ")+web);
        int row=2;foreach(var component in ComponentSetup.BundledComponents())AddRow(row++,component[0],ComponentSetup.HasFiles(component.Skip(1).ToArray())?ComponentSetup.Say("포함됨 · 설치 불필요","Included · no installation needed"):ComponentSetup.Say("누락 · ZIP 전체 압축 해제 필요","Missing · extract the entire ZIP"));
        RefreshSelection();Ui.Apply(rows);
    }
    void RefreshSelection(){
        string[] choices=selection();
        selected.Text=!connect.Checked?ComponentSetup.Say("구성 요소만 준비합니다.","Only components will be prepared."):choices.Length==0?ComponentSetup.Say("연결 대상: 지원 형식 전체. 일부만 연결하려면 ‘파일 연결’ 탭에서 체크하세요.","File types: all supported formats. To limit this, check formats in the File associations tab."):ComponentSetup.Say("연결 대상: ","File types: ")+String.Join(", ",choices);
    }
    async Task Prepare(){
        if(busy)return;busy=true;prepare.Enabled=refresh.Enabled=connect.Enabled=false;
        string[] chosen=connect.Checked?selection():new string[0];if(connect.Checked&&chosen.Length==0)chosen=FileAssociations.Extensions;
        try{
            status.Text=ComponentSetup.Say("구성 요소 확인 중…","Checking components…");
            await ComponentSetup.PrepareAsync(chosen,text=>{if(!IsDisposed)status.Text=text;},cancel.Token);
            if(!IsDisposed)status.Text=chosen.Length>0?ComponentSetup.Say("구성 요소가 준비되었습니다. 파일 연결 후보로 등록했으며, 열린 Windows 설정에서 LumaView를 선택하면 완료됩니다.","Components are ready and file types are registered. Choose LumaView in the Windows Settings page to finish."):ComponentSetup.Say("구성 요소가 준비되었습니다. 3D 모델을 다시 열면 사용할 수 있습니다.","Components are ready. Reopen your 3D model to use them.");
        }catch(OperationCanceledException){if(!IsDisposed)status.Text=ComponentSetup.Say("다운로드가 중단되었거나 시간이 초과되었습니다. 다시 시도하세요.","The download was cancelled or timed out. Try again.");}
        catch(Exception ex){if(!IsDisposed)status.Text=ex.Message;}
        finally{busy=false;if(!IsDisposed){prepare.Enabled=refresh.Enabled=connect.Enabled=true;RefreshStatus();}}
    }
    protected override void Dispose(bool disposing){if(disposing&&!IsDisposed){cancel.Cancel();cancel.Dispose();}base.Dispose(disposing);}
}
}
