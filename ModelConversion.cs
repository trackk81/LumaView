using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LumaView {
static class ModelConversion {
    internal static readonly string[] Formats={"fbx","glb","gltf","obj","stl"};
    static string Say(string ko,string en){return ComponentSetup.Say(ko,en);}
    static bool Exists(string path){return File.Exists(path)||Directory.Exists(path);}
    internal static string SuggestedName(string source,string format){
        string stem=Path.GetFileNameWithoutExtension(source),dir=Path.GetDirectoryName(source);
        for(int n=1;;n++){
            string name=stem+(n==1?"":" ("+n+")");
            string candidate=Path.Combine(dir,name+"."+format);
            if(Exists(candidate))continue;
            if(format=="obj"&&(Exists(Path.ChangeExtension(candidate,"mtl"))||Exists(Path.Combine(dir,name+".textures"))))continue;
            if(format=="gltf"&&Exists(Path.ChangeExtension(candidate,"bin")))continue;
            return Path.GetFileName(candidate);
        }
    }
    internal static string[] Convert(string source,string destination,CancellationToken token){
        source=Path.GetFullPath(source);destination=Path.GetFullPath(destination);
        string ext=Path.GetExtension(destination).TrimStart('.').ToLowerInvariant();
        if(!ModelFiles.Supported(source)||!Formats.Contains(ext))throw new ArgumentException(Say("지원하지 않는 형식입니다.","Unsupported model format."));
        if(String.Equals(Path.GetExtension(source),"."+ext,StringComparison.OrdinalIgnoreCase))
            throw new IOException(Say("원본과 다른 형식을 선택하세요.","Choose a different output format."));
        string dir=Path.GetDirectoryName(destination);
        if(Exists(destination))throw Collision(destination);
        token.ThrowIfCancellationRequested();
        string helper=Path.Combine(Path.GetDirectoryName(typeof(ModelConversion).Assembly.Location),"model-converter","DollView.Convert.exe");
        if(!File.Exists(helper))throw new FileNotFoundException(Say("변환기가 누락되었습니다. DollView를 다시 설치하거나 ZIP 전체를 압축 해제하세요.","The converter is missing. Reinstall DollView or extract the complete ZIP."),helper);
        string stage=Path.Combine(dir,".DollView-convert-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        string main=Path.Combine(stage,Path.GetFileNameWithoutExtension(destination)+"."+ext);
        try{
            var info=new ProcessStartInfo(helper,UpdateFiles.Quote(source)+" "+UpdateFiles.Quote(main)){
                WorkingDirectory=Path.GetDirectoryName(source),UseShellExecute=false,CreateNoWindow=true,
                RedirectStandardError=true,StandardErrorEncoding=Encoding.UTF8
            };
            string diagnostics;
            using(var process=Process.Start(info))
            using(token.Register(delegate{try{if(!process.HasExited)process.Kill();}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}})){
                diagnostics=process.StandardError.ReadToEnd();process.WaitForExit();token.ThrowIfCancellationRequested();
                if(process.ExitCode!=0||!File.Exists(main)||new FileInfo(main).Length==0)
                    throw new IOException(Say("3D 파일을 변환하지 못했습니다.","Could not convert this 3D file.")+
                        (String.IsNullOrWhiteSpace(diagnostics)?"":"\n"+diagnostics.Trim()));
            }
            Publish(stage,main,destination,token);
            const string skipped="Skipped texture: ";
            return diagnostics.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)
                .Where(line=>line.StartsWith(skipped,StringComparison.Ordinal))
                .Select(line=>line.Substring(skipped.Length)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }finally{
            try{Directory.Delete(stage,true);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    static IOException Collision(string path){
        return new IOException(Say("같은 이름의 파일이나 폴더가 있습니다. 다른 이름으로 저장하세요.","A file or folder already has that name. Choose a new name.")+"\n"+path);
    }
    static void Publish(string stage,string main,string destination,CancellationToken token){
        string dir=Path.GetDirectoryName(destination);
        var companions=Directory.GetFileSystemEntries(stage).Where(p=>!String.Equals(p,main,StringComparison.OrdinalIgnoreCase)).ToArray();
        var moved=new List<KeyValuePair<string,string>>();
        foreach(string entry in companions){
            string to=Path.Combine(dir,Path.GetFileName(entry));
            if(Exists(to))throw Collision(to);
        }
        try{
            foreach(string entry in companions){
                token.ThrowIfCancellationRequested();
                string to=Path.Combine(dir,Path.GetFileName(entry));
                if(Directory.Exists(entry))Directory.Move(entry,to);else File.Move(entry,to);
                moved.Add(new KeyValuePair<string,string>(entry,to));
            }
            token.ThrowIfCancellationRequested();
            File.Move(main,destination);
        }catch{
            // Move back only companions successfully published by this operation.
            for(int i=moved.Count-1;i>=0;i--){
                var item=moved[i];
                if(Directory.Exists(item.Value))Directory.Move(item.Value,item.Key);else File.Move(item.Value,item.Key);
            }
            throw;
        }
    }
}

sealed partial class ModelViewer {
    ToolStripDropDownButton convertButton;
    CancellationTokenSource conversionCancel;
    Task conversion;
    bool closingAfterConversion;
    string ConvertLabel {get{return ComponentSetup.Say("포맷 변환","Convert format");}}
    void AddConversionMenu(){
        convertButton=new ToolStripDropDownButton(ConvertLabel){Padding=new System.Windows.Forms.Padding(4,3,4,3),Tag="model",Enabled=false};
        foreach(string format in ModelConversion.Formats){
            if(String.Equals(Path.GetExtension(path),"."+format,StringComparison.OrdinalIgnoreCase))continue;
            string label=format.ToUpperInvariant();
            if(format=="stl")label+=ComponentSetup.Say(" · 형상만"," · Geometry only");
            if(format=="obj")label+=ComponentSetup.Say(" · 정적 메시와 재질"," · Static mesh and materials");
            var item=convertButton.DropDownItems.Add(label);
            if(format=="obj")item.ToolTipText=ComponentSetup.Say("재질 파일과 텍스처를 함께 저장합니다. 애니메이션은 저장되지 않습니다.","Saves materials and textures alongside the static mesh.");
            if(format=="stl")item.ToolTipText=ComponentSetup.Say("삼각형 형상만 저장합니다.","Saves triangle geometry only.");
            if(format=="fbx")item.ToolTipText=ComponentSetup.Say("뼈대와 변환 애니메이션을 저장합니다. 셰이프 키와 PBR 재질 표현은 달라질 수 있습니다.","Saves skinning and transform animation. Morph targets and PBR materials can differ.");
            item.Click+=delegate{SaveConvertedModel(format);};
        }
        var cancel=convertButton.DropDownItems.Add(ComponentSetup.Say("변환 취소","Cancel conversion"));
        cancel.Click+=delegate{if(conversionCancel!=null)conversionCancel.Cancel();};
        convertButton.DropDownOpening+=delegate{
            foreach(ToolStripItem item in convertButton.DropDownItems)item.Available=(item==cancel)==(conversionCancel!=null);
        };
        tools.Items.Add(convertButton);
        FormClosing+=FinishConversionOnClose;
    }
    async void FinishConversionOnClose(object sender,FormClosingEventArgs e){
        if(conversion==null||conversion.IsCompleted)return;
        e.Cancel=true;
        if(closingAfterConversion)return;
        closingAfterConversion=true;conversionCancel.Cancel();
        try{await conversion;}catch(Exception){}
        if(!IsDisposed)BeginInvoke((Action)Close);
    }
    async void SaveConvertedModel(string format){
        if(conversionCancel!=null||closingAfterConversion)return;
        string destination;
        using(var save=new SaveFileDialog()){
            save.Title=ComponentSetup.Say("3D 모델을 새 파일로 저장","Save model as a new file");
            save.InitialDirectory=Path.GetDirectoryName(path);save.DefaultExt=format;save.AddExtension=true;
            save.Filter=format.ToUpperInvariant()+" (*."+format+")|*."+format;
            save.FileName=ModelConversion.SuggestedName(path,format);save.OverwritePrompt=false;save.RestoreDirectory=true;
            if(save.ShowDialog(this)!=DialogResult.OK)return;
            destination=save.FileName;
        }
        if(!String.Equals(Path.GetExtension(destination),"."+format,StringComparison.OrdinalIgnoreCase)){
            MessageBox.Show(this,ComponentSetup.Say("선택한 확장자로 저장하세요.","Use the selected file extension.")+" (."+format+")",ConvertLabel);return;
        }
        conversionCancel=new CancellationTokenSource();
        var token=conversionCancel.Token;
        try{
            convertButton.Text=ComponentSetup.Say("변환 중…","Converting…");status.Text=convertButton.Text+" "+Path.GetFileName(destination);
            var save=Task.Run(()=>ModelConversion.Convert(path,destination,token));
            conversion=save;
            var skipped=await save;
            if(!closingAfterConversion){
                status.Text=ComponentSetup.Say("저장 완료","Saved")+" · "+destination;
                if(skipped.Length==0)MessageBox.Show(this,status.Text,ConvertLabel,MessageBoxButtons.OK,MessageBoxIcon.Information);
                else ShowSkippedTextures(destination,skipped);
            }
        }catch(OperationCanceledException){if(!closingAfterConversion)status.Text=ComponentSetup.Say("변환을 취소했습니다.","Conversion cancelled.");}
        catch(Exception ex){if(!closingAfterConversion){status.Text=ex.Message;MessageBox.Show(this,ex.Message,ConvertLabel,MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
        finally{conversion=null;conversionCancel.Dispose();conversionCancel=null;if(!closing)convertButton.Text=ConvertLabel;}
    }
    void ShowSkippedTextures(string destination,string[] names){
        using(var dialog=new Form{Text=ConvertLabel,StartPosition=FormStartPosition.CenterParent,MinimizeBox=false,MaximizeBox=false,
            ShowInTaskbar=false,AutoScaleMode=AutoScaleMode.Dpi,ClientSize=new System.Drawing.Size(540,290),MinimumSize=new System.Drawing.Size(400,225)}){
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(12)};
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label{AutoSize=true,Dock=DockStyle.Fill,Text=ComponentSetup.Say("저장 완료 · 포함하지 못한 텍스처 ","Saved · Skipped textures: ")+names.Length},0,0);
            layout.Controls.Add(new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,WordWrap=false,ScrollBars=ScrollBars.Both,
                Text=destination+Environment.NewLine+Environment.NewLine+String.Join(Environment.NewLine,names)},0,1);
            var ok=new Button{AutoSize=true,Anchor=AnchorStyles.Right,Text=ComponentSetup.Say("확인","OK"),DialogResult=DialogResult.OK};
            layout.Controls.Add(ok,0,2);dialog.Controls.Add(layout);dialog.AcceptButton=ok;dialog.CancelButton=ok;
            Ui.Apply(dialog);dialog.HandleCreated+=delegate{ModernUi.TitleBar(dialog);};dialog.ShowDialog(this);
        }
    }
}
}
