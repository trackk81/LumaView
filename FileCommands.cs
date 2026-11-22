using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Collections.Specialized;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LumaView {
static class FileCommands {
    public static string CopyInto(string source,string folder){
        string stem=Path.GetFileNameWithoutExtension(source),extension=Path.GetExtension(source);
        for(int n=0;;n++){
            string target=Path.Combine(folder,n==0?Path.GetFileName(source):stem+" - 복사본"+(n==1?"":" ("+n+")")+extension);
            if(File.Exists(target)||Directory.Exists(target))continue;
            FileStream output;
            try{output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None);}
            catch(IOException ex){int code=ex.HResult&0xffff;if(code==80||code==183)continue;throw;}
            try{using(output)using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read)){input.CopyTo(output);}return target;}
            catch{output.Dispose();try{File.Delete(target);}catch{}throw;}
        }
    }
    public static void Copy(string path){if(!File.Exists(path))throw new FileNotFoundException(Ui.T("파일을 찾을 수 없습니다."),path);Clipboard.SetFileDropList(new StringCollection{path});}
    public static string Rename(Form owner,string source){
        using(var dialog=new Form{Text=Ui.T("이름 바꾸기"),ClientSize=new Size(460,125),FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MinimizeBox=false,MaximizeBox=false,ShowInTaskbar=false,Font=owner.Font,BackColor=Color.FromArgb(38,42,48),ForeColor=Color.White}){
            var label=new Label{Text=Ui.T("새 파일 이름"),Left=16,Top=12,AutoSize=true};
            var input=new TextBox{Text=Path.GetFileName(source),Left=16,Top=36,Width=428};
            var ok=new Button{Text=Ui.T("변경"),Left=274,Top=83,Width=80,DialogResult=DialogResult.None};
            var cancel=new Button{Text=Ui.T("취소"),Left=364,Top=83,Width=80,DialogResult=DialogResult.Cancel};
            dialog.Controls.AddRange(new Control[]{label,input,ok,cancel});dialog.AcceptButton=ok;dialog.CancelButton=cancel;
            string result=null;
            dialog.Shown+=delegate{input.Focus();input.Select(0,Path.GetFileNameWithoutExtension(source).Length);};
            ok.Click+=delegate{
                try{
                    string name=input.Text;
                    if(String.IsNullOrWhiteSpace(name)||name=="."||name==".."||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||name.EndsWith(" ")||name.EndsWith("."))throw new IOException(Ui.T("사용할 수 없는 파일 이름입니다."));
                    string target=Path.Combine(Path.GetDirectoryName(source),name);
                    if(String.Equals(source,target,StringComparison.Ordinal)){result=source;dialog.DialogResult=DialogResult.OK;return;}
                    if(!String.Equals(source,target,StringComparison.OrdinalIgnoreCase)&&(File.Exists(target)||Directory.Exists(target)))throw new IOException(Ui.T("같은 이름의 파일이 이미 있습니다. 다른 이름을 입력해 주세요."));
                    File.Move(source,target);result=target;dialog.DialogResult=DialogResult.OK;
                }catch(Exception ex){MessageBox.Show(dialog,ex.Message,Ui.T("이름을 변경할 수 없습니다"),MessageBoxButtons.OK,MessageBoxIcon.Information);}
            };
            Ui.Apply(dialog);
            return dialog.ShowDialog(owner)==DialogResult.OK?result:null;
        }
    }
}
partial class Viewer {
    bool fileOperation;
    string FileCommandPath(){return full?album.Current:(fileList.SelectedItems.Count>0?fileList.SelectedItems[0].Tag as string:null);}
    void SetupFileCommands(){
        var menu=new ContextMenuStrip();
        var copy=new ToolStripMenuItem(Ui.T("복사")){ShortcutKeyDisplayString="Ctrl+C"};
        var paste=new ToolStripMenuItem(Ui.T("붙여넣기")){ShortcutKeyDisplayString="Ctrl+V"};
        var rename=new ToolStripMenuItem(Ui.T("이름 바꾸기")){ShortcutKeyDisplayString="F2"};
        copy.Click+=delegate{CopyFile();};paste.Click+=delegate{PasteFiles();};rename.Click+=delegate{RenameFile();};
        menu.Items.AddRange(new ToolStripItem[]{copy,paste,new ToolStripSeparator(),rename});
        menu.Opening+=delegate{copy.Enabled=rename.Enabled=!busy&&!fileOperation&&File.Exists(FileCommandPath());try{paste.Enabled=!busy&&!fileOperation&&Clipboard.ContainsFileDropList();}catch{paste.Enabled=false;}};
        fileList.MouseDown+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Right){var hit=fileList.GetItemAt(e.X,e.Y);fileList.SelectedItems.Clear();if(hit!=null){hit.Selected=true;hit.Focused=true;}fileList.Focus();}};
        fileList.ContextMenuStrip=menu;canvas.ContextMenuStrip=menu;
        FormClosed+=delegate{menu.Dispose();};
    }
    void CopyFile(){if(busy||fileOperation)return;try{string path=FileCommandPath();if(path!=null){string[] sequence;if(!full&&sequenceGroups.TryGetValue(path,out sequence)){var paths=new StringCollection();paths.AddRange(sequence);Clipboard.SetFileDropList(paths);}else FileCommands.Copy(path);}}catch(Exception ex){MessageBox.Show(this,ex.Message,Ui.T("복사할 수 없습니다"));}}
    void RenameFile(){
        if(busy||fileOperation)return;string source=FileCommandPath();if(source==null)return;if(!full&&sequenceGroups.ContainsKey(source)){MessageBox.Show(this,Ui.T("시퀀스의 개별 이름을 바꾸려면 SQ 묶기를 해제해 주세요."));return;}StopSlide();
        string target=FileCommands.Rename(this,source);if(target==null)return;
        for(int i=0;i<album.Files.Length;i++)if(String.Equals(album.Files[i],source,StringComparison.OrdinalIgnoreCase))album.Files[i]=target;
        int mixed=Array.IndexOf(album.WheelFiles,source);
        if(mixed>=0){album.WheelFiles[mixed]=target;Array.Sort(album.WheelFiles,new NaturalSort());wheelIndex=Array.IndexOf(album.WheelFiles,album.Current);canvas.Sidebar.Hide();}
        if(full){Text=AppUpdater.WindowTitle(Path.GetFileName(target));RefreshDetails();canvas.Invalidate();}
        else RefreshFileSelection(target);
    }
    void RefreshFileSelection(string selected){
        Browse(browsingFolder,selected);
    }
    async void PasteFiles(){
        if(busy||fileOperation)return;
        try{
            if(!Clipboard.ContainsFileDropList())return;
            string[] sources=Clipboard.GetFileDropList().Cast<string>().Where(File.Exists).ToArray();
            if(sources.Length==0){MessageBox.Show(this,Ui.T("복사한 파일이 없습니다. 폴더 붙여넣기는 지원하지 않습니다."));return;}
            string folder=full?Path.GetDirectoryName(album.Current):browsingFolder;
            if(!Directory.Exists(folder))return;
            fileOperation=true;UseWaitCursor=true;info.Text=Ui.T("파일을 복사하는 중…");
            string last=null;var errors=new System.Collections.Generic.List<string>();
            await Task.Run(delegate{foreach(string source in sources){try{last=FileCommands.CopyInto(source,folder);}catch(Exception ex){errors.Add(Path.GetFileName(source)+": "+ex.Message);}}});
            if(IsDisposed)return;
            if(!full&&String.Equals(folder,browsingFolder,StringComparison.OrdinalIgnoreCase))RefreshFileSelection(last);
            if(errors.Count>0)MessageBox.Show(this,String.Join("\n",errors),Ui.T("일부 파일을 복사하지 못했습니다"));
        }catch(Exception ex){if(!IsDisposed)MessageBox.Show(this,ex.Message,Ui.T("붙여넣기 실패"));}
        finally{fileOperation=false;if(!IsDisposed){UseWaitCursor=false;UpdateInfo();}}
    }
}
}
