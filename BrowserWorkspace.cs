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
    ImageList thumbnails;
    readonly SemaphoreSlim thumbnailWorker=new SemaphoreSlim(1,1);
    string browsingFolder, loadedGalleryFolder, pendingGallerySelection;
    bool galleryLoading, galleryClosing;
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
        var actions=ModernUi.Actions(GalleryPrintButton(),grouping,refresh,settings,update);actions.WrapContents=false;actions.Anchor=AnchorStyles.Right;actions.Margin=Padding.Empty;
        heading.Controls.Add(actions,1,0);addressBar.Controls.Add(heading);
        var location=new TableLayoutPanel{ColumnCount=3,AutoSize=true,Margin=Padding.Empty};
        location.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));location.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));location.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var up=new ModernButton{Text="↑",MinimumSize=new Size(42,40),AccessibleName=ComponentSetup.Say("상위 폴더","Parent folder")};
        folderAddress.Dock=DockStyle.None;folderAddress.BorderStyle=BorderStyle.None;folderAddress.Margin=Padding.Empty;
        var address=new ModernCard{Padding=new Padding(12,11,12,11),Margin=new Padding(0,0,10,0),Dock=DockStyle.Fill};address.Controls.Add(folderAddress);
        var chooseFolder=new ModernButton{Text=ComponentSetup.Say("폴더 열기","Open folder"),Margin=Padding.Empty};chooseFolder.Click+=delegate{Folder();};
        location.Controls.Add(up,0,0);location.Controls.Add(address,1,0);location.Controls.Add(chooseFolder,2,0);addressBar.Controls.Add(location);
        grouping.Click+=delegate{groupSequences=!groupSequences;canvas.Sidebar.GroupSequences=groupSequences;grouping.Selected=groupSequences;grouping.Invalidate();if(browsingFolder!=null)Browse(browsingFolder);};
        up.Click+=delegate{if(browsingFolder!=null){var p=Directory.GetParent(browsingFolder);if(p!=null)Browse(p.FullName);}};
        refresh.Click+=delegate{if(browsingFolder!=null)Browse(browsingFolder);};
        folderAddress.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Enter){Browse(folderAddress.Text);e.SuppressKeyPress=true;}};
        folders.ImageList=shellIcons.Images;folders.Dock=DockStyle.Fill;folders.BackColor=Color.FromArgb(30,33,38);folders.ForeColor=Color.Gainsboro;folders.BorderStyle=BorderStyle.None;folders.HideSelection=false;folders.ItemHeight=32;folders.ShowLines=false;folders.FullRowSelect=true;folders.ShowNodeToolTips=true;
        AddRoot(Ui.T("바탕 화면"),Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        AddRoot(Ui.T("문서"),Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        AddRoot(Ui.T("사진"),Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        var downloads=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads");if(Directory.Exists(downloads))AddRoot(Ui.T("다운로드"),downloads);
        int computerIcon=shellIcons.Computer();var pc=new TreeNode(Ui.T("내 PC")){ImageIndex=computerIcon,SelectedImageIndex=computerIcon};folders.Nodes.Add(pc);
        foreach(var drive in DriveInfo.GetDrives()){string label=drive.Name;try{if(drive.DriveType!=DriveType.Network && drive.IsReady && drive.VolumeLabel.Length>0)label=drive.VolumeLabel+" ("+drive.Name+")";}catch{}pc.Nodes.Add(FolderNode(label,drive.Name));}pc.Expand();
        folders.BeforeExpand+=delegate(object sender,TreeViewCancelEventArgs e){if(!syncingTree)Populate(e.Node);};
        folders.AfterSelect+=delegate(object sender,TreeViewEventArgs e){if(!syncingTree && e.Node.Tag is string)Browse((string)e.Node.Tag);};
        split.Panel1.Padding=new Padding(14,14,10,12);split.Panel1.Controls.Add(folders);
        split.Panel1.Controls.Add(new Label{Text=ComponentSetup.Say("폴더","FOLDERS"),Dock=DockStyle.Top,Height=34,Font=new Font("Segoe UI",9,FontStyle.Bold)});
        thumbnails=CreateGalleryImages();
        fileList.Dock=DockStyle.Fill;fileList.View=View.LargeIcon;fileList.LargeImageList=thumbnails;fileList.MultiSelect=false;fileList.HideSelection=false;fileList.BackColor=Color.FromArgb(40,43,48);fileList.ForeColor=Color.Gainsboro;fileList.BorderStyle=BorderStyle.None;fileList.ShowItemToolTips=true;
        fileList.HandleCreated+=delegate{ModernUi.GallerySpacing(fileList);};fileList.FontChanged+=delegate{ModernUi.GallerySpacing(fileList);};
        fileList.ItemActivate+=delegate{OpenSelectedImage();};
        fileList.SelectedIndexChanged+=delegate{UpdateBrowserInfo();};
        split.Panel2.Controls.Add(fileList);browser.Controls.Add(split);browser.Controls.Add(addressBar);Controls.Add(browser);
        canvas.Visible=false;
        Shown+=delegate{if(!startupOpening && browsingFolder==null)Browse(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));};
        FormClosing+=delegate{galleryClosing=true;folderVersion++;};
        Disposed+=delegate{if(thumbnails!=null)thumbnails.Dispose();shellIcons.Dispose();};
    }
    void AddRoot(string name,string path){if(!String.IsNullOrEmpty(path))folders.Nodes.Add(FolderNode(name,path));}
    sealed class FolderTreeNode:TreeNode {
        internal readonly TreeNode ExpandMarker=new TreeNode();
        internal bool Loading,Loaded;
        internal FolderTreeNode(string name):base(name){Nodes.Add(ExpandMarker);}
    }
    TreeNode FolderNode(string name,string path){
        int icon=shellIcons.Folder();
        return new FolderTreeNode(name){Tag=path,ToolTipText=path,ImageIndex=icon,SelectedImageIndex=icon};
    }
    async void Populate(TreeNode node){
        var folder=node as FolderTreeNode;
        if(folder==null||folder.Loaded||folder.Loading)return;
        folder.Loading=true;
        folder.ExpandMarker.Text=Ui.T("불러오는 중…");
        try{
            string[] paths=await Task.Run(()=>Directory.GetDirectories((string)node.Tag).OrderBy(path=>path,new NaturalSort()).ToArray());
            if(galleryClosing||IsDisposed||node.TreeView!=folders)return;
            node.Nodes.Remove(folder.ExpandMarker);
            var children=node.Nodes.Cast<TreeNode>().Where(child=>child.Tag is string)
                .ToDictionary(child=>(string)child.Tag,StringComparer.OrdinalIgnoreCase);
            folders.BeginUpdate();
            try{
                for(int i=0;i<paths.Length;i++){
                    TreeNode child;
                    if(!children.TryGetValue(paths[i],out child))
                        node.Nodes.Insert(Math.Min(i,node.Nodes.Count),FolderNode(Path.GetFileName(paths[i]),paths[i]));
                }
            }finally{folders.EndUpdate();}
            folder.Loaded=true;
        }catch(UnauthorizedAccessException){if(!IsDisposed)folder.ToolTipText=Ui.T("접근할 수 없음");}
        catch(IOException){if(!IsDisposed)folder.ToolTipText=Ui.T("폴더를 읽을 수 없음");}
        finally{folder.Loading=false;folder.ExpandMarker.Text="";}
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
                string nextPath=Path.Combine((string)current.Tag,part);
                var next=current.Nodes.Cast<TreeNode>().FirstOrDefault(n=>String.Equals(n.Tag as string,nextPath,StringComparison.OrdinalIgnoreCase));
                if(next==null){next=FolderNode(part,nextPath);current.Nodes.Add(next);}
                var folder=current as FolderTreeNode;
                if(folder!=null)folder.ExpandMarker.Remove();
                current.Expand();
                current=next;
            }
            folders.SelectedNode=current;current.EnsureVisible();
        }finally{syncingTree=false;}
    }
    void ToggleExplorer(){if(full){ExitImageView();return;}split.Panel1Collapsed=!split.Panel1Collapsed;}
    async void Browse(string path,string selected=null){
        if(full||galleryClosing)return;
        int version=++folderVersion;
        try{
            path=Path.GetFullPath(path);
            if(selected==null&&String.Equals(browsingFolder,path,StringComparison.OrdinalIgnoreCase)&&fileList.SelectedItems.Count>0)
                selected=fileList.SelectedItems[0].Tag as string;
            browsingFolder=path;
            pendingGallerySelection=selected;
            loadedGalleryFolder=null;
            galleryLoading=true;
            folderAddress.Text=path;
            Text=AppUpdater.WindowTitle(path);
            info.Text=Ui.T("불러오는 중…");
            info.ToolTipText=path;
            browserFiles=new string[0];
            sequenceGroups=new System.Collections.Generic.Dictionary<string,string[]>(StringComparer.OrdinalIgnoreCase);
            fileList.Items.Clear();UpdatePrintButtons();
            SelectFolderNode(path);
            bool group=groupSequences;
            var listing=await Task.Run(()=>{
                var files=Directory.GetFiles(path).Where(MediaFiles.Supported).OrderBy(file=>file,new NaturalSort()).ToArray();
                var sequences=group?Sequences.Group(files):new System.Collections.Generic.Dictionary<string,string[]>(StringComparer.OrdinalIgnoreCase);
                var hidden=new System.Collections.Generic.HashSet<string>(sequences.Values.SelectMany(items=>items.Skip(1)),StringComparer.OrdinalIgnoreCase);
                return new {Files=files.Where(file=>!hidden.Contains(file)).ToArray(),Sequences=sequences};
            });
            if(IsDisposed||version!=folderVersion)return;
            fileList.BeginUpdate();
            try{
                sequenceGroups=listing.Sequences;
                browserFiles=listing.Files;
                var oldThumbnails=thumbnails;
                thumbnails=CreateGalleryImages();
                fileList.LargeImageList=thumbnails;
                oldThumbnails.Dispose();
                var items=new ListViewItem[browserFiles.Length];
                for(int i=0;i<items.Length;i++){
                    items[i]=new ListViewItem("",0){Tag=browserFiles[i]};
                    ApplyGalleryItemText(items[i]);
                }
                fileList.Items.AddRange(items);
                loadedGalleryFolder=path;
                SelectGalleryFile(pendingGallerySelection,true);
            }finally{fileList.EndUpdate();}
            galleryLoading=false;
            UpdateBrowserInfo();
            int priority=fileList.SelectedIndices.Count==0?0:fileList.SelectedIndices[0];
            LoadThumbnails(listing.Files,version,priority);
        }catch(Exception ex){
            if(IsDisposed||version!=folderVersion)return;
            galleryLoading=false;
            info.Text=Ui.T("폴더를 열 수 없습니다");
            info.ToolTipText=ex.Message;
        }
    }
    static ImageList CreateGalleryImages(){
        var images=new ImageList{ImageSize=new Size(160,120),ColorDepth=ColorDepth.Depth32Bit};
        using(var placeholder=MakePlaceholder("…")){images.Images.Add(placeholder);var handle=images.Handle;}
        return images;
    }
    void ApplyGalleryItemText(ListViewItem item){
        string file=(string)item.Tag;
        string[] sequence;
        bool grouped=sequenceGroups.TryGetValue(file,out sequence);
        item.Text=Path.GetFileName(file)+(grouped?"  ["+sequence.Length+Ui.T(" 프레임]"):"");
        item.ToolTipText=file+(grouped?Ui.T("\nSQ 시퀀스 · ")+sequence.Length+Ui.T(" 프레임"):"");
    }
    void SelectGalleryFile(string path,bool selectFirst=false){
        string[] group=path==null?null:sequenceGroups.Values.FirstOrDefault(items=>items.Contains(path,StringComparer.OrdinalIgnoreCase));
        string visiblePath=group==null?path:group[0];
        var item=fileList.Items.Cast<ListViewItem>().FirstOrDefault(candidate=>String.Equals(candidate.Tag as string,visiblePath,StringComparison.OrdinalIgnoreCase));
        if(item==null&&selectFirst&&fileList.Items.Count>0)item=fileList.Items[0];
        if(item!=null){item.Selected=true;item.Focused=true;item.EnsureVisible();}
    }
    void ShowFileFolder(string file){
        if(String.IsNullOrEmpty(file))return;
        string directory=Path.GetDirectoryName(file);
        if(galleryLoading&&String.Equals(browsingFolder,directory,StringComparison.OrdinalIgnoreCase)){
            pendingGallerySelection=file;
            return;
        }
        if(!String.Equals(loadedGalleryFolder,directory,StringComparison.OrdinalIgnoreCase))Browse(directory,file);
        else SelectGalleryFile(file);
    }
    static Bitmap MakePlaceholder(string label){var b=new Bitmap(160,120);using(var g=Graphics.FromImage(b)){g.Clear(Color.FromArgb(55,59,65));using(var f=new Font("Segoe UI",14))using(var brush=new SolidBrush(Color.Silver))g.DrawString(label,f,brush,new RectangleF(0,45,160,40),new StringFormat{Alignment=StringAlignment.Center});}return b;}
    static Bitmap MakeThumbnail(string path){if(ModelFiles.Supported(path))return ModelFiles.Tile(path);if(MediaFiles.IsVideo(path))return MediaFiles.VideoTile(path);
        string ext=Path.GetExtension(path).ToLowerInvariant();
        using(var source=PdfPages.IsPdf(path)?PdfPages.Render(path,0,320):ExtendedDecoder.Extensions.Contains(ext)?ExtendedDecoder.Decode(path,320,240):Album.Decode(path)){
            var b=new Bitmap(160,120);using(var g=Graphics.FromImage(b)){
                g.Clear(Color.FromArgb(50,54,60));double scale=Math.Min(152.0/source.Width,112.0/source.Height);
                int w=Math.Max(1,(int)(source.Width*scale)),h=Math.Max(1,(int)(source.Height*scale));var rect=new Rectangle((160-w)/2,(120-h)/2,w,h);
                using(var tile=new Bitmap(24,24)){using(var t=Graphics.FromImage(tile)){t.Clear(Color.WhiteSmoke);t.FillRectangle(Brushes.LightGray,12,0,12,12);t.FillRectangle(Brushes.LightGray,0,12,12,12);}using(var brush=new TextureBrush(tile))g.FillRectangle(brush,rect);}
                g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(source,rect);
            }return b;
        }
    }
    async void LoadThumbnails(string[] paths,int version,int priority){
        await thumbnailWorker.WaitAsync();
        try{
            for(int offset=0;offset<paths.Length;offset++){
                int i=(priority+offset)%paths.Length;
                while((playbackActive||full)&&!IsDisposed&&version==folderVersion)await Task.Delay(150);
                if(IsDisposed || version!=folderVersion)return;
                string p=paths[i];bool failed=false;
                var bitmap=await Task.Run(delegate{try{return MakeThumbnail(p);}catch{failed=true;return MakePlaceholder(Ui.T("미리보기 없음"));}});
                using(bitmap){if(IsDisposed || version!=folderVersion)return;if(sequenceGroups.ContainsKey(p))Sequences.Badge(bitmap);thumbnails.Images.Add(bitmap);fileList.Items[i].ImageIndex=thumbnails.Images.Count-1;if(failed)fileList.Items[i].ToolTipText=p+"\n미리보기를 읽을 수 없습니다. 더블클릭하면 오류를 확인할 수 있습니다.";}
            }
        }finally{thumbnailWorker.Release();}
    }
    void UpdateBrowserInfo(){UpdatePrintButtons();if(full||busy||galleryLoading||galleryClosing||Disposing)return;string name=fileList.SelectedItems.Count>0?(string)fileList.SelectedItems[0].Tag:"";info.Text=browserFiles.Length+Ui.T("개 파일  ·  더블클릭: 열기")+"  ·  "+Path.GetFileName(name);info.ToolTipText=name;}
    void SyncExplorer(){
        if(!full)ShowFileFolder(album.Current);
    }
    void OpenSelectedImage(bool fullscreen=false){
        if(busy||fileList.SelectedItems.Count==0)return;
        string path=(string)fileList.SelectedItems[0].Tag;
        fullscreenAfterLoad=fullscreen&&Album.Supported(path);
        Open(path);
    }
    void ShowMediaViewer(MediaViewerForm viewer){
        bool restoreBrowser=Visible;
        if(restoreBrowser)Hide();
        try{viewer.ShowDialog(this);}
        finally{
            if(!IsDisposed&&!Disposing){
                if(viewer.ExitApplicationRequested)Close();
                else if(restoreBrowser){
                    Show();
                    Activate();
                    RefreshGallerySurface();
                }
            }
        }
    }
    void RefreshGallerySurface(){
        if(!browser.Visible)return;
        PerformLayout();
        browser.PerformLayout();
        Invalidate(true);
        Update();
    }
    async void LoadAsync(string path,int index,string[] wheelFiles=null,int page=0,bool fromDial=false){
        var previousOrder=album.WheelFiles;
        if(path!=null){
            wheelIndex=-1;canvas.Sidebar.Hide();
            if(wheelFiles==null){dialInput.Reset();pendingSteps=0;}
        }
        busy=true;UpdatePrintButtons();UseWaitCursor=true;info.Text=Ui.T("이미지를 읽는 중…");
        var files=(string[])album.Files.Clone();
        try{
            var next=await Task.Run(delegate{var a=new Album();try{if(path!=null)a.OpenFromFolder(path,wheelFiles,page);else{a.Files=files;a.Select(index,page);}return a;}catch{a.Dispose();throw;}});
            if(IsDisposed){next.Dispose();return;}
            if(path!=null)album.WheelFiles=next.WheelFiles;
            canvas.Image=null;album.Dispose();album.Image=next.Image;next.Image=null;album.Files=next.Files;album.Index=next.Index;album.PageIndex=next.PageIndex;album.PageCount=next.PageCount;next.Dispose();detailsFile=null;Loaded();
            if(enterAfterLoad && !full)EnterImageView();
            if(fullscreenAfterLoad && full)SetImageFullscreen(true);
            if(full&&sidebarButton.Checked&&(fromDial||wheelFiles!=null||album.PageCount>1||canvas.Sidebar.IsGroupedFrame(album.WheelFiles,album.Current)))RevealImageDial();
            else canvas.Sidebar.Select(album.WheelFiles,wheelIndex,album.Image,album.PageIndex,album.PageCount);
            if(startSlideAfterLoad && full){startSlideAfterLoad=false;ToggleSlide();}
        }catch(Exception ex){
            pendingSteps=0;album.WheelFiles=previousOrder;wheelIndex=Array.IndexOf(previousOrder,album.Current);
            if(!IsDisposed){StopSlide();if(wheelFiles!=null&&album.Image==null)ExitImageView();MessageBox.Show(this,ex.Message,Ui.T("이미지를 열 수 없습니다"),MessageBoxButtons.OK,MessageBoxIcon.Information);}
        }
        finally{if(startupOpening){startupOpening=false;if(album.Image==null&&!IsDisposed)ExitImageView();}enterAfterLoad=false;fullscreenAfterLoad=false;startSlideAfterLoad=false;busy=false;if(!IsDisposed){UseWaitCursor=false;UpdateInfo();if(!full)UpdateBrowserInfo();ContinueWheel();}}
    }
    void EnterImageView(bool allowEmpty=false){
        if((album.Image==null&&!allowEmpty) || full)return;
        full=true;
        galleryMinimumSize=MinimumSize;
        MinimumSize=new Size(360,230);
        browser.Visible=false;
        Controls.Add(canvas);
        canvas.Visible=true;
        canvas.BringToFront();
        toolbar.Visible=true;
        canvas.Focus();
        canvas.Reset();
    }
    void SetImageFullscreen(bool enabled,Rectangle? targetScreen=null){
        if(!full||imageFullscreen==enabled)return;
        imageFullscreen=enabled;
        sidebarButton.Checked=Ui.WheelSidebarEnabled;
        canvas.Sidebar.Hide();
        if(enabled){
            var screen=targetScreen??Screen.FromControl(this).Bounds;
            oldState=WindowState;
            oldBounds=WindowState==FormWindowState.Normal?Bounds:RestoreBounds;
            WindowState=FormWindowState.Normal;
            FormBorderStyle=FormBorderStyle.None;
            Bounds=screen;
            status.Visible=false;
            if(chrome==null){
                chrome=new FullscreenChrome(this,toolbar,null,delegate(Keys key){var message=new Message();return ProcessCmdKey(ref message,key);});
                chrome.TopInsetChanged+=canvas.SetDetailsTopInset;
            }
            chrome.Enable();
        }else{
            chrome.DockToolbarInOwner();
            canvas.BringToFront();
            FormBorderStyle=FormBorderStyle.Sizable;
            Bounds=oldBounds;
            WindowState=oldState;
            status.Visible=true;
        }
        imageModeButton.Text=enabled?ComponentSetup.Say("창 모드","Windowed mode"):ComponentSetup.Say("전체화면","Fullscreen");
        imageModeButton.ToolTipText=imageModeButton.Text+" ("+Shortcuts.Format("fullscreen")+")";
        canvas.Focus();
    }
    void ExitImageView(){
        pendingSteps=0;dialInput.Reset();canvas.Sidebar.Hide();
        SuspendLayout();
        try{
            if(full){SetImageFullscreen(false);MinimumSize=galleryMinimumSize;}
            StopSlide();full=false;canvas.Visible=false;Controls.Remove(canvas);
            toolbar.Visible=false;status.Visible=true;browser.Visible=true;browser.BringToFront();
        }finally{ResumeLayout(true);}
        Text=AppUpdater.WindowTitle(browsingFolder);
        RefreshGallerySurface();
        if(album.Image!=null)SyncExplorer();
        else if(browsingFolder!=null&&!galleryLoading&&!String.Equals(loadedGalleryFolder,browsingFolder,StringComparison.OrdinalIgnoreCase))Browse(browsingFolder);
        fileList.Focus();UpdateBrowserInfo();
    }
    void Fullscreen(){if(full)SetImageFullscreen(!imageFullscreen);else OpenSelectedImage(true);}
    void ShowPicker(){
        if(busy)return;StopSlide();string[] files=full?album.Files:browserFiles;if(files.Length==0)return;
        int selected=full?album.Index:(fileList.SelectedIndices.Count>0?fileList.SelectedIndices[0]:0);
        using(var picker=new ImagePicker(files,selected,canvas.BackgroundOne)){if(picker.ShowDialog(this)==DialogResult.OK){enterAfterLoad=true;Open(files[picker.SelectedIndex]);}}
    }
}
}

