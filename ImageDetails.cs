using System;
using System.IO;
using System.Drawing;
using System.Windows.Forms;

namespace LumaView {
partial class Viewer {
    void AddDetailsButton(){
        var button=AddShortcut(Ui.T("상세 정보"),delegate{canvas.ShowDetails=detailsButton.Checked;RefreshDetails();canvas.Invalidate();},"details");
        button.CheckOnClick=true;
        detailsButton=button;
    }
    ToolStripButton detailsButton;
    void ToggleDetails(){canvas.ShowDetails=!canvas.ShowDetails;if(detailsButton!=null)detailsButton.Checked=canvas.ShowDetails;RefreshDetails();canvas.Invalidate();}
    void RefreshDetails(){
        if(album.Image==null){canvas.DetailsText="";return;}
        var orig=Album.OriginalSize(album.Image);
        double zoom=canvas.ViewScale*album.Image.Width/Math.Max(1,orig.Width)*100;
        string size=Ui.T("확인 불가"),modified=Ui.T("확인 불가");
        try{var file=new FileInfo(album.Current);long bytes=file.Length;size=bytes>=1048576?(bytes/1048576.0).ToString("N2")+" MB":(bytes/1024.0).ToString("N1")+" KB";size+=" ("+bytes.ToString("N0")+" bytes)";modified=file.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");}catch(IOException){}catch(UnauthorizedAccessException){}
        canvas.DetailsText=String.Format(Ui.T("파일 이름: {0}  [{1}/{2}]\n파일 크기: {3}\n수정 날짜: {4}\n이미지 정보: {5} × {6} px  ({7})\n화면 배율: {8:0}%"),Path.GetFileName(album.Current),album.Index+1,album.Files.Length,size,modified,orig.Width,orig.Height,Path.GetExtension(album.Current).TrimStart('.').ToUpperInvariant(),zoom);
    }
}
}
