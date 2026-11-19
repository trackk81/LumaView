using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LumaView {
static class ModernUi {
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Form,object> captions=new System.Runtime.CompilerServices.ConditionalWeakTable<Form,object>();
    internal static Color Muted {get{return Ui.Theme=="light"?Color.FromArgb(94,106,124):Ui.Theme=="custom"?Blend(Ui.Foreground,Ui.Surface,.68f):Color.FromArgb(153,165,184);}}
    internal static Color Accent {get{return Ui.Theme=="custom"?Ui.Highlight:Color.FromArgb(70,131,235);}}
    internal static Color Blend(Color a,Color b,float amount){return Color.FromArgb((int)(a.R*amount+b.R*(1-amount)),(int)(a.G*amount+b.G*(1-amount)),(int)(a.B*amount+b.B*(1-amount)));}
    internal static GraphicsPath Rounded(RectangleF r,float radius){
        float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));var path=new GraphicsPath();
        if(d<=0){path.AddRectangle(r);return path;}
        path.AddArc(r.X,r.Y,d,d,180,90);path.AddArc(r.Right-d,r.Y,d,d,270,90);path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.X,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;
    }
    internal static WrapLabel Text(string text,bool muted=false,float size=10,bool bold=false){return new WrapLabel{Text=text,Muted=muted,Font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular),Margin=new Padding(0,0,0,8)};}
    internal static FlowLayoutPanel Actions(params Control[] controls){var row=new FlowLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=true,Margin=new Padding(0,8,0,0),Padding=Padding.Empty};row.Controls.AddRange(controls);return row;}
    internal static Panel Scroll(Control content){var panel=new Panel{Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(24,20,24,20)};content.Dock=DockStyle.Top;panel.Controls.Add(content);return panel;}
    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr w,IntPtr l);
    internal static void GallerySpacing(ListView list){if(!list.IsHandleCreated)return;int width=192,height=148+list.Font.Height*3;SendMessage(list.Handle,0x1035,IntPtr.Zero,(IntPtr)((height<<16)|width));}
    internal static void TitleBar(Form form){if(!form.IsHandleCreated)return;try{int dark=Ui.Theme=="light"?0:1;DwmSetWindowAttribute(form.Handle,20,ref dark,4);}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}}
    internal static void Style(Control control){
        var label=control as WrapLabel;if(label!=null&&label.Muted)label.ForeColor=Muted;
        var card=control as ModernCard;if(card!=null)card.BackColor=Ui.Surface;
        var form=control as Form;if(form!=null){object marker;if(!captions.TryGetValue(form,out marker)){captions.Add(form,new object());form.HandleCreated+=delegate{TitleBar(form);};}TitleBar(form);}
    }
}

// Width belongs to the layout; height is measured from the entire text.
// No ellipsis is used for instructions, errors or status messages.
class WrapLabel:Label {
    public bool Muted;
    public WrapLabel(){AutoSize=false;UseMnemonic=false;AutoEllipsis=false;Margin=new Padding(0,0,0,8);}
    public override Size GetPreferredSize(Size proposed){
        int width=proposed.Width>1?proposed.Width:Width;
        if(width<=20){
            var single=TextRenderer.MeasureText(Text??"",Font,Size.Empty,TextFormatFlags.NoPrefix);
            return new Size(Math.Max(20,single.Width+Padding.Horizontal),Math.Max(Font.Height,single.Height)+Padding.Vertical+2);
        }
        var measured=TextRenderer.MeasureText(Text??"",Font,new Size(Math.Max(1,width-Padding.Horizontal),int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl|TextFormatFlags.NoPrefix);
        return new Size(width,Math.Max(Font.Height,measured.Height)+Padding.Vertical+2);
    }
    protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);if(Parent!=null)Parent.PerformLayout();}
}

// Native controls keep keyboard/accessibility behavior. This stack assigns
// their full measured heights and lets the enclosing page scroll vertically.
class SettingsStack:Panel {
    bool layingOut;
    public SettingsStack(){DoubleBuffered=true;Margin=new Padding(0,0,0,16);}
    int Measure(int width,bool arrange){
        int y=Padding.Top;int content=Math.Max(1,width-Padding.Horizontal);
        foreach(Control child in Controls){
            if(Visible&&!child.Visible)continue;
            int available=Math.Max(1,content-child.Margin.Horizontal);
            int height=Math.Max(child.MinimumSize.Height,child.GetPreferredSize(new Size(available,0)).Height);
            y+=child.Margin.Top;
            if(arrange)child.SetBounds(Padding.Left+child.Margin.Left,y,available,height);
            y+=height+child.Margin.Bottom;
        }
        return y+Padding.Bottom;
    }
    public override Size GetPreferredSize(Size proposed){int width=proposed.Width>0?proposed.Width:Math.Max(100,Width);return new Size(width,Measure(width,false));}
    protected override void OnLayout(LayoutEventArgs e){
        if(layingOut)return;layingOut=true;
        try{base.OnLayout(e);int desired=Measure(ClientSize.Width,true);if(Height!=desired)Height=desired;}finally{layingOut=false;}
    }
    protected override void OnControlAdded(ControlEventArgs e){base.OnControlAdded(e);e.Control.VisibleChanged+=ChildChanged;e.Control.TextChanged+=ChildChanged;e.Control.FontChanged+=ChildChanged;PerformLayout();}
    void ChildChanged(object sender,EventArgs e){PerformLayout();}
    protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);PerformLayout();}
}
sealed class ModernCard:SettingsStack {
    public ModernCard(){Padding=new Padding(20);Margin=new Padding(0,0,0,16);}
    protected override void OnPaintBackground(PaintEventArgs e){
        e.Graphics.Clear(Parent==null?Ui.Background:Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var path=ModernUi.Rounded(new RectangleF(.5f,.5f,Math.Max(1,Width-1),Math.Max(1,Height-1)),10))
        using(var fill=new SolidBrush(Ui.Surface))using(var border=new Pen(Ui.Border)){e.Graphics.FillPath(fill,path);e.Graphics.DrawPath(border,path);}
    }
}
sealed class SettingsRow:Panel {
    readonly WrapLabel label;
    readonly Control action;
    public SettingsRow(WrapLabel label,Control action){
        this.label=label;this.action=action;
        DoubleBuffered=true;Margin=new Padding(0,0,0,8);
        label.Margin=Padding.Empty;label.TextAlign=ContentAlignment.MiddleLeft;
        action.Margin=Padding.Empty;
        Controls.Add(label);Controls.Add(action);
        action.TextChanged+=RefreshRow;label.TextChanged+=RefreshRow;
        action.FontChanged+=RefreshRow;label.FontChanged+=RefreshRow;
    }
    void RefreshRow(object sender,EventArgs e){PerformLayout();if(Parent!=null)Parent.PerformLayout();}
    void Compute(int width,out int labelW,out int labelH,out int actionW,out int actionH){
        var actionSize=action.GetPreferredSize(Size.Empty);
        actionW=Math.Max(action.MinimumSize.Width,actionSize.Width);
        actionH=Math.Max(action.MinimumSize.Height,actionSize.Height);
        labelW=Math.Max(40,width-actionW-16);
        labelH=label.GetPreferredSize(new Size(labelW,0)).Height;
    }
    public override Size GetPreferredSize(Size proposed){
        int width=proposed.Width>0?proposed.Width:Math.Max(100,Width);
        int labelW,labelH,actionW,actionH;Compute(width,out labelW,out labelH,out actionW,out actionH);
        return new Size(width,Math.Max(actionH,labelH));
    }
    protected override void OnLayout(LayoutEventArgs e){
        base.OnLayout(e);
        int width=Math.Max(1,ClientSize.Width);
        int labelW,labelH,actionW,actionH;Compute(width,out labelW,out labelH,out actionW,out actionH);
        int rowH=Math.Max(actionH,labelH);
        label.SetBounds(0,(rowH-labelH)/2,labelW,labelH);
        action.SetBounds(Math.Max(0,width-actionW),(rowH-actionH)/2,actionW,actionH);
    }
}
class ModernButton:Button {
    public bool Primary,Selected;
    public bool AlignLeft,Chevron;
    public Color? SwatchColor;
    bool hovering,pressed;
    public ModernButton(){AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;MinimumSize=new Size(84,38);Padding=new Padding(16,8,16,8);Margin=new Padding(0,0,8,0);FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;UseVisualStyleBackColor=false;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
    public override Size GetPreferredSize(Size proposed){var size=TextRenderer.MeasureText(Text??"",Font,Size.Empty,TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);return new Size(Math.Max(MinimumSize.Width,size.Width+Padding.Horizontal+4),Math.Max(MinimumSize.Height,size.Height+Padding.Vertical+4));}
    protected override void OnMouseEnter(EventArgs e){hovering=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hovering=false;pressed=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){pressed=true;Invalidate();base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
    protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e){
        e.Graphics.Clear(Parent==null?Ui.Surface:Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        Color fill=SwatchColor??(Primary?ModernUi.Accent:Selected?Ui.Highlight:ModernUi.Blend(Ui.Foreground,Ui.Surface,.045f));
        Color text=SwatchColor.HasValue||Primary?(fill.GetBrightness()>.55?Color.Black:Color.White):Ui.Foreground;
        if(hovering)fill=ModernUi.Blend(Ui.Foreground,fill,.08f);if(pressed)fill=ModernUi.Blend(Ui.Background,fill,.22f);
        if(!Enabled){fill=ModernUi.Blend(fill,Ui.Surface,.45f);text=ModernUi.Muted;}
        using(var path=ModernUi.Rounded(new RectangleF(1,1,Math.Max(1,Width-2),Math.Max(1,Height-2)),7))
        using(var brush=new SolidBrush(fill))using(var border=new Pen(Focused?ModernUi.Accent:Primary?fill:Ui.Border,Focused?2:1)){e.Graphics.FillPath(brush,path);e.Graphics.DrawPath(border,path);}
        var bounds=AlignLeft?new Rectangle(Padding.Left,0,Math.Max(1,Width-Padding.Horizontal-(Chevron?24:0)),Height):ClientRectangle;
        TextRenderer.DrawText(e.Graphics,Text,Font,bounds,text,(AlignLeft?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter)|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
        if(Chevron)using(var pen=new Pen(text,1.5f)){float x=Width-23,y=Height/2f;e.Graphics.DrawLines(pen,new[]{new PointF(x-4,y-2),new PointF(x,y+2),new PointF(x+4,y-2)});}
    }
}
sealed class ModernCombo:ModernButton {
    public readonly System.Collections.ArrayList Items=new System.Collections.ArrayList();
    ContextMenuStrip choices;
    int selectedIndex=-1;
    public event EventHandler SelectedIndexChanged;
    public int SelectedIndex {get{return selectedIndex;}set{if(value<0||value>=Items.Count)throw new ArgumentOutOfRangeException("value");if(selectedIndex==value)return;selectedIndex=value;Text=Convert.ToString(Items[value]);if(SelectedIndexChanged!=null)SelectedIndexChanged(this,EventArgs.Empty);}}
    public ModernCombo(){AlignLeft=Chevron=true;MinimumSize=new Size(100,40);Margin=new Padding(0,0,0,18);AccessibleRole=AccessibleRole.ComboBox;Click+=delegate{ShowChoices();};}
    void ShowChoices(){
        // WinForms still accesses a dropdown after raising Closed. Keep it alive
        // for the owning control's lifetime instead of disposing inside Closed.
        var menu=choices??(choices=new ContextMenuStrip{ShowImageMargin=false,ShowCheckMargin=true});
        if(menu.Visible){menu.Close();return;}
        menu.Font=Font;
        while(menu.Items.Count>0)menu.Items[0].Dispose();
        for(int i=0;i<Items.Count;i++){int index=i;var item=new ToolStripMenuItem(Convert.ToString(Items[i])){Checked=i==SelectedIndex,Padding=new Padding(8,7,8,7)};item.Click+=delegate{SelectedIndex=index;};menu.Items.Add(item);}
        Ui.Apply(menu);menu.Show(this,new Point(0,Height));
    }
    protected override void Dispose(bool disposing){if(disposing&&choices!=null){choices.Dispose();choices=null;}base.Dispose(disposing);}
    protected override bool ProcessCmdKey(ref Message message,Keys key){if(key==Keys.Space||key==Keys.Enter||key==(Keys.Alt|Keys.Down)){ShowChoices();return true;}if(key==Keys.Up||key==Keys.Down){SelectedIndex=Math.Max(0,Math.Min(Items.Count-1,SelectedIndex+(key==Keys.Down?1:-1)));return true;}return base.ProcessCmdKey(ref message,key);}
}
}
