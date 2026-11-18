using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LumaView {
partial class Viewer {
    readonly Panel browser=new Panel{Dock=DockStyle.Fill};
    readonly SplitContainer split=new SplitContainer();
    readonly TextBox folderAddress=new TextBox();
    readonly TreeView folders=new TreeView();
    readonly ShellIcons shellIcons=new ShellIcons();
    readonly ListView fileList=new ListView();
    readonly ImageList thumbnails=new ImageList();
    readonly SemaphoreSlim thumbnailWorker=new SemaphoreSlim(1,1);
    string browsingFolder;
    string[] browserFiles=new string[0];
    System.Collections.Generic.Dictionary<string,string[]> sequenceGroups=new System.Collections.Generic.Dictionary<string,string[]>(StringComparer.OrdinalIgnoreCase);
    bool groupSequences=true;
    bool playbackActive;
    bool busy, syncingTree, enterAfterLoad, startSlideAfterLoad;
    int folderVersion;
    void SetupWorkspace() {
        split.Dock=DockStyle.Fill;split.Size=new Size(1100,600);split.SplitterDistance=265;split.Panel1MinSize=160;split.Panel2MinSize=300;split.BackColor=Color.FromArgb(58,62,68);
        var addressBar=new SettingsStack{Dock=DockStyle.Top,Padding=new Padding(18,8,18,8),Margin=Padding.Empty};
        var heading=new TableLayoutPanel{ColumnCount=2,AutoSize=true,Margin=new Padding(0,0,0,6)};
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var grouping=new ModernButton{Text=Ui.T("SQ 묶기"),Selected=true};
        var refresh=new ModernButton{Text=Ui.T("새로고침")};
        var settings=new ModernButton{Text=Ui.T("설정")};settings.Click+=delegate{ShowSettings();};
        var update=new ModernButton{Name="updateButton",Text=Ui.T("업데이트")};update.Click+=async delegate{await AppUpdater.CheckAsync(this,update);};
        var actions=ModernUi.Actions(grouping,refresh,settings,update);actions.WrapContents=false;actions.Anchor=AnchorStyles.Right;actions.Margin=Padding.Empty;
        heading.Controls.Add(actions,1,0);addressBar.Controls.Add(heading);
        var location=new TableLayoutPanel{ColumnCount=3,AutoSize=true,Margin=Padding.Empty};
        location.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));location.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));location.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var up=new ModernButton{Text="↑",MinimumSize=new Size(42,40),AccessibleName=ComponentSetup.Say("상위 폴더","Parent folder")};
        folderAddress.Dock=DockStyle.None;folderAddress.BorderStyle=BorderStyle.None;folderAddress.Margin=Padding.Empty;
        var address=new ModernCard{Padding=new Padding(12,11,12,11),Margin=new Padding(0,0,10,0),Dock=DockStyle.Fill};address.Controls.Add(folderAddress);
        var chooseFolder=new ModernButton{Text=ComponentSetup.Say("폴더 열기","Open folder"),Margin=Padding.Empty};chooseFolder.Click+=delegate{Folder();};
        location.Controls.Add(up,0,0);location.Controls.Add(address,1,0);location.Controls.Add(chooseFolder,2,0);addressBar.Controls.Add(location);
        grouping.Click+=delegate{groupSequences=!groupSequences;grouping.Selected=groupSequences;grouping.Invalidate();if(browsingFolder!=null)Browse(browsingFolder);};
        up.Click+=delegate{if(browsingFolder!=null){var p=Directory.GetParent(browsingFolder);if(p!=null)Browse(p.FullName);}};
        refresh.Click+=delegate{if(browsingFolder!=null)Browse(browsingFolder);};
        folderAddress.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Enter){Browse(folderAddress.Text);e.SuppressKeyPress=true;}};
        folders.ImageList=shellIcons.Images;folders.Dock=DockStyle.Fill;folders.BackColor=Color.FromArgb(30,33,38);folders.ForeColor=Color.Gainsboro;folders.BorderStyle=BorderStyle.None;folders.HideSelection=false;folders.ItemHeight=32;folders.ShowLines=false;folders.FullRowSelect=true;folders.ShowNodeToolTips=true;
        AddRoot(Ui.T("바탕 화면"),Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        AddRoot(Ui.T("문서"),Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        AddRoot(Ui.T("사진"),Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        var downloads=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads");if(Directory.Exists(downloads))AddRoot(Ui.T("다운로드"),downloads);
        int computerIcon=shellIcons.Computer();var pc=new TreeNode(Ui.T("내 PC")){ImageIndex=computerIcon,SelectedImageIndex=computerIcon};folders.Nodes.Add(pc);
        foreach(var drive in DriveInfo.GetDrives()){string label=drive.Name;try{if(drive.IsReady && drive.VolumeLabel.Length>0)label=drive.VolumeLabel+" ("+drive.Name+")";}catch{}pc.Nodes.Add(FolderNode(label,drive.Name));}pc.Expand();
        folders.BeforeExpand+=delegate(object sender,TreeViewCancelEventArgs e){Populate(e.Node);};
        folders.AfterSelect+=delegate(object sender,TreeViewEventArgs e){if(!syncingTree && e.Node.Tag is string)Browse((string)e.Node.Tag);};
        split.Panel1.Padding=new Padding(14,14,10,12);split.Panel1.Controls.Add(folders);
        split.Panel1.Controls.Add(new Label{Text=ComponentSetup.Say("폴더","FOLDERS"),Dock=DockStyle.Top,Height=34,Font=new Font("Segoe UI",9,FontStyle.Bold)});
        thumbnails.ImageSize=new Size(160,120);thumbnails.ColorDepth=ColorDepth.Depth32Bit;
        using(var placeholder=MakePlaceholder("…")){thumbnails.Images.Add(placeholder);var handle=thumbnails.Handle;}
        fileList.Dock=DockStyle.Fill;fileList.View=View.LargeIcon;fileList.LargeImageList=thumbnails;fileList.MultiSelect=false;fileList.HideSelection=false;fileList.BackColor=Color.FromArgb(40,43,48);fileList.ForeColor=Color.Gainsboro;fileList.BorderStyle=BorderStyle.None;fileList.ShowItemToolTips=true;
        fileList.HandleCreated+=delegate{ModernUi.GallerySpacing(fileList);};fileList.FontChanged+=delegate{ModernUi.GallerySpacing(fileList);};
        fileList.ItemActivate+=delegate{OpenSelectedFullscreen();};
        fileList.SelectedIndexChanged+=delegate{UpdateBrowserInfo();};
        split.Panel2.Controls.Add(fileList);browser.Controls.Add(split);browser.Controls.Add(addressBar);Controls.Add(browser);
        canvas.Visible=false;
        Shown+=delegate{if(!startupOpening && browsingFolder==null)Browse(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));};
        FormClosed+=delegate{folderVersion++;thumbnails.Dispose();shellIcons.Dispose();};
    }
    void AddRoot(string name,string path){if(!String.IsNullOrEmpty(path))folders.Nodes.Add(FolderNode(name,path));}
    TreeNode FolderNode(string name,string path){int icon=shellIcons.ForPath(path);var node=new TreeNode(name){Tag=path,ToolTipText=path,ImageIndex=icon,SelectedImageIndex=icon};node.Nodes.Add(new TreeNode("…"));return node;}
    void Populate(TreeNode node){
        if(node.Tag==null || node.Nodes.Count!=1 || node.Nodes[0].Tag!=null || node.Nodes[0].Text!="…")return;
        node.Nodes.Clear();
        try{foreach(string p in Directory.GetDirectories((string)node.Tag).OrderBy(p=>p,new NaturalSort()))node.Nodes.Add(FolderNode(Path.GetFileName(p),p));}
        catch(UnauthorizedAccessException){node.Nodes.Add(Ui.T("접근할 수 없음"));}catch(IOException){node.Nodes.Add(Ui.T("폴더를 읽을 수 없음"));}
    }
    void SelectFolderNode(string path){
        syncingTree=true;
        try{
            string root=Path.GetPathRoot(path);
            var pc=folders.Nodes.Cast<TreeNode>().First(n=>n.Tag==null);
            TreeNode current=pc.Nodes.Cast<TreeNode>().FirstOrDefault(n=>String.Equals((string)n.Tag,root,StringComparison.OrdinalIgnoreCase));
            if(current==null){current=FolderNode(root,root);pc.Nodes.Add(current);}
            string remainder=path.Substring(root.Length);
            foreach(string part in remainder.Split(new[]{'\\','/'},StringSplitOptions.RemoveEmptyEntries)){
                Populate(current);current.Expand();
                string nextPath=Path.Combine((string)current.Tag,part);
                var next=current.Nodes.Cast<TreeNode>().FirstOrDefault(n=>String.Equals(n.Tag as string,nextPath,StringComparison.OrdinalIgnoreCase));
                if(next==null){next=FolderNode(part,nextPath);current.Nodes.Add(next);}current=next;
            }
            folders.SelectedNode=current;current.EnsureVisible();
        }finally{syncingTree=false;}
    }
    void ToggleExplorer(){if(full){ExitFullscreen();return;}split.Panel1Collapsed=!split.Panel1Collapsed;}
    void Browse(string path){
        if(full)return;
        try{
            path=Path.GetFullPath(path);string[] files=Directory.GetFiles(path).Where(MediaFiles.Supported).OrderBy(p=>p,new NaturalSort()).ToArray();
            sequenceGroups=groupSequences?Sequences.Group(files):new System.Collections.Generic.Dictionary<string,string[]>(StringComparer.OrdinalIgnoreCase);
            var hidden=new System.Collections.Generic.HashSet<string>(sequenceGroups.Values.SelectMany(group=>group.Skip(1)),StringComparer.OrdinalIgnoreCase);files=files.Where(p=>!hidden.Contains(p)).ToArray();
            browsingFolder=path;browserFiles=files;folderAddress.Text=path;int version=++folderVersion;
            fileList.BeginUpdate();fileList.Items.Clear();thumbnails.Images.Clear();using(var placeholder=MakePlaceholder("…")){thumbnails.Images.Add(placeholder);var handle=thumbnails.Handle;}
            foreach(string p in files){string[] sequence;bool sq=sequenceGroups.TryGetValue(p,out sequence);var item=new ListViewItem(Path.GetFileName(p)+(sq?"  ["+sequence.Length+Ui.T(" 프레임]"):""),0){Tag=p,ToolTipText=p+(sq?Ui.T("\nSQ 시퀀스 · ")+sequence.Length+Ui.T(" 프레임"):"")};fileList.Items.Add(item);}
            fileList.EndUpdate();SelectFolderNode(path);Text=AppUpdater.WindowTitle(path);
            if(fileList.Items.Count>0){fileList.Items[0].Selected=true;fileList.Items[0].Focused=true;}
            UpdateBrowserInfo();LoadThumbnails(files,version);
        }catch(Exception ex){fileList.EndUpdate();MessageBox.Show(this,ex.Message,Ui.T("폴더를 열 수 없습니다"));}
    }
    static Bitmap MakePlaceholder(string label){var b=new Bitmap(160,120);using(var g=Graphics.FromImage(b)){g.Clear(Color.FromArgb(55,59,65));using(var f=new Font("Segoe UI",14))using(var brush=new SolidBrush(Color.Silver))g.DrawString(label,f,brush,new RectangleF(0,45,160,40),new StringFormat{Alignment=StringAlignment.Center});}return b;}
    static Bitmap MakeThumbnail(string path){if(ModelFiles.Supported(path))return ModelFiles.Tile(path);if(MediaFiles.IsVideo(path))return MediaFiles.VideoTile(path);
        using(var source=Album.Decode(path)){
            var b=new Bitmap(160,120);using(var g=Graphics.FromImage(b)){
                g.Clear(Color.FromArgb(50,54,60));double scale=Math.Min(152.0/source.Width,112.0/source.Height);
                int w=Math.Max(1,(int)(source.Width*scale)),h=Math.Max(1,(int)(source.Height*scale));var rect=new Rectangle((160-w)/2,(120-h)/2,w,h);
                using(var tile=new Bitmap(24,24)){using(var t=Graphics.FromImage(tile)){t.Clear(Color.WhiteSmoke);t.FillRectangle(Brushes.LightGray,12,0,12,12);t.FillRectangle(Brushes.LightGray,0,12,12,12);}using(var brush=new TextureBrush(tile))g.FillRectangle(brush,rect);}
                g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(source,rect);
            }return b;
        }
    }
    async void LoadThumbnails(string[] paths,int version){
        await thumbnailWorker.WaitAsync();
        try{
            for(int i=0;i<paths.Length;i++){
                while(playbackActive&&!IsDisposed&&version==folderVersion)await Task.Delay(150);
                if(IsDisposed || version!=folderVersion)return;
                string p=paths[i];bool failed=false;
                var bitmap=await Task.Run(delegate{try{return MakeThumbnail(p);}catch{failed=true;return MakePlaceholder(Ui.T("미리보기 없음"));}});
                using(bitmap){if(IsDisposed || version!=folderVersion)return;if(sequenceGroups.ContainsKey(p))Sequences.Badge(bitmap);thumbnails.Images.Add(bitmap);fileList.Items[i].ImageIndex=thumbnails.Images.Count-1;if(failed)fileList.Items[i].ToolTipText=p+"\n미리보기를 읽을 수 없습니다. 더블클릭하면 오류를 확인할 수 있습니다.";}
            }
        }finally{thumbnailWorker.Release();}
    }
    void UpdateBrowserInfo(){if(full || busy)return;string name=fileList.SelectedItems.Count>0?(string)fileList.SelectedItems[0].Tag:"";info.Text=browserFiles.Length+Ui.T("개 파일  ·  더블클릭: 전체화면  ·  ")+Path.GetFileName(name);info.ToolTipText=name;}
    void SyncExplorer(){
        string dir=Path.GetDirectoryName(album.Current);
        if(!full && !String.Equals(dir,browsingFolder,StringComparison.OrdinalIgnoreCase))Browse(dir);
        foreach(ListViewItem item in fileList.Items){item.Selected=String.Equals((string)item.Tag,album.Current,StringComparison.OrdinalIgnoreCase);if(item.Selected){item.Focused=true;item.EnsureVisible();}}
        if(!full){Text=AppUpdater.WindowTitle(browsingFolder);UpdateBrowserInfo();}
    }
    void OpenSelectedFullscreen(){if(busy)return;if(fileList.SelectedItems.Count==0)return;enterAfterLoad=true;Open((string)fileList.SelectedItems[0].Tag);}
    bool OpenSequence(string path){
        string[] sequence;if(!sequenceGroups.TryGetValue(path,out sequence)){
            sequence=sequenceGroups.Values.FirstOrDefault(items=>items.Contains(path,StringComparer.OrdinalIgnoreCase));
            if(sequence==null)return false;
        }
        StopSlide();enterAfterLoad=false;startSlideAfterLoad=false;if(full&&!startupOpening)ExitFullscreen();playbackActive=true;
        int start=Array.FindIndex(sequence,p=>String.Equals(p,path,StringComparison.OrdinalIgnoreCase));
        try{using(var player=new SequenceViewer(sequence,Screen.FromControl(this).Bounds,start))player.ShowDialog(this);}finally{playbackActive=false;if(startupOpening){startupOpening=false;ExitFullscreen();}fileList.Focus();}return true;
    }
    async void LoadAsync(string path,int index){
        busy=true;UseWaitCursor=true;info.Text=Ui.T("이미지를 읽는 중…");
        var files=(string[])album.Files.Clone();
        try{
            var next=await Task.Run(delegate{var a=new Album();try{if(path!=null)a.Open(path);else{a.Files=files;a.Select(index);}return a;}catch{a.Dispose();throw;}});
            if(IsDisposed){next.Dispose();return;}
            canvas.Image=null;album.Dispose();album.Image=next.Image;next.Image=null;album.Files=next.Files;album.Index=next.Index;next.Dispose();Loaded();
            if(enterAfterLoad && !full)EnterFullscreen(); if(startSlideAfterLoad && full){startSlideAfterLoad=false;ToggleSlide();}
        }catch(Exception ex){if(!IsDisposed){StopSlide();MessageBox.Show(this,ex.Message,Ui.T("이미지를 열 수 없습니다"),MessageBoxButtons.OK,MessageBoxIcon.Information);}}
        finally{if(startupOpening){startupOpening=false;if(album.Image==null&&!IsDisposed)ExitFullscreen();}enterAfterLoad=false;startSlideAfterLoad=false;busy=false;if(!IsDisposed){UseWaitCursor=false;UpdateInfo();if(!full)UpdateBrowserInfo();}}
    }
    void EnterFullscreen(bool allowEmpty=false){
        if((album.Image==null&&!allowEmpty) || full)return;
        oldBounds=Bounds;oldState=WindowState;WindowState=FormWindowState.Normal;FormBorderStyle=FormBorderStyle.None;Bounds=Screen.FromControl(this).Bounds;
        if(chrome==null)chrome=new FullscreenChrome(this,toolbar,null,delegate(Keys key){var message=new Message();return ProcessCmdKey(ref message,key);});
        full=true;browser.Visible=false;toolbar.Visible=true;status.Visible=false;Controls.Add(canvas);canvas.Visible=true;canvas.BringToFront();canvas.Focus();canvas.Reset();chrome.Enable();
    }
    void ExitFullscreen(){
        if(!full)return;chrome.Disable();StopSlide();full=false;canvas.Visible=false;Controls.Remove(canvas);browser.Visible=true;toolbar.Visible=false;status.Visible=true;
        FormBorderStyle=FormBorderStyle.Sizable;Bounds=oldBounds;WindowState=oldState;Browse(browsingFolder);if(album.Image!=null)SyncExplorer();fileList.Focus();UpdateBrowserInfo();
    }
    void Fullscreen(){if(full)ExitFullscreen();else OpenSelectedFullscreen();}
    void ShowPicker(){
        if(busy)return;StopSlide();string[] files=full?album.Files:browserFiles;if(files.Length==0)return;
        int selected=full?album.Index:(fileList.SelectedIndices.Count>0?fileList.SelectedIndices[0]:0);
        using(var picker=new ImagePicker(files,selected,canvas.BackgroundOne)){if(picker.ShowDialog(this)==DialogResult.OK){enterAfterLoad=true;Open(files[picker.SelectedIndex]);}}
    }
}
}

