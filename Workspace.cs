using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace LumaView {
partial class Viewer {
    void SetupBackgroundMenu(){
        var menu=new ToolStripDropDownButton(Ui.T("배경"));toolbar.Items.Add(menu);
        Action<string,Color,Color,bool> option=(name,a,b,checker)=>menu.DropDownItems.Add(name,null,delegate{canvas.BackgroundOne=a;canvas.BackgroundTwo=b;canvas.Checker=checker;canvas.Invalidate();});
        option(Ui.T("밝은 체크 (기본)"),Color.White,Color.FromArgb(205,205,205),true);
        option(Ui.T("어두운 체크"),Color.FromArgb(65,68,74),Color.FromArgb(45,48,54),true);
        option(Ui.T("흰색"),Color.White,Color.White,false);option(Ui.T("회색"),Color.FromArgb(128,128,128),Color.Gray,false);option(Ui.T("검정"),Color.Black,Color.Black,false);
        menu.DropDownItems.Add(Ui.T("사용자 배경색…"),null,delegate{using(var d=new ColorDialog{Color=canvas.BackgroundOne,FullOpen=true})if(d.ShowDialog(this)==DialogResult.OK){canvas.BackgroundOne=d.Color;canvas.Checker=false;canvas.Invalidate();}});
        menu.DropDownItems.Add(new ToolStripSeparator());
        foreach(int n in new[]{16,24,32,48}){int size=n;menu.DropDownItems.Add(Ui.T("체크 크기 ")+n+" px",null,delegate{canvas.CheckerSize=size;canvas.Checker=true;canvas.Invalidate();});}
    }
}
class ImagePicker : Form {
    readonly ListView list=new ListView();
    readonly Filmstrip preview=new Filmstrip();
    readonly Label caption=new Label();
    readonly string[] files;
    public int SelectedIndex {get {return list.SelectedIndices.Count>0?list.SelectedIndices[0]:-1;}}
    public ImagePicker(string[] paths,int selected,Color background) {
        files=(string[])paths.Clone();Text=Ui.T("이미지 선택");Size=new Size(920,720);MinimumSize=new Size(650,480);StartPosition=FormStartPosition.CenterParent;Font=new Font("Malgun Gothic",9);BackColor=Color.FromArgb(30,33,38);ForeColor=Color.White;
        preview.Dock=DockStyle.Top;preview.Height=155;preview.SetFiles(files,background);
        preview.Selected+=delegate(int index){if(index<0 || index>=list.Items.Count)return;list.Items[index].Selected=true;list.Items[index].Focused=true;list.Items[index].EnsureVisible();list.Focus();};
        preview.OpenSelected+=delegate{if(SelectedIndex>=0){DialogResult=DialogResult.OK;Close();}};
        caption.Dock=DockStyle.Top;caption.Height=30;caption.TextAlign=ContentAlignment.MiddleCenter;
        list.Dock=DockStyle.Fill;list.View=View.Details;list.FullRowSelect=true;list.MultiSelect=false;list.HideSelection=false;list.BackColor=Color.FromArgb(38,42,48);list.ForeColor=Color.White;list.Columns.Add(Ui.T("사진 목록"),490);list.Columns.Add(Ui.T("수정 날짜"),170);list.Columns.Add(Ui.T("크기"),100);
        foreach(string p in files){var f=new FileInfo(p);string date="",size="";try{date=f.LastWriteTime.ToString("yyyy-MM-dd HH:mm");size=(f.Length/1024.0).ToString("N0")+" KB";}catch{}list.Items.Add(new ListViewItem(new[]{Path.GetFileName(p),date,size}));}
        var bottom=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=45,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(6)};
        var ok=new Button{Text=Ui.T("열기"),Width=100,DialogResult=DialogResult.OK,ForeColor=Color.Black};var cancel=new Button{Text=Ui.T("취소"),Width=100,DialogResult=DialogResult.Cancel,ForeColor=Color.Black};
        bottom.Controls.Add(ok);bottom.Controls.Add(cancel);AcceptButton=ok;CancelButton=cancel;
        Controls.Add(list);Controls.Add(caption);Controls.Add(preview);Controls.Add(bottom);
        list.SelectedIndexChanged+=delegate{int index=SelectedIndex;if(index<0)return;preview.SelectIndex(index);caption.Text=Path.GetFileName(files[index]);};
        list.ItemActivate+=delegate{if(SelectedIndex>=0){DialogResult=DialogResult.OK;Close();}};
        Shown+=delegate{if(selected>=0 && selected<list.Items.Count){list.Items[selected].Selected=true;list.Items[selected].Focused=true;list.Items[selected].EnsureVisible();}list.Focus();};
        Ui.Apply(this);
        FormClosing+=delegate(object s,FormClosingEventArgs e){if(DialogResult==DialogResult.OK && SelectedIndex<0)e.Cancel=true;};
    }
}
}
