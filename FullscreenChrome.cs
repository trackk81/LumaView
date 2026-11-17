using System;
using System.Drawing;
using System.Windows.Forms;

namespace LumaView {
// Owned windows keep controls above the WPF video surface without resizing it.
sealed class FullscreenChrome : IDisposable {
    sealed class Overlay : Form {
        internal Func<Keys,bool> Shortcut;
        public Overlay(){FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.None;BackColor=Color.FromArgb(28,31,37);Opacity=0;}
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override bool ProcessCmdKey(ref Message msg,Keys key){return (Shortcut!=null&&Shortcut(key))||base.ProcessCmdKey(ref msg,key);}
    }
    readonly Form owner;
    readonly Overlay top=new Overlay(),bottom=new Overlay();
    readonly ToolStrip toolbar;
    readonly Control footer;
    readonly Timer timer=new Timer{Interval=30};
    readonly ToolStripButton pin=new ToolStripButton(Ui.T("고정")){CheckOnClick=true,Alignment=ToolStripItemAlignment.Right,DisplayStyle=ToolStripItemDisplayStyle.Image,Overflow=ToolStripItemOverflow.Never,AutoSize=false,Width=48,Height=40,ImageScaling=ToolStripItemImageScaling.None,ToolTipText=Ui.T("메뉴와 재생바를 항상 표시합니다")};
    readonly Button close=new Button{Size=new Size(46,32),FlatStyle=FlatStyle.Flat,TabStop=false,Anchor=AnchorStyles.Top|AnchorStyles.Right,AccessibleName="LumaView 종료 / Exit"};
    readonly Bitmap pinOff=DrawIcon(false,false),pinOn=DrawIcon(true,false),closeIcon=DrawIcon(false,true);
    DateTime topUntil,bottomUntil;
    bool enabled,disposed;
    public FullscreenChrome(Form owner,ToolStrip toolbar,Control footer,Func<Keys,bool> shortcut){
        this.owner=owner;this.toolbar=toolbar;this.footer=footer;
        top.Font=bottom.Font=owner.Font;top.Shortcut=bottom.Shortcut=shortcut;
        toolbar.Dock=DockStyle.Top;toolbar.BackColor=top.BackColor;toolbar.ForeColor=Color.FromArgb(235,238,243);
        toolbar.Renderer=new ToolStripProfessionalRenderer(new ChromeColors());
        pin.Image=pinOff;close.Image=closeIcon;
        pin.AccessibleName=Ui.T("고정");close.AccessibleName="LumaView 종료 / Exit";
        top.Padding=new Padding(0,32,0,0);
        toolbar.Items.Add(pin);top.Controls.Add(toolbar);top.Controls.Add(close);
        top.SizeChanged+=delegate{close.Location=new Point(top.ClientSize.Width-close.Width,0);};
        close.Click+=delegate{
            Form root=owner;while(root.Owner!=null)root=root.Owner;
            if(root!=owner){var main=root;main.BeginInvoke((Action)delegate{if(!main.IsDisposed)main.Close();});}
            owner.Close();
        };
        if(footer!=null){footer.Dock=DockStyle.Fill;bottom.Controls.Add(footer);}
        pin.CheckedChanged+=delegate{pin.Image=pin.Checked?pinOn:pinOff;pin.AccessibleName=pin.Checked?Ui.T("고정됨"):Ui.T("고정");Reveal();owner.Activate();};
        Ui.Apply(top);Ui.Apply(bottom);StyleClose();
        timer.Tick+=delegate{Tick();};
    }
    void StyleClose(){close.FlatAppearance.BorderSize=0;close.FlatAppearance.MouseOverBackColor=Color.FromArgb(232,17,35);close.FlatAppearance.MouseDownBackColor=Color.FromArgb(180,12,26);close.BackColor=top.BackColor;close.BringToFront();}
    public void AddTop(Control control){top.Controls.Add(control);control.Dock=DockStyle.Top;toolbar.BringToFront();Ui.Apply(top);StyleClose();}
    public void Enable(){enabled=true;toolbar.Visible=true;Reveal();timer.Start();Tick();}
    public void Disable(){enabled=false;timer.Stop();top.Hide();bottom.Hide();}
    public void Reveal(){topUntil=bottomUntil=DateTime.UtcNow.AddSeconds(2.2);}
    void Tick(){
        if(!enabled||owner.IsDisposed)return;
        Form active=Form.ActiveForm;
        bool foreground=owner.Enabled&&(active==owner||active==top||active==bottom);
        if(!foreground||owner.WindowState==FormWindowState.Minimized){top.Hide();bottom.Hide();return;}
        Rectangle area=owner.RectangleToScreen(owner.ClientRectangle);
        int height=top.Padding.Top;foreach(Control c in top.Controls)if(c.Visible&&c.Dock==DockStyle.Top)height+=c.Height;
        height=Math.Max(toolbar.Height,height);
        var topBounds=new Rectangle(area.X,area.Y,area.Width,height);if(top.Bounds!=topBounds)top.Bounds=topBounds;
        var bottomBounds=new Rectangle(area.X,area.Bottom-56,area.Width,56);if(bottom.Bounds!=bottomBounds)bottom.Bounds=bottomBounds;
        Point mouse=Cursor.Position;DateTime now=DateTime.UtcNow;
        bool inOwner=area.Contains(mouse);
        bool overTop=inOwner&&(mouse.Y<area.Top+22||(top.Visible&&top.Bounds.Contains(mouse)));
        bool overBottom=inOwner&&(mouse.Y>=area.Bottom-22||(bottom.Visible&&bottom.Bounds.Contains(mouse)));
        bool interacting=Control.MouseButtons!=MouseButtons.None||DropDownOpen();
        if(overTop||(top.ContainsFocus&&interacting))topUntil=now.AddSeconds(1.5);
        if(overBottom||(bottom.ContainsFocus&&interacting))bottomUntil=now.AddSeconds(1.5);
        Fade(top,pin.Checked||now<topUntil||DropDownOpen());
        if(footer!=null)Fade(bottom,pin.Checked||now<bottomUntil);
    }
    bool DropDownOpen(){foreach(ToolStripItem item in toolbar.Items){var drop=item as ToolStripDropDownItem;if(drop!=null&&drop.DropDown.Visible)return true;var combo=item as ToolStripComboBox;if(combo!=null&&combo.DroppedDown)return true;}return toolbar.OverflowButton.DropDown.Visible;}
    void Fade(Overlay bar,bool show){
        if(show&&!bar.Visible){bar.Opacity=0;bar.Show(owner);}
        if(!bar.Visible)return;
        double opacity=Math.Max(0,Math.Min(.96,bar.Opacity+(show?.16:-.16)));if(Math.Abs(bar.Opacity-opacity)>.001)bar.Opacity=opacity;
        if(!show&&bar.Opacity<=0){bool focused=bar.ContainsFocus;bar.Hide();if(focused)owner.Activate();}
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

