using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace LumaView {
sealed class UpdateFile {public string Name{get;set;}public long Length{get;set;}public string Hash{get;set;}}
sealed class UpdateJob {
    public string Id{get;set;}public string TargetRoot{get;set;}public string WorkRoot{get;set;}
    public string FromVersion{get;set;}public string ToVersion{get;set;}public string RestartPath{get;set;}public string Language{get;set;}
    public int ParentPid{get;set;}public long ParentStarted{get;set;}public UpdateFile[] Files{get;set;}
}
static class UpdateFiles {
    internal static string LockName(string root){using(var hash=SHA256.Create())return "Local\\LumaView.Install."+BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(root).TrimEnd('\\').ToUpperInvariant()))).Replace("-","");}
    internal static string Hash(string path){using(var input=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant();}
    internal static string Inside(string root,string relative){
        if(String.IsNullOrWhiteSpace(relative)||Path.IsPathRooted(relative))throw new InvalidDataException("Invalid update path.");
        string normalized=relative.Replace('\\','/');
        foreach(string part in normalized.Split('/')){
            if(part.Length==0||part=="."||part==".."||part.EndsWith(".")||part.EndsWith(" ")||part.IndexOfAny(Path.GetInvalidFileNameChars())>=0||Regex.IsMatch(part,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)",RegexOptions.IgnoreCase))throw new InvalidDataException("Unsafe update path: "+relative);
        }
        string fullRoot=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        string result=Path.GetFullPath(Path.Combine(fullRoot,normalized.Replace('/',Path.DirectorySeparatorChar)));
        if(!result.StartsWith(fullRoot,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Update path escaped its folder.");
        return result;
    }
    internal static void NoLinks(string path){
        string current=Path.GetFullPath(path);
        while(!String.IsNullOrEmpty(current)){
            if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked folders are not supported for automatic updates: "+current);
            current=Path.GetDirectoryName(current);
        }
    }
    internal static void CheckWritable(string root){
        NoLinks(root);string probe=Inside(root,".lumaview-write-"+Guid.NewGuid().ToString("N"));
        try{using(var file=new FileStream(probe,FileMode.CreateNew,FileAccess.Write,FileShare.None,1,FileOptions.DeleteOnClose)){file.WriteByte(0);}}
        catch(UnauthorizedAccessException){throw new UnauthorizedAccessException("프로그램 폴더에 쓰기 권한이 없습니다. LumaView를 관리자 권한으로 실행한 뒤 업데이트를 다시 시도하세요.\nThe application folder is not writable. Run LumaView as administrator and retry.");}
    }
    internal static string Quote(string value){
        var result=new StringBuilder("\"");int slashes=0;
        foreach(char c in value??""){if(c=='\\'){slashes++;continue;}if(c=='\"'){result.Append('\\',slashes*2+1);result.Append(c);}else{result.Append('\\',slashes);result.Append(c);}slashes=0;}
        result.Append('\\',slashes*2);return result.Append('"').ToString();
    }
    internal static UpdateFile[] Extract(string zip,string destination,string version){
        if(Directory.Exists(destination))throw new IOException("Choose a new update staging directory.");
        Directory.CreateDirectory(destination);NoLinks(destination);
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var manifest=new List<UpdateFile>();long total=0;
        using(var archive=ZipFile.OpenRead(zip)){
            if(archive.Entries.Count>5000)throw new InvalidDataException("Too many update files.");
            // Validate every archive entry before writing any payload file.
            foreach(var entry in archive.Entries){
                string name=entry.FullName.Replace('\\','/').TrimEnd('/');if(name.Length==0)throw new InvalidDataException("Empty archive entry.");
                Inside(destination,name);if(!names.Add(name))throw new InvalidDataException("Duplicate update path: "+name);
                if(((entry.ExternalAttributes>>16)&0xf000)==0xa000||(entry.ExternalAttributes&(int)FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked archive entries are not allowed.");
                if(entry.Length>300L*1024*1024||(total+=entry.Length)>1024L*1024*1024)throw new InvalidDataException("Update contents are too large.");
            }
            foreach(var entry in archive.Entries){
                string name=entry.FullName.Replace('\\','/').TrimEnd('/');string target=Inside(destination,name);
                if(entry.FullName.EndsWith("/")||entry.FullName.EndsWith("\\")){Directory.CreateDirectory(target);continue;}
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using(var input=entry.Open())using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
                    byte[] buffer=new byte[65536];int count;long written=0;while((count=input.Read(buffer,0,buffer.Length))>0){written+=count;if(written>entry.Length)throw new InvalidDataException("Archive entry exceeds its declared size.");output.Write(buffer,0,count);}
                }
                if(new FileInfo(target).Length!=entry.Length)throw new InvalidDataException("Incomplete update file: "+name);
                manifest.Add(new UpdateFile{Name=name,Length=entry.Length,Hash=Hash(target)});
            }
        }
        ValidatePayload(destination,version,manifest.ToArray());return manifest.ToArray();
    }
    internal static void ValidatePayload(string root,string version,UpdateFile[] files){
        if(!Regex.IsMatch(version??"",@"^\d+\.\d+\.\d+$"))throw new InvalidDataException("Invalid update version.");
        var names=new HashSet<string>(files.Select(f=>f.Name),StringComparer.OrdinalIgnoreCase);
        foreach(string name in new[]{"LumaView.exe","LumaView.Updater.exe","VERSION","Microsoft.Web.WebView2.Core.dll","Microsoft.Web.WebView2.WinForms.dll","WebView2Loader.dll","video-runtime/mpv.exe","codecs/magick.exe","model-viewer/index.html"})if(!names.Contains(name))throw new InvalidDataException("Update is missing "+name);
        if(File.ReadAllText(Inside(root,"VERSION")).Trim()!=version||FileVersionInfo.GetVersionInfo(Inside(root,"LumaView.exe")).FileVersion!=version+".0")throw new InvalidDataException("Update executable version mismatch.");
    }
    internal static void ValidateJob(UpdateJob job){
        Guid id;if(job==null||!Guid.TryParseExact(job.Id,"N",out id)||job.Files==null||job.Files.Length==0||job.Files.Length>5000)throw new InvalidDataException("Invalid update job.");
        NoLinks(job.WorkRoot);NoLinks(job.TargetRoot);
        string source=Inside(job.WorkRoot,"payload");string target=Inside(job.TargetRoot,"LumaView.exe");
        string targetRoot=Path.GetFullPath(job.TargetRoot).TrimEnd('\\')+"\\",workRoot=Path.GetFullPath(job.WorkRoot).TrimEnd('\\')+"\\";
        if(targetRoot.StartsWith(workRoot,StringComparison.OrdinalIgnoreCase)||workRoot.StartsWith(targetRoot,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Update cache must be outside the application directory.");
        if(!File.Exists(target)||FileVersionInfo.GetVersionInfo(target).ProductName!="LumaView")throw new InvalidDataException("The update target is not a LumaView installation.");
        if(!Regex.IsMatch(job.FromVersion??"",@"^\d+\.\d+\.\d+$")||FileVersionInfo.GetVersionInfo(target).FileVersion!=job.FromVersion+".0")throw new InvalidDataException("The installed executable changed. Restart LumaView and retry the update.");
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in job.Files){
            if(file==null||!names.Add(file.Name)||file.Length<0||!Regex.IsMatch(file.Hash??"",@"^[0-9a-f]{64}$"))throw new InvalidDataException("Invalid update file manifest.");
            string staged=Inside(source,file.Name);NoLinks(staged);NoLinks(Inside(job.TargetRoot,file.Name));
            if(!File.Exists(staged)||new FileInfo(staged).Length!=file.Length||Hash(staged)!=file.Hash)throw new InvalidDataException("Staged update changed: "+file.Name);
        }
        ValidatePayload(source,job.ToVersion,job.Files);CheckWritable(job.TargetRoot);
        if(new Version(job.ToVersion)<=new Version(job.FromVersion))throw new InvalidDataException("An update must be newer than the installed version.");
    }
    internal static void SaveJob(UpdateJob job){File.WriteAllText(Inside(job.WorkRoot,"job.json"),new JavaScriptSerializer().Serialize(job),new UTF8Encoding(false));}
    internal static UpdateJob LoadJob(string path){
        if(new FileInfo(path).Length>4*1024*1024)throw new InvalidDataException("Update job is too large.");
        var job=new JavaScriptSerializer{MaxJsonLength=4*1024*1024}.Deserialize<UpdateJob>(File.ReadAllText(path));
        if(job==null||!String.Equals(Path.GetFullPath(path),Inside(job.WorkRoot,"job.json"),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Unexpected update job location.");
        ValidateJob(job);return job;
    }
    internal static void Result(UpdateJob job,string state,string message){File.WriteAllText(Inside(job.WorkRoot,"result.json"),new JavaScriptSerializer().Serialize(new{state=state,message=message,version=job.ToVersion,target=job.TargetRoot}),new UTF8Encoding(false));}
    internal static void Replace(string source,string target){
        NoLinks(target);Directory.CreateDirectory(Path.GetDirectoryName(target));string temporary=target+".update-"+Guid.NewGuid().ToString("N");
        try{File.Copy(source,temporary,false);if(File.Exists(target))File.Replace(temporary,target,null);else File.Move(temporary,target);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
sealed class UpdateTransaction {
    readonly UpdateJob job;
    readonly Dictionary<string,bool> originals=new Dictionary<string,bool>(StringComparer.OrdinalIgnoreCase);
    readonly List<string> changed=new List<string>();
    internal UpdateTransaction(UpdateJob plan){job=plan;}
    internal void Backup(){
        string backup=UpdateFiles.Inside(job.WorkRoot,"backup");if(Directory.Exists(backup))throw new IOException("Update backup already exists.");Directory.CreateDirectory(backup);
        foreach(var file in job.Files){string target=UpdateFiles.Inside(job.TargetRoot,file.Name);UpdateFiles.NoLinks(target);bool exists=File.Exists(target);originals.Add(file.Name,exists);if(exists){string saved=UpdateFiles.Inside(backup,file.Name);Directory.CreateDirectory(Path.GetDirectoryName(saved));File.Copy(target,saved,false);if(UpdateFiles.Hash(saved)!=UpdateFiles.Hash(target))throw new IOException("Backup verification failed: "+file.Name);}}
        File.WriteAllText(UpdateFiles.Inside(job.WorkRoot,"original-files.json"),new JavaScriptSerializer().Serialize(originals));
    }
    internal void Install(Action<string> progress){
        foreach(var file in job.Files.OrderBy(f=>String.Equals(f.Name,"LumaView.exe",StringComparison.OrdinalIgnoreCase)?1:0)){
            progress(file.Name);
            File.WriteAllText(UpdateFiles.Inside(job.WorkRoot,"changed-files.json"),new JavaScriptSerializer().Serialize(changed.Concat(new[]{file.Name})));
            UpdateFiles.Replace(UpdateFiles.Inside(UpdateFiles.Inside(job.WorkRoot,"payload"),file.Name),UpdateFiles.Inside(job.TargetRoot,file.Name));
            changed.Add(file.Name);
            if(UpdateFiles.Hash(UpdateFiles.Inside(job.TargetRoot,file.Name))!=file.Hash)throw new IOException("Installed file verification failed: "+file.Name);
        }
    }
    internal void Rollback(){
        var errors=new List<string>();foreach(string name in changed.AsEnumerable().Reverse()){
            try{string target=UpdateFiles.Inside(job.TargetRoot,name);UpdateFiles.NoLinks(target);if(originals[name])UpdateFiles.Replace(UpdateFiles.Inside(UpdateFiles.Inside(job.WorkRoot,"backup"),name),target);else if(File.Exists(target))File.Delete(target);}
            catch(Exception ex){errors.Add(name+": "+ex.Message);}
        }
        if(errors.Count>0)throw new IOException("Could not restore some files. Backup: "+UpdateFiles.Inside(job.WorkRoot,"backup")+"\n"+String.Join("\n",errors));
    }
}
}
