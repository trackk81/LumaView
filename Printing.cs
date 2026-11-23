using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LumaView {
sealed class MediaPrintDocument : PrintDocument {
    readonly string[] files;
    readonly bool pdf;
    readonly int current;
    int next,last;
    internal MediaPrintDocument(string[] paths,int count,int currentPage){
        files=paths;pdf=PdfPages.IsPdf(paths[0]);current=currentPage;
        DocumentName=Path.GetFileName(paths[0]);OriginAtMargins=true;
        DefaultPageSettings.Margins=new Margins(50,50,50,50);
        PrinterSettings.MinimumPage=1;PrinterSettings.MaximumPage=count;
        PrinterSettings.FromPage=1;PrinterSettings.ToPage=count;
        if(current>=0&&count>1)PrinterSettings.PrintRange=PrintRange.CurrentPage;
    }
    protected override void OnBeginPrint(PrintEventArgs e){
        var settings=PrinterSettings;
        next=0;last=settings.MaximumPage-1;
        if(settings.PrintRange==PrintRange.CurrentPage)next=last=Math.Max(0,current);
        if(settings.PrintRange==PrintRange.SomePages){next=settings.FromPage-1;last=settings.ToPage-1;}
        base.OnBeginPrint(e);
    }
    protected override void OnPrintPage(PrintPageEventArgs e){
        var area=e.MarginBounds.Size;
        int pixels=(int)Math.Min(6000,Math.Ceiling(Math.Max(area.Width,area.Height)*Math.Max(300,e.Graphics.DpiX)/100.0));
        using(var image=pdf?Task.Run(()=>PdfPages.Render(files[0],next,pixels)).GetAwaiter().GetResult():Album.Decode(files[next])){
            double scale=Math.Min((double)area.Width/image.Width,(double)area.Height/image.Height);
            float w=(float)(image.Width*scale),h=(float)(image.Height*scale);
            e.Graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(image,new RectangleF((area.Width-w)/2,(area.Height-h)/2,w,h));
        }
        e.HasMorePages=++next<=last;
        base.OnPrintPage(e);
    }
}
partial class Viewer {
    readonly Font printerFont=new Font("Segoe MDL2 Assets",13);
    readonly ToolTip printTip=new ToolTip();
    ModernButton galleryPrint;
    ToolStripButton viewerPrint;
    bool printing;
    string PrintLabel {get{return ComponentSetup.Say("인쇄","Print");}}
    void RefreshPrintLabels(){
        string label=PrintLabel;
        galleryPrint.AccessibleName=viewerPrint.AccessibleName=label;
        viewerPrint.ToolTipText=label+" (Ctrl+P)";
        printTip.SetToolTip(galleryPrint,viewerPrint.ToolTipText);
    }
    ToolStripButton ViewerPrintButton(){
        viewerPrint=new ToolStripButton("\uE749"){Name="viewerPrint",Font=printerFont,DisplayStyle=ToolStripItemDisplayStyle.Text,
            AccessibleName=PrintLabel,ToolTipText=PrintLabel+" (Ctrl+P)",Padding=new Padding(5,2,5,2)};
        viewerPrint.Click+=delegate{PrintSelected();};return viewerPrint;
    }
    ModernButton GalleryPrintButton(){
        galleryPrint=new ModernButton{Name="galleryPrint",Text="\uE749",Font=printerFont,MinimumSize=new Size(42,38),
            Padding=new Padding(6),AccessibleName=PrintLabel,Enabled=false};
        printTip.SetToolTip(galleryPrint,PrintLabel+" (Ctrl+P)");
        galleryPrint.Click+=delegate{PrintSelected();};
        Disposed+=delegate{printTip.Dispose();printerFont.Dispose();};
        return galleryPrint;
    }
    void UpdatePrintButtons(){
        if(viewerPrint!=null)viewerPrint.Enabled=!printing&&!busy&&album.Image!=null;
        if(galleryPrint!=null){
            string path=fileList.SelectedItems.Count==0?null:fileList.SelectedItems[0].Tag as string;
            galleryPrint.Enabled=!printing&&!galleryLoading&&path!=null&&Album.Supported(path);
        }
    }
    async void PrintSelected(){
        string path=FileCommandPath();
        if(printing||busy||String.IsNullOrEmpty(path)||!Album.Supported(path))return;
        StopSlide();printing=true;UpdatePrintButtons();
        try{
            string[] files;
            if(full||!sequenceGroups.TryGetValue(path,out files))files=new[]{path};
            int current=full?album.PageIndex:-1;
            int count=PdfPages.IsPdf(path)?await Task.Run(()=>PdfPages.Count(path)):files.Length;
            if(IsDisposed||Disposing)return;
            using(var doc=new MediaPrintDocument(files,count,current))
            using(var dialog=new PrintDialog{Document=doc,AllowCurrentPage=current>=0&&count>1,AllowSomePages=count>1,UseEXDialog=true}){
                if(dialog.ShowDialog(this)==DialogResult.OK)doc.Print();
            }
        }catch(Exception ex){if(!IsDisposed)MessageBox.Show(this,ex.Message,PrintLabel,MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        finally{printing=false;if(!IsDisposed)UpdatePrintButtons();}
    }
}
}
