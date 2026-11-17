using System;using System.Drawing;using System.Reflection;using System.Runtime.InteropServices;using System.Windows.Forms;using System.Diagnostics;
class ToolbarInteractionCheck{
[DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
[DllImport("user32.dll")]static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
[DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr hwnd);
static BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic;
static void Pump(int ms){var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<ms){Application.DoEvents();System.Threading.Thread.Sleep(2);}}
static void Click(Point p){SetCursorPos(p.X,p.Y);Pump(100);mouse_event(2,0,0,0,UIntPtr.Zero);Pump(35);mouse_event(4,0,0,0,UIntPtr.Zero);Pump(100);}
static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
[STAThread]static int Main(string[] args){object chrome=null;var oldCursor=Cursor.Position;try{
Application.EnableVisualStyles();var assembly=Assembly.LoadFrom(args.Length>0?args[0]:@"D:\chatGPT\LumaView\LumaView.exe");var type=assembly.GetType("LumaView.FullscreenChrome");var barType=assembly.GetType("LumaView.ViewerToolStrip")??typeof(ToolStrip);
using(var owner=new Form{StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(100,150,900,650)})using(var bar=(ToolStrip)Activator.CreateInstance(barType))using(var content=new TextBox{Multiline=true,Dock=DockStyle.Fill}){
owner.Controls.Add(content);owner.Show();owner.Activate();content.Focus();chrome=Activator.CreateInstance(type,new object[]{owner,bar,null,new Func<Keys,bool>(k=>false)});
var top=(Form)type.GetField("top",fields).GetValue(chrome);int clicks=0;var button=new ToolStripButton("Click test");button.Click+=delegate{clicks++;owner.Activate();content.Focus();};bar.Items.Insert(0,button);
var menu=new ToolStripDropDownButton("Menu");int selected=0;menu.DropDownItems.Add("Select",null,delegate{selected++;});bar.Items.Insert(1,menu);
type.GetMethod("Enable").Invoke(chrome,null);Pump(250);var point=bar.PointToScreen(new Point(button.Bounds.Left+button.Width/2,button.Bounds.Top+button.Height/2));
Click(point);Check(clicks==1,"first click after viewport focus executes immediately (count="+clicks+")");
for(int i=0;i<12;i++)Click(point);Check(clicks==13,"repeated single clicks after focus returns to viewport");
Click(bar.PointToScreen(new Point(menu.Bounds.Left+menu.Width/2,menu.Bounds.Top+menu.Height/2)));Check(menu.DropDown.Visible,"drop-down opens on first click");
SetCursorPos(top.Right-40,top.Bottom+100);Pump(2500);Check(top.Visible&&menu.DropDown.Visible,"open drop-down prevents auto-hide");
var entry=menu.DropDownItems[0];Click(menu.DropDown.PointToScreen(new Point(entry.Bounds.Left+20,entry.Bounds.Top+entry.Height/2)));Check(selected==1,"drop-down selection executes once");
owner.Activate();content.Focus();SetCursorPos(owner.Left+owner.Width/2,owner.Top+owner.Height/2);Pump(2800);Check(!top.Visible,"idle menu hides");
SetCursorPos(point.X,point.Y);Pump(80);Check(top.Visible&&top.Opacity>=.95,"menu row reveals immediately without touching top edge");Click(point);Check(clicks==14,"first click on newly revealed menu executes");
int overflowClicks=0;var overflowItem=new ToolStripButton("Overflow action"){Overflow=ToolStripItemOverflow.Always};overflowItem.Click+=delegate{overflowClicks++;};bar.Items.Add(overflowItem);Pump(100);
Click(bar.PointToScreen(new Point(bar.OverflowButton.Bounds.Left+bar.OverflowButton.Width/2,bar.OverflowButton.Bounds.Top+bar.OverflowButton.Height/2)));Check(bar.OverflowButton.DropDown.Visible,"overflow opens on first click");Click(bar.OverflowButton.DropDown.PointToScreen(new Point(overflowItem.Bounds.Left+20,overflowItem.Bounds.Top+overflowItem.Height/2)));Check(overflowClicks==1,"overflow command executes once");
var editor=new ToolStripTextBox{AutoSize=false,Width=80,Overflow=ToolStripItemOverflow.Never};bar.Items.Insert(2,editor);Pump(80);Click(editor.TextBox.PointToScreen(new Point(15,10)));SetCursorPos(owner.Left+400,owner.Top+350);Pump(2700);Check(top.Visible&&editor.TextBox.Focused,"keyboard editing keeps toolbar visible");content.Focus();owner.Activate();Pump(2700);Check(!top.Visible,"toolbar hides after editor releases focus");SetCursorPos(point.X,point.Y);Pump(100);
var pin=(ToolStripButton)type.GetField("pin",fields).GetValue(chrome);Click(bar.PointToScreen(new Point(pin.Bounds.Left+pin.Width/2,pin.Bounds.Top+pin.Height/2)));Check(pin.Checked,"pin responds on first click");SetCursorPos(owner.Left+400,owner.Top+350);Pump(2700);Check(top.Visible,"pinned menu remains visible");
((IDisposable)chrome).Dispose();chrome=null;owner.Close();}
Console.WriteLine("PASS toolbar interaction suite");return 0;
}catch(Exception e){Console.WriteLine(e);return 1;}finally{if(chrome!=null)((IDisposable)chrome).Dispose();SetCursorPos(oldCursor.X,oldCursor.Y);}}
}
