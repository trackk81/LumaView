using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LumaView {
static class Sequences {
    public static Dictionary<string,string[]> Group(string[] paths){
        var candidates=new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach(string path in paths){
            if(!Album.Supported(path))continue;
            var match=Regex.Match(Path.GetFileNameWithoutExtension(path),@"^(.*?)(\d{3,})$");
            if(!match.Success)continue;
            string key=Path.GetDirectoryName(path)+"|"+match.Groups[1].Value+"|"+match.Groups[2].Length+"|"+Path.GetExtension(path);
            List<string> list;if(!candidates.TryGetValue(key,out list))candidates[key]=list=new List<string>();list.Add(path);
        }
        var result=new Dictionary<string,string[]>(StringComparer.OrdinalIgnoreCase);
        foreach(var list in candidates.Values)if(list.Count>=2){var ordered=list.OrderBy(p=>p,new NaturalSort()).ToArray();result[ordered[0]]=ordered;}
        return result;
    }
    public static void Badge(Bitmap bitmap){using(var g=Graphics.FromImage(bitmap)){
        float x=bitmap.Width/2f,y=bitmap.Height/2f;g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var shade=new SolidBrush(Color.FromArgb(200,12,20,26)))g.FillEllipse(shade,x-23,y-23,46,46);
        using(var font=new Font("Segoe UI",13,FontStyle.Bold))using(var brush=new SolidBrush(Color.FromArgb(151,232,216)))using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})g.DrawString("SQ",font,brush,new RectangleF(x-23,y-23,46,46),format);
    }}
}
}
