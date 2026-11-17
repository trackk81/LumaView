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
    const long MaxPackageBytes=200L*1024*1024;
    public sealed class Asset { public string name{get;set;} public string browser_download_url{get;set;} public long size{get;set;} }
    public sealed class Release { public string tag_name{get;set;} public bool draft{get;set;} public bool prerelease{get;set;} public Asset[] assets{get;set;} }
    public sealed class Update { public Version Version; public string Tag; public Asset Package,Checksums; }
    static string Say(string ko,string en){return Ui.Language=="en"?en:ko;}
    internal static Version CurrentVersion { get {var v=Assembly.GetExecutingAssembly().GetName().Version;return new Version(v.Major,v.Minor,v.Build);}}
    internal static Update ParseRelease(string json){
        var release=new JavaScriptSerializer{MaxJsonLength=1048576}.Deserialize<Release>(json);
        if(release==null||release.draft||release.prerelease)return null;
        string tag=release.tag_name??"";Version version;
        if(!Regex.IsMatch(tag,@"^v\d+\.\d+\.\d+$")||!Version.TryParse(tag.Substring(1),out version))throw new InvalidDataException("Invalid release version.");
        string filename="LumaView-"+version+"-win-x64.zip";
        var assets=release.assets??new Asset[0];
        var package=assets.SingleOrDefault(a=>a.name==filename);
        var sums=assets.SingleOrDefault(a=>a.name=="SHA256SUMS.txt");
        if(package==null||sums==null||package.size<=0||package.size>MaxPackageBytes||sums.size<=0||sums.size>65536)throw new InvalidDataException("Release is missing its portable ZIP or checksums.");
        foreach(var asset in new[]{package,sums}){
            string expected="https://github.com/"+Repository+"/releases/download/"+tag+"/"+asset.name;
            if(!String.Equals(asset.browser_download_url,expected,StringComparison.Ordinal))throw new InvalidDataException("Unexpected release download address.");
        }
        return new Update{Version=version,Tag=tag,Package=package,Checksums=sums};
    }
    internal static string ExpectedHash(string sums,string name){
        var matches=sums.Split('\n').Select(line=>Regex.Match(line.Trim(),@"^([a-fA-F0-9]{64})\s+\*?(.+)$")).Where(m=>m.Success&&m.Groups[2].Value==name).ToArray();
        if(matches.Length!=1)throw new InvalidDataException("Missing or duplicate ZIP checksum.");
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
                var buffer=new byte[65536];int count;while((count=await input.ReadAsync(buffer,0,buffer.Length,token))>0){total+=count;if(total>asset.size||total>MaxPackageBytes)throw new InvalidDataException("ZIP size mismatch.");await output.WriteAsync(buffer,0,count,token);progress((int)(total*100/asset.size));}
            }
            if(total!=asset.size)throw new InvalidDataException("Incomplete ZIP download.");
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
                    if(MessageBox.Show(owner,Say("새 버전 ","New version ")+update.Version+Say(" ZIP을 내려받을까요?\n파일 검증 후 압축 파일이 있는 폴더를 엽니다."," is available.\nDownload and verify the portable ZIP, then open its folder?"),"LumaView",MessageBoxButtons.YesNo,MessageBoxIcon.Information)!=DialogResult.Yes)return;
                    string sums=await ReadSmall(client,update.Checksums.browser_download_url,65536,cancel.Token);
                    string expected=ExpectedHash(sums,update.Package.name);
                    string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LumaView","Updates",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
                    temporary=Path.Combine(folder,update.Package.name+".download");
                    await Download(client,update.Package,temporary,percent=>{if(!button.IsDisposed)button.Text=percent+"%";},cancel.Token);
                    if(!await Task.Run(()=>VerifyHash(temporary,expected)))throw new InvalidDataException(Say("다운로드 파일 검증에 실패했습니다. 다시 시도해 주세요.","Download verification failed. Please try again."));
                    cancel.Token.ThrowIfCancellationRequested();string packagePath=Path.Combine(folder,update.Package.name);File.Move(temporary,packagePath);temporary=null;
                    MessageBox.Show(owner,Say("ZIP을 내려받고 검증했습니다.\n압축을 모두 푼 뒤 새 폴더의 LumaView.exe를 실행하세요.","The ZIP has been downloaded and verified.\nExtract all files, then run LumaView.exe from the new folder."),"LumaView");Process.Start(new ProcessStartInfo("explorer.exe", "/select,\""+packagePath+"\""){UseShellExecute=true});
                }
            }catch(OperationCanceledException){if(!owner.IsDisposed)MessageBox.Show(owner,Say("업데이트 확인 시간이 초과되었습니다. 다시 시도해 주세요.","The update request timed out. Please try again."),"LumaView");}
            catch(Exception ex){if(!owner.IsDisposed)MessageBox.Show(owner,Say("업데이트를 확인하거나 다운로드하지 못했습니다.\n","Could not check or download the update.\n")+ex.Message,"LumaView",MessageBoxButtons.OK,MessageBoxIcon.Information);}
            finally{owner.FormClosed-=closed;if(temporary!=null)try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}if(!button.IsDisposed){button.Text=Ui.T("업데이트");button.Enabled=true;}}
        }
    }
}
}

