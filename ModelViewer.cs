using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LumaView {
static class ModelFiles {
    public static bool Supported(string path){return new[]{".obj",".glb",".gltf",".stl",".fbx"}.Contains(Path.GetExtension(path).ToLowerInvariant());}
    public static string PreviewPath(string path){
        var file=new FileInfo(path);string stamp=Path.GetFullPath(path)+"|"+file.Length+"|"+file.LastWriteTimeUtc.Ticks;
        using(var hash=SHA256.Create()){return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LumaView","model-thumbnails",BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(stamp))).Replace("-","")+".png");}
    }
    static readonly System.Collections.Generic.Dictionary<string,byte[]> previews=new System.Collections.Generic.Dictionary<string,byte[]>();
    static readonly object previewLock=new object();
    public static byte[] PreviewData(string path){
        string key=PreviewPath(path);lock(previewLock){byte[] data;if(previews.TryGetValue(key,out data))return data;}
        try{return File.Exists(key)?File.ReadAllBytes(key):null;}catch(IOException){return null;}catch(UnauthorizedAccessException){return null;}
    }
    public static void StorePreview(string path,byte[] bytes){
        string destination=PreviewPath(path);
        lock(previewLock){if(previews.Count>=128)previews.Remove(previews.Keys.First());previews[destination]=bytes;}
        // Memory cache remains usable even if the local disk cache is unavailable.
        string temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{Directory.CreateDirectory(Path.GetDirectoryName(destination));File.WriteAllBytes(temporary,bytes);if(File.Exists(destination))File.Replace(temporary,destination,null);else File.Move(temporary,destination);}
        catch(IOException){}catch(UnauthorizedAccessException){}
        finally{try{if(File.Exists(temporary))File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    public static Bitmap Tile(string path){
        var bitmap=new Bitmap(180,120);using(var g=Graphics.FromImage(bitmap)){
            g.Clear(Color.FromArgb(32,35,40));bool preview=false;
            try{byte[] cached=PreviewData(path);if(cached!=null)using(var stream=new MemoryStream(cached))using(var image=Image.FromStream(stream)){g.DrawImage(image,0,0,180,120);preview=true;}}catch(IOException){}catch(ArgumentException){}
            if(!preview)using(var pen=new Pen(Color.FromArgb(133,204,230),3)){
                var top=new[]{new Point(90,22),new Point(124,40),new Point(90,59),new Point(56,40)};g.DrawPolygon(pen,top);g.DrawLines(pen,new[]{new Point(56,40),new Point(56,77),new Point(90,96),new Point(124,77),new Point(124,40)});g.DrawLine(pen,90,59,90,96);
            }
            using(var font=new Font("Segoe UI",10,FontStyle.Bold)){g.FillRectangle(Brushes.Black,130,92,50,28);g.DrawString("3D",font,Brushes.White,139,96);}
        }return bitmap;
    }
}
partial class Viewer {
    void OpenModel(string path){
        StopSlide();enterAfterLoad=false;startSlideAfterLoad=false;if(full&&!startupOpening)ExitImageView();
        string directory=Path.GetDirectoryName(Path.GetFullPath(path));if(!String.Equals(directory,browsingFolder,StringComparison.OrdinalIgnoreCase))Browse(directory);
        playbackActive=true;
        var area=Screen.FromControl(this).WorkingArea;
        var size=new Size(Math.Min(1200,area.Width),Math.Min(820,area.Height));
        var bounds=new Rectangle(area.X+(area.Width-size.Width)/2,area.Y+(area.Height-size.Height)/2,size.Width,size.Height);
        try{using(var player=new ModelViewer(path,bounds))ShowMediaViewer(player);}
        finally{playbackActive=false;if(startupOpening&&!IsDisposed){startupOpening=false;ExitImageView();}}
        if(IsDisposed)return;
        foreach(ListViewItem item in fileList.Items)if(String.Equals(item.Tag as string,path,StringComparison.OrdinalIgnoreCase)){item.Selected=true;item.EnsureVisible();}
        fileList.Focus();UpdateBrowserInfo();
    }
}
sealed class ModelViewer:MediaViewerForm {
    readonly WebView2 web=new WebView2{Dock=DockStyle.Fill,DefaultBackgroundColor=Color.FromArgb(32,35,40)};
    readonly ToolStrip tools=new ViewerToolStrip{GripStyle=ToolStripGripStyle.Hidden,Padding=new Padding(10,8,10,8)};
    readonly Label status=new Label{Dock=DockStyle.Fill,ForeColor=Color.White,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(12,4,12,4)};
    readonly Label title=new Label{Height=30,ForeColor=Color.White,Padding=new Padding(12,5,0,0)};
    readonly FullscreenChrome chrome;
    readonly string path;
    readonly JavaScriptSerializer json=new JavaScriptSerializer();
    bool ready,closing,lightMode,editingLight;
    bool fullscreen;
    Rectangle windowBounds;
    FormWindowState windowState;
    ToolStripButton modeButton;
    ToolStripButton detailsButton,lightButton,lightSettingsButton;
    bool lightSelected,effectsPanelOpen;
    readonly System.Collections.Generic.Dictionary<string,ToolStripButton> visualButtons=new System.Collections.Generic.Dictionary<string,ToolStripButton>();
    string statistics="";
    public ModelViewer(string file,Rectangle bounds){
        path=Path.GetFullPath(file);Text=AppUpdater.WindowTitle(Path.GetFileName(path)+" · 3D");BackColor=Color.FromArgb(32,35,40);Font=new Font("Malgun Gothic",9);FormBorderStyle=FormBorderStyle.Sizable;MinimumSize=new Size(400,310);StartPosition=FormStartPosition.Manual;Bounds=bounds;KeyPreview=true;
        Add(Ui.T("목록으로 (Esc)"),delegate{Close();});Add(Ui.T("화면 맞춤"),delegate{Command("iso");});Add(Ui.T("정면"),delegate{Command("front");});Add(Ui.T("측면"),delegate{Command("side");});Add(Ui.T("윗면"),delegate{Command("top");});
        modeButton=Add(ComponentSetup.Say("전체화면","Fullscreen"),ToggleFullscreen);
        modeButton.ToolTipText="Alt+Enter";
        var effectsButton=Add(Ui.Language=="en"?"Rendering effects":"렌더링 효과",delegate{ToggleRenderEffects();});effectsButton.Enabled=false;effectsButton.Tag="model";
        Toggle(Ui.T("와이어프레임"),"wire",false);Toggle(Ui.T("텍스처"),"textures",true);Toggle(Ui.T("그리드"),"grid",true);Toggle(Ui.T("플랫 컬러"),"flat",false);Toggle("SSAO","ssao",false);
        lightButton=Add(Ui.T("라이트 추가 (L)"),delegate{Command("light");});lightButton.Enabled=false;lightButton.Tag="model";
        lightSettingsButton=Add(Ui.T("라이트 설정"),delegate{Command("editLight");});lightSettingsButton.Enabled=false;
        detailsButton=Add(Ui.T("상세 정보"),delegate{});detailsButton.CheckOnClick=true;detailsButton.Enabled=false;detailsButton.Tag="model";detailsButton.CheckedChanged+=delegate{UpdateDetailsOverlay();};
        status.Text=Ui.T("3D 모델을 읽는 중…");title.Text=Path.GetFileName(path);
        var bottom=new Panel{Height=42,BackColor=BackColor};bottom.Controls.Add(status);Controls.Add(web);
        chrome=new FullscreenChrome(this,tools,bottom,delegate(Keys key){var msg=new Message();return ProcessCmdKey(ref msg,key);});chrome.AddTop(title);tools.SizeChanged+=delegate{UpdateDetailsOverlay();};web.SizeChanged+=delegate{UpdateDetailsOverlay();};
        chrome.DockToolbarInOwner();web.BringToFront();
        ModernUi.TitleBar(this);
        HandleCreated+=delegate{ModernUi.TitleBar(this);};
        Shown+=delegate{Initialize();};
        FormClosed+=delegate{closing=true;chrome.Dispose();web.Dispose();};
    }
    void ToggleFullscreen(){
        if(closing)return;
        fullscreen=!fullscreen;
        if(fullscreen){
            var screen=Screen.FromControl(this).Bounds;
            windowState=WindowState;
            windowBounds=windowState==FormWindowState.Normal?Bounds:RestoreBounds;
            WindowState=FormWindowState.Normal;
            FormBorderStyle=FormBorderStyle.None;
            Bounds=screen;
            chrome.Enable();
        }else{
            chrome.DockToolbarInOwner();
            web.BringToFront();
            FormBorderStyle=FormBorderStyle.Sizable;
            Bounds=windowBounds;
            WindowState=windowState;
        }
        modeButton.Text=fullscreen?ComponentSetup.Say("창 모드","Windowed mode"):ComponentSetup.Say("전체화면","Fullscreen");
        UpdateDetailsOverlay();
        web.Focus();
    }
    void EscapeView(){Close();}
    ToolStripButton Add(string label,Action action){var button=new ToolStripButton(label){Padding=new Padding(4,3,4,3)};button.Click+=delegate{action();};tools.Items.Add(button);return button;}
    void Toggle(string label,string command,bool initial){var b=Add(label,delegate{Command(command);});b.CheckOnClick=true;b.Checked=initial;b.Enabled=false;b.Tag="model";visualButtons[command]=b;if(command=="ssao")b.ToolTipText=Ui.T("접촉부 음영을 표시합니다. 플랫 컬러에서는 적용하지 않습니다.");}
    async void Command(string action){if(!ready||closing)return;try{await web.CoreWebView2.ExecuteScriptAsync("window.modelCommand("+json.Serialize(action)+")");web.Focus();}catch(Exception ex){if(!closing)status.Text=ex.Message;}}
    async void ToggleRenderEffects(){
        if(!ready||closing)return;
        try{await web.CoreWebView2.ExecuteScriptAsync("window.toggleRenderEffects("+(Ui.Language=="en"?"true":"false")+")");web.Focus();}catch(Exception ex){if(!closing)status.Text=ex.Message;}
    }
    async void UpdateDetailsOverlay(){
        if(!ready||closing)return;
        string text=Path.GetFileName(path)+"\n"+statistics;
        try{var info=new FileInfo(path);text+="\n"+Ui.T("파일 크기: ")+(info.Length/1048576.0).ToString("N2")+" MB\n"+Ui.T("수정한 날짜: ")+info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");}catch(IOException){}
        int top=fullscreen?32+title.Height+tools.Height+12:12;
        try{await web.CoreWebView2.ExecuteScriptAsync("window.setModelDetails("+json.Serialize(text)+","+top+","+(detailsButton.Checked?"true":"false")+")");}catch(Exception ex){if(!closing)status.Text=ex.Message;}
    }
    async void ApplyLight(int id,Color color,decimal strength){
        if(closing)return;
        try{await web.CoreWebView2.ExecuteScriptAsync("window.updateModelLight("+id+","+json.Serialize("#"+color.R.ToString("X2")+color.G.ToString("X2")+color.B.ToString("X2"))+","+strength.ToString(System.Globalization.CultureInfo.InvariantCulture)+")");}catch(Exception ex){if(!closing)status.Text=ex.Message;}
    }
    async void EditLight(int id,string hex,decimal strength){
        if(closing||editingLight)return;editingLight=true;
        try{
            Color original=ColorTranslator.FromHtml(hex);
            using(var editor=new ModelLightEditor(original,strength)){
                editor.Preview+=(color,value)=>ApplyLight(id,color,value);
                var result=editor.ShowDialog(this);
                if(result==DialogResult.Ignore){await web.CoreWebView2.ExecuteScriptAsync("window.removeModelLight("+id+")");}
                else if(result!=DialogResult.OK)ApplyLight(id,original,strength);
            }
        }catch(Exception ex){if(!closing)status.Text=ex.Message;}
        finally{editingLight=false;if(!closing)web.Focus();}
    }
    async void Initialize(){
        try{
            var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LumaView","webview3d"));
            if(closing)return;await web.EnsureCoreWebView2Async(environment);if(closing)return;
            var core=web.CoreWebView2;core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.AreDevToolsEnabled=false;core.Settings.IsStatusBarEnabled=false;core.Settings.IsZoomControlEnabled=false;
            core.SetVirtualHostNameToFolderMapping("viewer.lumaview.invalid",Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"model-viewer"),CoreWebView2HostResourceAccessKind.DenyCors);
            core.SetVirtualHostNameToFolderMapping("model.lumaview.invalid",Path.GetDirectoryName(path),CoreWebView2HostResourceAccessKind.Allow);
            core.NavigationStarting+=delegate(object s,CoreWebView2NavigationStartingEventArgs e){if(!e.Uri.StartsWith("https://viewer.lumaview.invalid/",StringComparison.OrdinalIgnoreCase))e.Cancel=true;};
            core.NewWindowRequested+=delegate(object s,CoreWebView2NewWindowRequestedEventArgs e){e.Handled=true;};
            core.PermissionRequested+=delegate(object s,CoreWebView2PermissionRequestedEventArgs e){e.State=CoreWebView2PermissionState.Deny;};
            core.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);
            core.WebResourceRequested+=delegate(object s,CoreWebView2WebResourceRequestedEventArgs e){
                Uri uri;if(Uri.TryCreate(e.Request.Uri,UriKind.Absolute,out uri)&&uri.Scheme=="https"&&(uri.Host=="viewer.lumaview.invalid"||uri.Host=="model.lumaview.invalid"))return;
                if(e.Request.Uri.StartsWith("data:")||e.Request.Uri.StartsWith("blob:"))return;
                e.Response=environment.CreateWebResourceResponse(new MemoryStream(),403,"Local resources only","");
            };
            core.WebMessageReceived+=OnMessage;
            core.NavigationCompleted+=delegate(object s,CoreWebView2NavigationCompletedEventArgs e){if(!e.IsSuccess&&!closing)status.Text=Ui.T("3D 화면을 열 수 없습니다.")+" "+e.WebErrorStatus;};
            core.Navigate("https://viewer.lumaview.invalid/index.html");
        }catch(Exception ex){if(!closing)status.Text=Ui.T("3D 화면을 열 수 없습니다.")+" "+(String.IsNullOrEmpty(ComponentSetup.WebViewVersion())?ComponentSetup.Say("설정 → 구성 요소 → 다운로드 및 연결에서 WebView2를 설치한 뒤 모델을 다시 여세요.","Install WebView2 in Settings → Components → Download and connect, then reopen the model."):ex.Message);}
    }
    async void OnMessage(object sender,CoreWebView2WebMessageReceivedEventArgs e){
        if(closing||!e.Source.StartsWith("https://viewer.lumaview.invalid/",StringComparison.OrdinalIgnoreCase))return;
        try{
            string message=e.TryGetWebMessageAsString();
            if(message=="ready"){
                string url="https://model.lumaview.invalid/"+Uri.EscapeDataString(Path.GetFileName(path));
                await web.CoreWebView2.ExecuteScriptAsync("window.loadModel("+json.Serialize(url)+","+json.Serialize(Path.GetExtension(path).Substring(1).ToLowerInvariant())+")");
            }else if(message=="exit"){EscapeView();}
            else if(message=="toggleFullscreen"){ToggleFullscreen();}
            else if(message=="toggleDetails"){detailsButton.PerformClick();}
            else if(message.StartsWith("renderPanel:")){effectsPanelOpen=message.EndsWith(":on");}
            else if(message.StartsWith("renderBase:")){var data=json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(message.Substring(11));foreach(var name in new[]{"ssao","flat","textures"})if(data.ContainsKey(name))visualButtons[name].Checked=Convert.ToBoolean(data[name]);}
            else if(message.StartsWith("lightMode:")){lightMode=message.EndsWith(":on");lightButton.Checked=lightMode;status.Text=lightMode?Ui.T("빈 공간을 클릭해 라이트를 배치하세요. Esc: 취소"):Ui.T("라이트 클릭: 선택·이동 · 더블클릭: 색상·강도 · L: 추가");}
            else if(message.StartsWith("lightSelected:")){lightSelected=message!="lightSelected:-1";lightSettingsButton.Enabled=lightSelected;if(lightSelected)status.Text=Ui.T("축 드래그: 라이트 이동 · 더블클릭/라이트 설정: 색상·강도 · Esc: 선택 해제");}
            else if(message.StartsWith("editLight:")){var data=json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(message.Substring(10));int id=Convert.ToInt32(data["id"]);string color=(string)data["color"];decimal strength=Convert.ToDecimal(data["strength"]);BeginInvoke((Action)delegate{EditLight(id,color,strength);});}
            else if(message.StartsWith("loaded:")){
                ready=true;var data=json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(message.Substring(7));
                var dimensions=(System.Collections.IEnumerable)data["size"];var values=new System.Collections.Generic.List<string>();foreach(var value in dimensions)values.Add(Convert.ToDouble(value).ToString("0.###"));
                statistics=Ui.T("메시: ")+data["meshes"]+Ui.T(" · 삼각형: ")+Convert.ToInt64(data["triangles"]).ToString("N0")+Ui.T(" · 재질: ")+data["materials"]+"\n"+Ui.T("크기 (모델 단위): ")+String.Join(" × ",values);
                status.Text=Ui.T("좌클릭 드래그: 회전 · 우클릭 드래그: 이동 · 휠: 확대/축소 · F: 화면 맞춤");
                foreach(ToolStripItem item in tools.Items)if((string)item.Tag=="model")item.Enabled=true;
                UpdateDetailsOverlay();web.Focus();
            }else if(message.StartsWith("error:")){status.Text=Ui.T("모델 읽기 실패: ")+message.Substring(6);}
            else if(message.StartsWith("thumbnail:")&&message.Length<1000000){
                try{byte[] bytes=Convert.FromBase64String(message.Substring(10));using(var stream=new MemoryStream(bytes))using(var image=Image.FromStream(stream)){if(image.Width>512||image.Height>512)return;ModelFiles.StorePreview(path,bytes);}}catch(IOException){}catch(ArgumentException){}
            }
        }catch(Exception ex){if(!closing)status.Text=ex.Message;}
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys key){
        if(key==(Keys.Alt|Keys.Enter)){ToggleFullscreen();return true;}
        if(effectsPanelOpen&&web.ContainsFocus){
            if(key==Keys.Escape){ToggleRenderEffects();return true;}
            return base.ProcessCmdKey(ref msg,key);
        }
        if(key==Keys.Escape){
            if(lightMode)Command("cancelLight");else if(lightSelected)Command("clearSelection");else EscapeView();
            return true;
        }
        if(key==Keys.L){Command("light");return true;}
        if(key==Keys.I){detailsButton.PerformClick();return true;}
        if(key==Keys.F){Command("iso");return true;}
        return base.ProcessCmdKey(ref msg,key);
    }
}
sealed class ModelLightEditor:Form {
    readonly Button colorButton=new Button{Location=new Point(120,20),Size=new Size(220,34)};
    readonly NumericUpDown strength=new NumericUpDown{Location=new Point(120,75),Width=120,DecimalPlaces=2,Minimum=0,Maximum=100,Increment=.1m};
    readonly TrackBar slider=new TrackBar{Location=new Point(16,115),Width=355,Minimum=0,Maximum=1000,TickFrequency=100,LargeChange=10};
    Color selected;
    public event Action<Color,decimal> Preview;
    public ModelLightEditor(Color color,decimal value){
        Text=Ui.T("포인트 라이트");ClientSize=new Size(390,260);StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;Font=new Font("Malgun Gothic",9);selected=color;
        Controls.Add(new Label{Text=Ui.T("색상"),Location=new Point(20,27),AutoSize=true});Controls.Add(colorButton);
        Controls.Add(new Label{Text=Ui.T("강도"),Location=new Point(20,80),AutoSize=true});Controls.Add(strength);Controls.Add(slider);
        Controls.Add(new Label{Text=Ui.T("플랫 컬러에서는 조명 효과가 보이지 않습니다."),Location=new Point(20,172),AutoSize=true});
        var ok=new Button{Text=Ui.T("확인"),Location=new Point(200,215),Size=new Size(80,30),DialogResult=DialogResult.OK};var cancel=new Button{Text=Ui.T("취소"),Location=new Point(290,215),Size=new Size(80,30),DialogResult=DialogResult.Cancel};var remove=new Button{Text=Ui.T("라이트 삭제"),Location=new Point(20,215),Size=new Size(110,30),DialogResult=DialogResult.Ignore};Controls.AddRange(new Control[]{ok,cancel,remove});AcceptButton=ok;CancelButton=cancel;
        strength.Value=Math.Max(0,Math.Min(100,value));slider.Value=(int)(strength.Value*10);
        colorButton.Click+=delegate{using(var picker=new ColorDialog{Color=selected,FullOpen=true})if(picker.ShowDialog(this)==DialogResult.OK){selected=picker.Color;UpdateColor();Notify();}};
        strength.ValueChanged+=delegate{slider.Value=(int)(strength.Value*10);Notify();};slider.Scroll+=delegate{strength.Value=slider.Value/10m;};
        Ui.Apply(this);UpdateColor();
    }
    void UpdateColor(){colorButton.BackColor=selected;colorButton.ForeColor=selected.GetBrightness()>.5f?Color.Black:Color.White;colorButton.Text="#"+selected.R.ToString("X2")+selected.G.ToString("X2")+selected.B.ToString("X2");}
    void Notify(){if(Preview!=null)Preview(selected,strength.Value);}
}
}








