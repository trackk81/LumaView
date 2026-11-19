using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

class ModernLayoutChecks {
    static Assembly app;
    static BindingFlags all=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    static IEnumerable<Control> Walk(Control parent){foreach(Control c in parent.Controls){yield return c;foreach(var nested in Walk(c))yield return nested;}}
    static void Pump(){var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<60){Application.DoEvents();System.Threading.Thread.Sleep(2);}}
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    static void Audit(Form form,string scenario){
        foreach(var c in Walk(form).Where(c=>c.Visible)){
            if(c.GetType().Name=="WrapLabel"){
                int needed=c.GetPreferredSize(new Size(c.Width,0)).Height;
                Check(c.Height>=needed-2,scenario+" clipped label: "+c.Text+" bounds="+c.Bounds+" expected height="+needed);
            }
            if(c is Button&&c.GetType().Name.StartsWith("Modern")){
                var needed=c.GetPreferredSize(Size.Empty);
                Check(c.Width>=needed.Width-1&&c.Height>=needed.Height-1,scenario+" clipped button: "+c.Text+" actual="+c.Size+" expected="+needed);
                Check(c.Left>=0&&c.Right<=c.Parent.ClientSize.Width+1,scenario+" button outside container: "+c.Text);
            }
            if(c.GetType().Name=="SettingsStack"||c.GetType().Name=="ModernCard"){
                var children=c.Controls.Cast<Control>().Where(x=>x.Visible).ToArray();
                for(int i=1;i<children.Length;i++)Check(children[i].Top>=children[i-1].Bottom,scenario+" overlapping content: "+children[i].Text);
            }
            if(c.GetType().Name=="SettingsRow"){
                Check(c.Height<=110,scenario+" excessive SettingsRow height="+c.Height);
            }
        }
        var save=Walk(form).FirstOrDefault(c=>c.Name=="settingsSave");
        if(save!=null){var rectangle=form.RectangleToClient(save.RectangleToScreen(save.ClientRectangle));Check(form.ClientRectangle.Contains(rectangle),scenario+" save button unreachable");}
    }
    static void ScaleUi(Form form,float scale){
        if(scale==1)return;
        var controls=Walk(form).Concat(new[]{form}).ToArray();var fonts=controls.Select(c=>c.Font).ToArray();
        form.Scale(new SizeF(scale,scale));
        for(int i=0;i<controls.Length;i++)controls[i].Font=new Font(fonts[i].FontFamily,fonts[i].SizeInPoints*scale,fonts[i].Style);
    }
    [STAThread] static int Main(string[] args){try{
        Application.EnableVisualStyles();app=Assembly.LoadFrom(args[0]);var ui=app.GetType("LumaView.Ui");
        foreach(string language in new[]{"ko","en"})foreach(string theme in new[]{"dark","light","custom"})foreach(float scale in new[]{1f,1.5f,2f}){
            ui.GetField("Language").SetValue(null,language);ui.GetField("Theme").SetValue(null,theme);
            using(var form=(Form)Activator.CreateInstance(app.GetType("LumaView.SettingsDialog"))){
                form.Show();ScaleUi(form,scale);form.ClientSize=new Size((int)(700*scale),(int)(520*scale));
                for(int page=0;page<4;page++){
                    form.GetType().GetMethod("SelectPage",all).Invoke(form,new object[]{page});Pump();
                    if(page==1){var status=Walk(form).First(c=>c.Name=="componentStatus");status.Text=String.Join(" ",Enumerable.Repeat(language=="ko"?"연결에 실패했습니다. 인터넷 연결과 설치 정책을 확인하고 다시 시도하세요.":"The connection failed. Check your network and installation policy, then try again.",8));Pump();}
                    Audit(form,language+"/"+theme+"/"+scale+"/page"+page);
                }
                form.Close();
            }
            Console.WriteLine("PASS settings layout "+language+" "+theme+" at "+scale+"x with long status text");
        }
        foreach(string language in new[]{"ko","en"}){
            ui.GetField("Language").SetValue(null,language);ui.GetField("Theme").SetValue(null,"dark");
            using(var form=(Form)Activator.CreateInstance(app.GetType("LumaView.Viewer"),new object[]{null})){
                form.Show();form.Size=new Size(720,500);Pump();Audit(form,"gallery "+language);form.Close();
            }
            Console.WriteLine("PASS compact gallery controls "+language);
        }
        Console.WriteLine("PASS modern layout regression checks");return 0;
    }catch(Exception ex){Console.WriteLine(ex);return 1;}}
}
