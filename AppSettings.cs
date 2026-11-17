using System;
using System.IO;
using System.Drawing;
using System.Collections.Generic;
using System.Windows.Forms;

namespace LumaView {
static class Ui {
    public static string Language="ko",Theme="dark";
    public static readonly string SettingsPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LumaView","settings.ini");
    static readonly Dictionary<string,string> english=new Dictionary<string,string>();
    static readonly Dictionary<string,string> korean=new Dictionary<string,string>();
    static Ui(){foreach(string line in Translations.Split('\n')){var parts=line.TrimEnd('\r').Split('\t');if(parts.Length==2){string a=parts[0].Replace("\\n","\n"),b=parts[1].Replace("\\n","\n");english[a]=b;korean[b]=a;}}}
    public static string T(string text){if(String.IsNullOrEmpty(text))return text??"";string canonical;if(korean.TryGetValue(text,out canonical))text=canonical;string translated;return Language=="en"&&english.TryGetValue(text,out translated)?translated:text;}
    public static Color[] CustomColors=DefaultColors();
    public static Color[] DefaultColors(){return new[]{Color.FromArgb(68,68,68),Color.FromArgb(68,68,68),Color.FromArgb(235,235,235),Color.FromArgb(90,90,90),Color.FromArgb(97,97,97)};}
    static readonly string[] ColorKeys={"background","surface","text","highlight","border"};
    public static void Load(){Language="ko";Theme="dark";CustomColors=DefaultColors();try{
        if(!File.Exists(SettingsPath))return;
        foreach(string line in File.ReadAllLines(SettingsPath)){
            if(line=="language=en")Language="en";
            if(line=="theme=light")Theme="light";
            if(line=="theme=custom"||line=="theme=medium")Theme="custom";
            for(int i=0;i<ColorKeys.Length;i++){
                string prefix="color."+ColorKeys[i]+"=";
                if(!line.StartsWith(prefix,StringComparison.Ordinal))continue;
                string hex=line.Substring(prefix.Length).TrimStart('#');int rgb;
                if(hex.Length==6&&Int32.TryParse(hex,System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out rgb))CustomColors[i]=Color.FromArgb(255,(rgb>>16)&255,(rgb>>8)&255,rgb&255);
            }
        }
    }catch(IOException){}catch(UnauthorizedAccessException){}}
    public static void Save(string language,string theme){SavePalette(language,theme,CustomColors);}
    public static void SavePalette(string language,string theme,Color[] colors){
        if(language!="ko"&&language!="en")throw new ArgumentException("language");if(theme!="dark"&&theme!="light"&&theme!="custom")throw new ArgumentException("theme");
        if(colors==null||colors.Length!=5)throw new ArgumentException("colors");
        var snapshot=(Color[])colors.Clone();
        string data="language="+language+"\r\ntheme="+theme+"\r\n";
        for(int i=0;i<ColorKeys.Length;i++)data+="color."+ColorKeys[i]+"="+Hex(snapshot[i])+"\r\n";
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));string temp=SettingsPath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,data);if(File.Exists(SettingsPath))File.Replace(temp,SettingsPath,null);else File.Move(temp,SettingsPath);}finally{if(File.Exists(temp))File.Delete(temp);}
        Language=language;Theme=theme;CustomColors=snapshot;
    }
    public static string Hex(Color color){return "#"+color.R.ToString("X2")+color.G.ToString("X2")+color.B.ToString("X2");}
    public static Color Background{get{return Theme=="light"?Color.FromArgb(244,246,248):Theme=="custom"?CustomColors[0]:Color.FromArgb(30,33,38);}}
    public static Color Surface{get{return Theme=="light"?Color.FromArgb(230,234,239):Theme=="custom"?CustomColors[1]:Color.FromArgb(40,44,51);}}
    public static Color Foreground{get{return Theme=="light"?Color.FromArgb(30,36,43):Theme=="custom"?CustomColors[2]:Color.FromArgb(239,242,246);}}
    public static Color Highlight{get{return Theme=="light"?Color.FromArgb(194,222,234):Theme=="custom"?CustomColors[3]:Color.FromArgb(45,86,101);}}
    public static Color Border{get{return Theme=="light"?Color.FromArgb(180,188,198):Theme=="custom"?CustomColors[4]:Color.FromArgb(135,143,154);}}
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control,object> painted=new System.Runtime.CompilerServices.ConditionalWeakTable<Control,object>();
    static void SelectionColors(Control control){
        object marker;if(painted.TryGetValue(control,out marker))return;
        var tree=control as TreeView;
        if(tree!=null){painted.Add(control,new object());tree.DrawMode=TreeViewDrawMode.OwnerDrawText;tree.DrawNode+=delegate(object sender,DrawTreeNodeEventArgs e){
            if(Theme!="custom"){e.DrawDefault=true;return;}
            bool selected=(e.State&TreeNodeStates.Selected)!=0;var bounds=new Rectangle(e.Bounds.X,e.Bounds.Y,Math.Max(1,tree.ClientSize.Width-e.Bounds.X),e.Bounds.Height);
            using(var brush=new SolidBrush(selected?Highlight:Background))e.Graphics.FillRectangle(brush,bounds);
            TextRenderer.DrawText(e.Graphics,e.Node.Text,tree.Font,e.Bounds,Foreground,TextFormatFlags.NoPrefix|TextFormatFlags.VerticalCenter);
        };}
        var list=control as ListView;
        if(list!=null&&list.View==View.LargeIcon){painted.Add(control,new object());list.OwnerDraw=true;list.DrawItem+=delegate(object sender,DrawListViewItemEventArgs e){
            if(Theme!="custom"){e.DrawDefault=true;return;}
            using(var brush=new SolidBrush(e.Item.Selected?Highlight:Background))e.Graphics.FillRectangle(brush,e.Bounds);
            int h=0;if(list.LargeImageList!=null&&e.Item.ImageIndex>=0&&e.Item.ImageIndex<list.LargeImageList.Images.Count){var size=list.LargeImageList.ImageSize;list.LargeImageList.Draw(e.Graphics,e.Bounds.X+(e.Bounds.Width-size.Width)/2,e.Bounds.Y+4,e.Item.ImageIndex);h=size.Height+8;}
            TextRenderer.DrawText(e.Graphics,e.Item.Text,list.Font,new Rectangle(e.Bounds.X+2,e.Bounds.Y+h,e.Bounds.Width-4,Math.Max(1,e.Bounds.Height-h)),Foreground,TextFormatFlags.HorizontalCenter|TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
            if(e.Item.Focused){using(var pen=new Pen(Border))e.Graphics.DrawRectangle(pen,e.Bounds.X,e.Bounds.Y,Math.Max(0,e.Bounds.Width-1),Math.Max(0,e.Bounds.Height-1));}
        };}
    }
    public static void Apply(Control control){
        if(control is CustomColorEditor){((CustomColorEditor)control).RefreshSwatches();return;}
        SelectionColors(control);
        bool image=control is Canvas||control is Filmstrip;
        if(!image){control.BackColor=control is TextBoxBase||control is ListView||control is TreeView?Background:Surface;control.ForeColor=Foreground;}
        if(!(control is TextBoxBase)&&!(control is ListView)&&!(control is TreeView))control.Text=T(control.Text);
        var button=control as ButtonBase;if(button!=null){button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderColor=Border;}
        var strip=control as ToolStrip;if(strip!=null){strip.Renderer=new ToolStripProfessionalRenderer(new Palette());foreach(ToolStripItem item in strip.Items)ApplyItem(item);}
        var list=control as ListView;if(list!=null)foreach(ColumnHeader column in list.Columns)column.Text=T(column.Text);
        if(control.ContextMenuStrip!=null)Apply(control.ContextMenuStrip);
        foreach(Control child in control.Controls)Apply(child);
        control.Invalidate();
    }
    static void ApplyItem(ToolStripItem item){item.Text=T(item.Text);item.ToolTipText=T(item.ToolTipText);item.BackColor=Surface;item.ForeColor=Foreground;var drop=item as ToolStripDropDownItem;if(drop!=null){drop.DropDown.Renderer=new ToolStripProfessionalRenderer(new Palette());foreach(ToolStripItem child in drop.DropDownItems)ApplyItem(child);}var combo=item as ToolStripComboBox;if(combo!=null){combo.BackColor=Background;combo.ForeColor=Foreground;for(int i=0;i<combo.Items.Count;i++)combo.Items[i]=T(Convert.ToString(combo.Items[i]));}}
    sealed class Palette:ProfessionalColorTable {
        public override Color ToolStripBorder{get{return Border;}}public override Color MenuBorder{get{return Border;}}public override Color ButtonSelectedBorder{get{return Border;}}public override Color ButtonCheckedHighlightBorder{get{return Border;}}public override Color SeparatorDark{get{return Border;}}
        public override Color ToolStripGradientBegin{get{return Surface;}}public override Color ToolStripGradientMiddle{get{return Surface;}}public override Color ToolStripGradientEnd{get{return Surface;}}
        public override Color MenuStripGradientBegin{get{return Surface;}}public override Color MenuStripGradientEnd{get{return Surface;}}
        public override Color ToolStripDropDownBackground{get{return Surface;}}
        public override Color ImageMarginGradientBegin{get{return Surface;}}public override Color ImageMarginGradientMiddle{get{return Surface;}}public override Color ImageMarginGradientEnd{get{return Surface;}}
        public override Color MenuItemSelected{get{return Highlight;}}public override Color MenuItemSelectedGradientBegin{get{return Highlight;}}public override Color MenuItemSelectedGradientEnd{get{return Highlight;}}
        public override Color ButtonSelectedGradientBegin{get{return Highlight;}}public override Color ButtonSelectedGradientEnd{get{return Highlight;}}
        public override Color ButtonCheckedGradientBegin{get{return Highlight;}}public override Color ButtonCheckedGradientMiddle{get{return Highlight;}}public override Color ButtonCheckedGradientEnd{get{return Highlight;}}
    }
    const string Translations=@"스카이돔	Sky dome
돔 밝기	Dome brightness
라이트 설정	Light settings
라이트 클릭: 선택·이동 · 더블클릭: 색상·강도 · L: 추가	Click light: select/move · Double-click: color/intensity · L: add
축 드래그: 라이트 이동 · 더블클릭/라이트 설정: 색상·강도 · Esc: 선택 해제	Drag axes: move light · Double-click/Light settings: color/intensity · Esc: deselect
접촉부 음영을 표시합니다. 플랫 컬러에서는 적용하지 않습니다.	Show contact shading. Bypassed in flat color mode.
플랫 컬러	Flat color
라이트 추가 (L)	Add light (L)
포인트 라이트	Point light
색상	Color
강도	Intensity
라이트 삭제	Delete light
플랫 컬러에서는 조명 효과가 보이지 않습니다.	Lights have no effect in flat color mode.
빈 공간을 클릭해 라이트를 배치하세요. Esc: 취소	Click empty space to place a light. Esc: cancel
라이트 클릭: 색상·강도 변경 · L: 라이트 추가	Click a light: color/intensity · L: add light
정면	Front
측면	Side
윗면	Top
와이어프레임	Wireframe
텍스처	Textures
그리드	Grid
3D 모델을 읽는 중…	Loading 3D model…
3D 화면을 열 수 없습니다.	Could not open the 3D viewer.
메시: 	Meshes: 
 · 삼각형: 	 · Triangles: 
 · 재질: 	 · Materials: 
크기 (모델 단위): 	Size (model units): 
좌클릭 드래그: 회전 · 우클릭 드래그: 이동 · 휠: 확대/축소 · F: 화면 맞춤	Left drag: orbit · Right drag: pan · Wheel: zoom · F: fit
모델 읽기 실패: 	Could not load model: 
재생 FPS	Playback FPS
실시간 재생	Real-time playback
읽기가 늦으면 프레임을 건너뛰어 재생 시간을 유지합니다.	Skip late frames to keep playback on time.
프레임 번호	Frame number
LumaView — 선택한 확장자	LumaView — Selected extensions
확장자를 체크하고 연결을 누르면 선택한 형식만 표시됩니다.\n열린 화면에서 LumaView를 선택하세요. 검색할 필요가 없습니다.	Connect opens a page containing only the checked extensions.\nChoose LumaView there; no extension search is needed.
연결	Connect
연결 후보	Available handler
확장자를 체크하고 연결을 누르세요. LumaView 기본 앱 화면이 바로 열립니다.\nWindows에서 최종 선택하면 돌아왔을 때 연결 상태가 갱신됩니다.	Check extensions and click Connect to open LumaView defaults directly.\nConfirm in Windows; status refreshes when you return.
Windows에서 선택할 확장자: 	Extensions to select in Windows: 
LumaView를 선택해 완료하세요. 연결 결과는 복귀 시 갱신됩니다.	Choose LumaView to finish. Results refresh when you return.
UI 배경	UI background
메뉴 / 버튼	Menus / buttons
글자	Text
선택 강조	Selection highlight
테두리	Borders
기본 색으로	Reset colors
미리보기	Preview
색상 선택	Choose color
일반	General
파일 연결	File associations
확장자	Extension
등록 상태	Registration
현재 기본 앱	Current default app
전체 선택	Select all
선택 항목 등록	Register selected
Windows 기본 앱 설정 열기	Open Windows default apps
등록됨	Registered
미등록	Not registered
확장자를 선택해 주세요.	Select at least one extension.
확장자를 선택하고 등록한 뒤 Windows에서 기본 앱을 LumaView로 선택하세요.\n등록만으로 현재 기본 앱이 바뀌지는 않습니다.	Select extensions and register them, then choose LumaView in Windows.\nRegistration alone does not change your default apps.
등록했습니다. Windows 기본 앱 설정에서 확장자별로 LumaView를 선택하세요.	Registered. Choose LumaView for each extension in Windows default apps.
설정	Settings
업데이트	Update
언어	Language
테마	Theme
어둡게	Dark
밝게	Light
커스텀	Custom
저장	Save
취소	Cancel
새로고침	Refresh
SQ 묶기	Group SQ
바탕 화면	Desktop
문서	Documents
사진	Pictures
다운로드	Downloads
내 PC	This PC
탐색기	Explorer
목록	List
열기	Open
폴더	Folder
◀ 이전	◀ Previous
다음 ▶	Next ▶
화면 맞춤	Fit
↷ 회전	↷ Rotate
반전	Flip
좌우 반전	Horizontal
상하 반전	Vertical
▶ 슬라이드쇼	▶ Slideshow
1초	1 sec
3초	3 sec
5초	5 sec
10초	10 sec
목록으로	Back to list
상세 정보	Details
배경	Background
밝은 체크 (기본)	Light checker (default)
어두운 체크	Dark checker
흰색	White
회색	Gray
검정	Black
사용자 배경색…	Custom background…
체크 크기 	Checker size 
이미지 선택	Select image
사진 목록	Images
수정 날짜	Modified
크기	Size
복사	Copy
붙여넣기	Paste
이름 바꾸기	Rename
새 파일 이름	New file name
변경	Rename
고정	Pin
고정됨	Pinned
메뉴와 재생바를 항상 표시합니다	Keep toolbar and playback controls visible
화면에서만 반전합니다. 원본은 변경하지 않습니다.	Flip display only. The source file is unchanged.
LumaView 영상	LumaView Video
LumaView 시퀀스	LumaView Sequence
목록으로 (Esc)	Back to list (Esc)
◀ 이전 영상	◀ Previous video
다음 영상 ▶	Next video ▶
Ⅱ 일시정지	Ⅱ Pause
▶ 재생	▶ Play
▶ 다시 재생	▶ Replay
−5초	−5 sec
+5초	+5 sec
처음으로	Start
◀ 프레임 (D)	◀ Frame (D)
프레임 ▶ (F)	Frame ▶ (F)
음소거	Mute
볼륨	Volume
불러오는 중…	Loading…
불러오는 중…  	Loading…  
시퀀스: 	Sequence: 
\n프레임: 	\nFrame: 
 fps\n미리보기: 	 fps\nPreview: 
프레임 읽기 실패: 	Frame error: 
재생 불가	Playback unavailable
재생할 수 없는 영상: 	Cannot play: 
파일 정보를 읽을 수 없습니다.	Cannot read file information.
 (정보 없음: 30 fps 기준)	 (unknown: using 30 fps)
미리보기 없음	No preview
접근할 수 없음	Access denied
폴더를 읽을 수 없음	Cannot read folder
폴더를 열 수 없습니다	Cannot open folder
이미지를 열 수 없습니다	Cannot open image
이미지를 읽는 중…	Loading image…
 프레임]	 frames]
\nSQ 시퀀스 · 	\nSQ sequence · 
 프레임	 frames
개 파일  ·  더블클릭: 전체화면  ·  	 items  ·  Double-click: fullscreen  ·  
확인 불가	Unavailable
슬라이드쇼 재생 중	Slideshow playing
준비  ·  원본 파일을 변경하지 않습니다	Ready  ·  Source files are preserved
이미지가 있는 폴더 선택	Select an image folder
파일을 찾을 수 없습니다.	File not found.
사용할 수 없는 파일 이름입니다.	Invalid file name.
같은 이름의 파일이 이미 있습니다. 다른 이름을 입력해 주세요.	A file with that name already exists. Enter a different name.
이름을 변경할 수 없습니다	Cannot rename
복사할 수 없습니다	Cannot copy
시퀀스의 개별 이름을 바꾸려면 SQ 묶기를 해제해 주세요.	Disable Group SQ to rename individual frames.
복사한 파일이 없습니다. 폴더 붙여넣기는 지원하지 않습니다.	No copied files. Pasting folders is not supported.
파일을 복사하는 중…	Copying files…
일부 파일을 복사하지 못했습니다	Some files could not be copied
붙여넣기 실패	Paste failed
설정을 저장하지 못했습니다	Could not save settings
선택한 설정은 바로 적용되며 다음 실행에도 유지됩니다.	Settings apply immediately and are kept for the next launch.
파일 이름: {0}  [{1}/{2}]\n파일 크기: {3}\n수정 날짜: {4}\n이미지 정보: {5} × {6} px  ({7})\n화면 배율: {8:0}%	File: {0}  [{1}/{2}]\nSize: {3}\nModified: {4}\nImage: {5} × {6} px  ({7})\nZoom: {8:0}%
파일: {0}  [{1}/{2}]\n크기: {3:N2} MB  ·  수정: {4:yyyy-MM-dd HH:mm:ss}  ·  영상: {5} × {6}  ·  길이: {7}	File: {0}  [{1}/{2}]\nSize: {3:N2} MB  ·  Modified: {4:yyyy-MM-dd HH:mm:ss}  ·  Video: {5} × {6}  ·  Duration: {7}";
}
sealed class CustomColorEditor:UserControl {
    public Color[] Colors=(Color[])Ui.CustomColors.Clone();
    readonly Button[] swatches=new Button[5];
    readonly Preview preview;
    public CustomColorEditor(){
        string[] names={"UI 배경","메뉴 / 버튼","글자","선택 강조","테두리"};
        for(int i=0;i<5;i++){
            int index=i;
            Controls.Add(new Label{Left=0,Top=i*38+7,Width=125,Height=25,Text=Ui.T(names[i])});
            var button=new Button{Left=130,Top=i*38,Width=112,Height=30,FlatStyle=FlatStyle.Flat,AccessibleName=Ui.T(names[i])+" — "+Ui.T("색상 선택")};
            button.Click+=delegate{using(var dialog=new ColorDialog{Color=Colors[index],FullOpen=true,AnyColor=true})if(dialog.ShowDialog(this)==DialogResult.OK){Colors[index]=dialog.Color;RefreshSwatches();}};
            swatches[i]=button;Controls.Add(button);
        }
        var reset=new Button{Left=0,Top=202,Width=242,Height=30,Text=Ui.T("기본 색으로"),FlatStyle=FlatStyle.Flat};reset.Click+=delegate{Colors=Ui.DefaultColors();RefreshSwatches();};Controls.Add(reset);
        preview=new Preview(this){Left=265,Top=0,Width=265,Height=232};Controls.Add(preview);RefreshSwatches();
    }
    public void RefreshSwatches(){
        BackColor=Ui.Surface;ForeColor=Ui.Foreground;
        for(int i=0;i<swatches.Length;i++){var button=swatches[i];if(button==null)continue;button.BackColor=Colors[i];button.ForeColor=Colors[i].GetBrightness()>.55?Color.Black:Color.White;button.Text=Ui.Hex(Colors[i]);button.FlatAppearance.BorderColor=Ui.Border;}
        if(preview!=null)preview.Invalidate();
    }
    sealed class Preview:Control {
        readonly CustomColorEditor editor;
        public Preview(CustomColorEditor editor){this.editor=editor;DoubleBuffered=true;}
        protected override void OnPaint(PaintEventArgs e){
            var c=editor.Colors;e.Graphics.Clear(c[0]);
            using(var surface=new SolidBrush(c[1]))using(var text=new SolidBrush(c[2]))using(var selected=new SolidBrush(c[3]))using(var border=new Pen(c[4])){
                e.Graphics.FillRectangle(surface,0,0,Width,42);e.Graphics.DrawLine(border,0,42,Width,42);
                e.Graphics.DrawString(Ui.T("미리보기"),Font,text,12,13);
                e.Graphics.FillRectangle(surface,12,64,Width-24,38);e.Graphics.DrawRectangle(border,12,64,Width-24,38);
                e.Graphics.DrawString(Ui.T("메뉴 / 버튼"),Font,text,22,75);
                e.Graphics.DrawString("LumaView  ·  image.png",Font,text,16,123);
                e.Graphics.FillRectangle(selected,12,158,Width-24,38);e.Graphics.DrawString(Ui.T("선택 강조"),Font,text,22,169);
                e.Graphics.DrawRectangle(border,0,0,Width-1,Height-1);
            }
        }
    }
}
sealed class SettingsDialog:Form {
    public SettingsDialog(){
        Text=AppUpdater.WindowTitle(Ui.T("설정"));Font=new Font("Malgun Gothic",10);ClientSize=new Size(600,530);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;StartPosition=FormStartPosition.CenterParent;
        var language=new ComboBox{Left=135,Top=30,Width=275,DropDownStyle=ComboBoxStyle.DropDownList};language.Items.AddRange(new object[]{"한국어","English"});language.SelectedIndex=Ui.Language=="en"?1:0;
        var theme=new ComboBox{Left=135,Top=85,Width=275,DropDownStyle=ComboBoxStyle.DropDownList};theme.Items.AddRange(new object[]{Ui.T("어둡게"),Ui.T("밝게"),Ui.T("커스텀")});theme.SelectedIndex=Ui.Theme=="light"?1:Ui.Theme=="custom"?2:0;
        var colors=new CustomColorEditor{Left=24,Top=140,Width=540,Height=250};
        colors.Enabled=theme.SelectedIndex==2;theme.SelectedIndexChanged+=delegate{colors.Enabled=theme.SelectedIndex==2;};
        var note=new Label{Left=24,Top=410,Width=390,Height=48,Text=Ui.T("선택한 설정은 바로 적용되며 다음 실행에도 유지됩니다.")};
        var save=new Button{Left=224,Top=205,Width=90,Height=30,Text=Ui.T("저장")};var cancel=new Button{Left=324,Top=205,Width=90,Height=30,Text=Ui.T("취소"),DialogResult=DialogResult.Cancel};
        Controls.AddRange(new Control[]{new Label{Left=24,Top=34,Width=100,Text=Ui.T("언어")},new Label{Left=24,Top=89,Width=100,Text=Ui.T("테마")},language,theme,colors,note,save,cancel});AcceptButton=save;CancelButton=cancel;
        save.Click+=delegate{try{Ui.SavePalette(language.SelectedIndex==1?"en":"ko",new[]{"dark","light","custom"}[theme.SelectedIndex],colors.Colors);DialogResult=DialogResult.OK;}catch(Exception ex){MessageBox.Show(this,ex.Message,Ui.T("설정을 저장하지 못했습니다"));}};
        var tabs=new TabControl{Dock=DockStyle.Fill};var general=new TabPage(Ui.T("일반"));var associations=new TabPage(Ui.T("파일 연결"));var components=new TabPage(ComponentSetup.Say("구성 요소","Components"));
        var existing=new System.Collections.Generic.List<Control>();foreach(Control child in Controls)existing.Add(child);
        foreach(Control child in existing)if(child!=save&&child!=cancel)general.Controls.Add(child);
        note.Width=510;
        var footer=new Panel{Dock=DockStyle.Bottom,Height=50};save.Location=new Point(390,10);cancel.Location=new Point(490,10);footer.Controls.Add(save);footer.Controls.Add(cancel);
        var associationSettings=new AssociationSettings();
        tabs.TabPages.Add(general);tabs.TabPages.Add(components);tabs.TabPages.Add(associations);associations.Controls.Add(associationSettings);components.Controls.Add(new ComponentSettings(()=>associationSettings.SelectedExtensions));Controls.Add(tabs);Controls.Add(footer);
        Ui.Apply(this);colors.RefreshSwatches();
    }
}
partial class Viewer {
    void ShowSettings(){using(var dialog=new SettingsDialog())if(dialog.ShowDialog(this)==DialogResult.OK){
        foreach(Form form in Application.OpenForms)Ui.Apply(form);Ui.Apply(toolbar);Ui.Apply(canvas.ContextMenuStrip);
        foreach(TreeNode node in folders.Nodes)node.Text=Ui.T(node.Text);
        string selected=FileCommandPath();if(!full)RefreshFileSelection(selected);UpdateInfo();
    }}
}
}






