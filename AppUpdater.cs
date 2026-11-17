using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace LumaView {
static class AppUpdater {
    internal const string Repository="trackk81/LumaView";
    const long MaxInstallerBytes=200L*1024*1024;
    public sealed class Asset { public string name{get;set;} public string browser_download_url{get;set;} public long size{get;set;} }
    public sealed class Release { public string tag_name{get;set;} public bool draft{get;set;} public bool prerelease{get;set;} public Asset[] assets{get;set;} }
    public sealed class Update { public Version Version; public string Tag; public Asset Installer,Checksums; }
    static string Say(string ko,string en){return Ui.Language=="en"?en:ko;}
    internal static Version CurrentVersion { get {var v=Assembly.GetExecutingAssembly().GetName().Version;return new Version(v.Major,v.Minor,v.Build);}}
    internal static Update ParseRelease(string json){
        var release=new JavaScriptSerializer{MaxJsonLength=1048576}.Deserialize<Release>(json);
        if(release==null||release.draft||release.prerelease)return null;
        string tag=release.tag_name??"";Version version;
        if(!Regex.IsMatch(tag,@"^v\d+\.\d+\.\d+$")||!Version.TryParse(tag.Substring(1),out version))throw new InvalidDataException("Invalid release version.");
        string filename="LumaView-Setup-"+version+"-x64.exe";
        var assets=release.assets??new Asset[0];
        var installer=assets.SingleOrDefault(a=>a.name==filename);
        var sums=assets.SingleOrDefault(a=>a.name=="SHA256SUMS.txt");
        if(installer==null||sums==null||installer.size<=0||installer.size>MaxInstallerBytes||sums.size<=0||sums.size>65536)throw new InvalidDataException("Release is missing its installer or checksums.");
        foreach(var asset in new[]{installer,sums}){
            string expected="https://github.com/"+Repository+"/releases/download/"+tag+"/"+asset.name;
            if(!String.Equals(asset.browser_download_url,expected,StringComparison.Ordinal))throw new InvalidDataException("Unexpected release download address.");
        }
        return new Update{Version=version,Tag=tag,Installer=installer,Checksums=sums};
    }
    internal static string ExpectedHash(string sums,string name){
        var matches=sums.Split('\n').Select(line=>Regex.Match(line.Trim(),@"^([a-fA-F0-9]{64})\s+\*?(.+)$")).Where(m=>m.Success&&m.Groups[2].Value==name).ToArray();
        if(matches.Length!=1)throw new InvalidDataException("Missing or duplicate installer checksum.");
        return matches[0].Groups[1].Value.ToLowerInvariant();
    }
    internal static bool VerifyHash(string path,string expected){using(var stream=File.OpenRead(path))using(var hash=SHA256.Create())return String.Equals(BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", ""),expected,StringComparison.OrdinalIgnoreCase);}
    static async Task<string> ReadSmall(HttpClient client,string url,int limit,CancellationToken token){
        using(var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,token)){
            response.EnsureSuccessStatusCode();using(var input=await response.Content.ReadAsStreamAsync())using(var memory=new MemoryStream()){
                var buffer=new byte[8192];int count;while((count=await input.ReadAsync(buffer,0,buffer.Length,token))>0){if(memory.Length+count>limit)throw new InvalidDataException("Response too large.");memory.Write(buffer,0,count);}
                return System.Text.Encoding.UTF8.GetString(memory.ToArray());
            }
        }
    }
    static async Task Download(HttpClient client,Asset asset,string path,Action<int> progress,CancellationToken token){
        using(var response=await client.GetAsync(asset.browser_download_url,HttpCompletionOption.ResponseHeadersRead,token)){
            response.EnsureSuccessStatusCode();long total=0;using(var input=await response.Content.ReadAsStreamAsync())using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true)){
                var buffer=new byte[65536];int count;while((count=await input.ReadAsync(buffer,0,buffer.Length,token))>0){total+=count;if(total>asset.size||total>MaxInstallerBytes)throw new InvalidDataException("Installer size mismatch.");await output.WriteAsync(buffer,0,count,token);progress((int)(total*100/asset.size));}
            }
            if(total!=asset.size)throw new InvalidDataException("Incomplete installer download.");
        }
    }
    public static async Task CheckAsync(Form owner,Button button){
        button.Enabled=false;button.Text=Say("확인 중…","Checking…");
        using(var cancel=new CancellationTokenSource(TimeSpan.FromMinutes(5))){
            FormClosedEventHandler closed=delegate{cancel.Cancel();};owner.FormClosed+=closed;
            string temporary=null;
            try{
                ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
                using(var client=new HttpClient{Timeout=TimeSpan.FromSeconds(30)}){
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("LumaView/"+CurrentVersion);
                    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                    string releaseJson;
                    using(var response=await client.GetAsync("https://api.github.com/repos/"+Repository+"/releases/latest",cancel.Token)){
                        if(response.StatusCode==HttpStatusCode.NotFound){MessageBox.Show(owner,Say("아직 공개된 업데이트가 없습니다.","No public release is available yet."),"LumaView");return;}
                        response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>1048576)throw new InvalidDataException("Response too large.");releaseJson=await response.Content.ReadAsStringAsync();if(releaseJson.Length>1048576)throw new InvalidDataException("Response too large.");
                    }
                    var update=ParseRelease(releaseJson);
                    if(update==null||update.Version<=CurrentVersion){MessageBox.Show(owner,Say("최신 버전입니다. 현재 버전: ","You are up to date. Current version: ")+CurrentVersion,"LumaView");return;}
                    if(MessageBox.Show(owner,Say("새 버전 ","New version ")+update.Version+Say("을 설치할까요?\n다운로드 후 LumaView를 닫고 설치 프로그램을 실행합니다."," is available.\nDownload it, close LumaView, and start setup?"),"LumaView",MessageBoxButtons.YesNo,MessageBoxIcon.Information)!=DialogResult.Yes)return;
                    string sums=await ReadSmall(client,update.Checksums.browser_download_url,65536,cancel.Token);
                    string expected=ExpectedHash(sums,update.Installer.name);
                    string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LumaView","Updates",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
                    temporary=Path.Combine(folder,update.Installer.name+".download");
                    await Download(client,update.Installer,temporary,percent=>{if(!button.IsDisposed)button.Text=percent+"%";},cancel.Token);
                    if(!await Task.Run(()=>VerifyHash(temporary,expected)))throw new InvalidDataException(Say("다운로드 파일 검증에 실패했습니다. 다시 시도해 주세요.","Download verification failed. Please try again."));
                    cancel.Token.ThrowIfCancellationRequested();string installer=Path.Combine(folder,update.Installer.name);File.Move(temporary,installer);temporary=null;
                    Process.Start(new ProcessStartInfo(installer){UseShellExecute=true});Application.Exit();
                }
            }catch(OperationCanceledException){if(!owner.IsDisposed)MessageBox.Show(owner,Say("업데이트 확인 시간이 초과되었습니다. 다시 시도해 주세요.","The update request timed out. Please try again."),"LumaView");}
            catch(Exception ex){if(!owner.IsDisposed)MessageBox.Show(owner,Say("업데이트를 확인하거나 다운로드하지 못했습니다.\n","Could not check or download the update.\n")+ex.Message,"LumaView",MessageBoxButtons.OK,MessageBoxIcon.Information);}
            finally{owner.FormClosed-=closed;if(temporary!=null)try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}if(!button.IsDisposed){button.Text=Ui.T("업데이트");button.Enabled=true;}}
        }
    }
}
}
