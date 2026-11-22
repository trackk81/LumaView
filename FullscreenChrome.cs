using System;
using System.Drawing;
using System.Windows.Forms;

namespace LumaView {
class MediaViewerForm : Form {
    internal bool ExitApplicationRequested {get;private set;}
    internal void ExitApplication(){
        ExitApplicationRequested=true;
        Close();
    }
    protected override void WndProc(ref Message message){
        const int SystemCommand=0x0112,CloseCommand=0xf060;
        if(message.Msg==SystemCommand&&(message.WParam.ToInt64()&0xfff0)==CloseCommand){
            ExitApplication();
            return;
        }
        base.WndProc(ref message);
    }
}
// .NET Framework ToolStrip normally consumes the click that activates its window.
// Our toolbar lives in a separate owned window, so this must be allowed through
// on every return from the image/WebView/video surface, not only on application entry.
sealed class ViewerToolStrip : ToolStrip {
    protected override void WndProc(ref Message message){
        base.WndProc(ref message);
        const int WM_MOUSEACTIVATE=0x21,MA_ACTIVATEANDEAT=2,MA_ACTIVATE=1;
        if(message.Msg==WM_MOUSEACTIVATE&&message.Result==(IntPtr)MA_ACTIVATEANDEAT)message.Result=(IntPtr)MA_ACTIVATE;
    }
}
// Owned windows keep controls above the WPF video surface without resizing it.
sealed class FullscreenChrome : IDisposable {
    internal const int FooterHeight=56;
    sealed class Overlay : Form {
        internal Func<Keys,bool> Shortcut;
        public Overlay(){FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.None;BackColor=Color.FromArgb(28,31,37);}
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override bool ProcessCmdKey(ref Message msg,Keys key){return (Shortcut!=null&&Shortcut(key))||base.ProcessCmdKey(ref msg,key);}
    }
    readonly Form owner;
    readonly Overlay top=new Overlay(),bottom=new Overlay();
    readonly ToolStrip toolbar;
    readonly Control footer;
    readonly int dockedFooterHeight;
    readonly Timer timer=new Timer{Interval=30};
    readonly ToolStripButton pin=new ToolStripButton(Ui.T("고정")){CheckOnClick=true,Alignment=ToolStripItemAlignment.Right,DisplayStyle=ToolStripItemDisplayStyle.Image,Overflow=ToolStripItemOverflow.Never,AutoSize=false,Width=48,Height=40,ImageScaling=ToolStripItemImageScaling.None,ToolTipText=Ui.T("메뉴와 재생바를 항상 표시합니다")};
    readonly Button close=new Button{Size=new Size(46,32),FlatStyle=FlatStyle.Flat,TabStop=false,Anchor=AnchorStyles.Top|AnchorStyles.Right,AccessibleName="DollView 종료 / Exit"};
    readonly Label versionLabel=new Label{Text=AppUpdater.AppTitle,Location=new Point(12,6),Height=22,AutoEllipsis=true};
    readonly Bitmap pinOff=DrawIcon(false,false),pinOn=DrawIcon(true,false),closeIcon=DrawIcon(false,true);
    DateTime topUntil,bottomUntil;
    bool enabled,disposed;
    internal event Action<int> TopInsetChanged;
    void ReportTopInset(){
        var handler=TopInsetChanged;
        if(handler!=null)handler(top.Visible?top.Height:0);
    }
    public FullscreenChrome(Form owner,ToolStrip toolbar,Control footer,Func<Keys,bool> shortcut){
        this.owner=owner;this.toolbar=toolbar;this.footer=footer;
        dockedFooterHeight=footer==null?0:footer.Height;
        top.Font=bottom.Font=owner.Font;top.Shortcut=bottom.Shortcut=shortcut;
        toolbar.Dock=DockStyle.Top;toolbar.BackColor=top.BackColor;toolbar.ForeColor=Color.FromArgb(235,238,243);
        toolbar.Renderer=new ToolStripProfessionalRenderer(new ChromeColors());
        pin.Image=pinOff;close.Image=closeIcon;
        pin.AccessibleName=Ui.T("고정");close.AccessibleName="DollView 종료 / Exit";
        top.Padding=new Padding(0,32,0,0);
        toolbar.Items.Add(pin);top.Controls.Add(toolbar);top.Controls.Add(close);top.Controls.Add(versionLabel);
        top.SizeChanged+=delegate{close.Location=new Point(top.ClientSize.Width-close.Width,0);versionLabel.Width=Math.Max(1,top.ClientSize.Width-close.Width-24);};
        top.VisibleChanged+=delegate{ReportTopInset();};
        top.SizeChanged+=delegate{ReportTopInset();};
        close.Click+=delegate{
            var media=owner as MediaViewerForm;
            if(media!=null)media.ExitApplication();
            else owner.Close();
        };
        if(footer!=null){footer.Dock=DockStyle.Fill;bottom.Controls.Add(footer);}
        pin.CheckedChanged+=delegate{pin.Image=pin.Checked?pinOn:pinOff;pin.AccessibleName=pin.Checked?Ui.T("고정됨"):Ui.T("고정");Reveal();owner.Activate();};
        Ui.Apply(top);Ui.Apply(bottom);StyleClose();
        timer.Tick+=delegate{Tick();};
    }
    void StyleClose(){close.FlatAppearance.BorderSize=0;close.FlatAppearance.MouseOverBackColor=Color.FromArgb(232,17,35);close.FlatAppearance.MouseDownBackColor=Color.FromArgb(180,12,26);close.BackColor=top.BackColor;close.BringToFront();}
    public void AddTop(Control control){top.Controls.Add(control);control.Dock=DockStyle.Top;toolbar.BringToFront();Ui.Apply(top);StyleClose();}
    public void Enable(){
        if(toolbar.Parent!=top){top.Controls.Add(toolbar);toolbar.BringToFront();}
        if(footer!=null&&footer.Parent!=bottom){bottom.Controls.Add(footer);footer.Dock=DockStyle.Fill;}
        pin.Available=true;enabled=true;toolbar.Visible=true;Reveal();timer.Start();Tick();
    }
    internal void DockToolbarInOwner(){
        Disable();
        pin.Available=false;
        owner.Controls.Add(toolbar);
        toolbar.BringToFront();
        if(footer!=null){owner.Controls.Add(footer);footer.Dock=DockStyle.Bottom;footer.Height=dockedFooterHeight;footer.BringToFront();}
    }
    public void Disable(){enabled=false;timer.Stop();top.Hide();bottom.Hide();}
    public void Reveal(){topUntil=bottomUntil=DateTime.UtcNow.AddSeconds(2.2);}
    void Tick(){
        if(!enabled||owner.IsDisposed)return;
        Form active=Form.ActiveForm;
        bool foreground=owner.Enabled&&(active==owner||active==top||active==bottom);
        if(!foreground||owner.WindowState==FormWindowState.Minimized){top.Hide();bottom.Hide();return;}
        Rectangle area=owner.RectangleToScreen(owner.ClientRectangle);
        // Control.Visible also becomes false when its parent is hidden. Keep the
        // title/toolbar row heights stable while hidden so reveal and hit areas agree.
        int height=top.Padding.Top;foreach(Control c in top.Controls)if(c.Dock==DockStyle.Top)height+=c.Height;
        height=Math.Max(toolbar.Height,height);
        var topBounds=new Rectangle(area.X,area.Y,area.Width,height);if(top.Bounds!=topBounds)top.Bounds=topBounds;
        var bottomBounds=new Rectangle(area.X,area.Bottom-FooterHeight,area.Width,FooterHeight);if(bottom.Bounds!=bottomBounds)bottom.Bounds=bottomBounds;
        Point mouse=Cursor.Position;DateTime now=DateTime.UtcNow;
        bool inOwner=area.Contains(mouse);
        // Reveal over the actual controls, rather than a hard-to-find 22px strip.
        // A small margin keeps the bar stable at its lower/upper edge.
        bool overTop=inOwner&&mouse.Y<topBounds.Bottom+(top.Visible?12:0);
        bool overBottom=inOwner&&mouse.Y>=bottomBounds.Top-(bottom.Visible?12:0);
        bool interacting=Control.MouseButtons!=MouseButtons.None||DropDownOpen();
        if(overTop||(top.ContainsFocus&&interacting)||EditorFocused(top))topUntil=now.AddSeconds(2.2);
        if(overBottom||(bottom.ContainsFocus&&interacting)||EditorFocused(bottom))bottomUntil=now.AddSeconds(2.2);
        ShowBar(top,pin.Checked||now<topUntil||DropDownOpen());
        if(footer!=null)ShowBar(bottom,pin.Checked||now<bottomUntil);
    }
    bool DropDownOpen(){foreach(ToolStripItem item in toolbar.Items){var drop=item as ToolStripDropDownItem;if(drop!=null&&drop.DropDown.Visible)return true;var combo=item as ToolStripComboBox;if(combo!=null&&combo.DroppedDown)return true;}return toolbar.OverflowButton.DropDown.Visible;}
    static bool EditorFocused(Control root){
        if(!root.ContainsFocus)return false;
        if(root is TextBoxBase||root is UpDownBase||root is ComboBox||root is TrackBar)return true;
        foreach(Control child in root.Controls)if(EditorFocused(child))return true;
        return false;
    }
    void ShowBar(Overlay bar,bool show){
        // No transparent, still-clickable transition window between hidden/shown.
        if(show){if(!bar.Visible)bar.Show(owner);return;}
        if(bar.Visible){bool focused=bar.ContainsFocus;bar.Hide();if(focused)owner.Activate();}
    }
    static Bitmap DrawIcon(bool active,bool cross){
        int size=cross?24:32;var image=new Bitmap(size,size);using(var g=Graphics.FromImage(image)){
            g.ScaleTransform(size/24f,size/24f);
            g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using(var pen=new Pen(active?Color.FromArgb(104,220,235):Color.FromArgb(235,238,243),2)){
                if(cross){g.DrawLine(pen,6,6,18,18);g.DrawLine(pen,18,6,6,18);}
                else{
                    if(!active){g.TranslateTransform(12,12);g.RotateTransform(35);g.TranslateTransform(-12,-12);}
                    g.DrawLines(pen,new[]{new Point(7,4),new Point(17,4),new Point(15,7),new Point(15,12),new Point(18,15),new Point(6,15),new Point(9,12),new Point(9,7),new Point(7,4)});
                    g.DrawLine(pen,12,15,12,22);
                }
            }
        }return image;
    }
    public void Dispose(){if(disposed)return;disposed=true;Disable();timer.Dispose();top.Controls.Remove(toolbar);toolbar.Items.Remove(pin);top.Controls.Remove(close);pin.Dispose();close.Dispose();pinOff.Dispose();pinOn.Dispose();closeIcon.Dispose();if(footer!=null)bottom.Controls.Remove(footer);top.Dispose();bottom.Dispose();}
    sealed class ChromeColors : ProfessionalColorTable {
        public override Color ToolStripGradientBegin{get{return Color.FromArgb(28,31,37);}}
        public override Color ToolStripGradientMiddle{get{return ToolStripGradientBegin;}}
        public override Color ToolStripGradientEnd{get{return ToolStripGradientBegin;}}
        public override Color ButtonSelectedHighlight{get{return Color.FromArgb(61,74,87);}}
        public override Color ButtonSelectedGradientBegin{get{return ButtonSelectedHighlight;}}
        public override Color ButtonSelectedGradientEnd{get{return ButtonSelectedHighlight;}}
        public override Color ButtonCheckedGradientBegin{get{return Color.FromArgb(43,91,104);}}
        public override Color ButtonCheckedGradientEnd{get{return ButtonCheckedGradientBegin;}}
        public override Color ButtonCheckedGradientMiddle{get{return ButtonCheckedGradientBegin;}}
    }
}
}

