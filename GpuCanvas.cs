using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LumaView {
sealed class GpuCanvas : IDisposable {
    [StructLayout(LayoutKind.Sequential)] struct TargetProperties {
        public int Type, Format, Alpha;
        public float DpiX, DpiY;
        public int Usage, FeatureLevel;
    }
    [StructLayout(LayoutKind.Sequential)] struct SizeU {
        public uint Width, Height;
        public SizeU(int width,int height){Width=(uint)width;Height=(uint)height;}
    }
    [StructLayout(LayoutKind.Sequential)] struct WindowProperties {
        public IntPtr Window;
        public SizeU Size;
        public int Options;
    }
    [StructLayout(LayoutKind.Sequential)] struct BitmapProperties {
        public int Format, Alpha;
        public float DpiX,DpiY;
    }
    [StructLayout(LayoutKind.Sequential)] struct BitmapBrushProperties {
        public int ExtendX, ExtendY, Interpolation;
    }
    [StructLayout(LayoutKind.Sequential)] struct Rect {
        public float Left,Top,Right,Bottom;
        public Rect(RectangleF r){Left=r.Left;Top=r.Top;Right=r.Right;Bottom=r.Bottom;}
    }
    [StructLayout(LayoutKind.Sequential)] struct Rgba {
        public float R,G,B,A;
        public Rgba(Color c){R=c.R/255f;G=c.G/255f;B=c.B/255f;A=1;}
    }

    // Windows Direct2D COM ABI. The numbered slots below include IUnknown and
    // ID2D1Resource methods and follow the order declared in d2d1.h.
    [DllImport("d2d1.dll", ExactSpelling=true)]
    static extern int D2D1CreateFactory(int type,ref Guid iid,IntPtr options,out IntPtr factory);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int CreateWindowTarget(IntPtr self,ref TargetProperties properties,ref WindowProperties window,out IntPtr target);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int CreateBitmap(IntPtr self,SizeU size,IntPtr data,uint stride,ref BitmapProperties properties,out IntPtr bitmap);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int CreateBitmapBrush(IntPtr self,IntPtr bitmap,ref BitmapBrushProperties options,IntPtr style,out IntPtr brush);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void FillRectangle(IntPtr self,ref Rect destination,IntPtr brush);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate uint MaximumBitmapSize(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int ResizeTarget(IntPtr self,ref SizeU size);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void BeginDraw(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int EndDraw(IntPtr self,IntPtr tag1,IntPtr tag2);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void ClearTarget(IntPtr self,ref Rgba color);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void DrawBitmap(IntPtr self,IntPtr bitmap,ref Rect destination,float opacity,int interpolation,IntPtr source);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void PushClip(IntPtr self,ref Rect bounds,int antialiasMode);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void PopClip(IntPtr self);

    static T Method<T>(IntPtr instance,int slot) where T:class {
        IntPtr address=Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance),slot*IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer(address,typeof(T)) as T;
    }
    static void Check(int result){if(result<0)Marshal.ThrowExceptionForHR(result);}
    static void Release(ref IntPtr pointer){if(pointer!=IntPtr.Zero){Marshal.Release(pointer);pointer=IntPtr.Zero;}}

    sealed class Tile {
        public Rectangle Area, Samples;
        public IntPtr Texture;
    }
    readonly List<Tile> tiles=new List<Tile>();
    IntPtr factory,target;
    IntPtr backgroundBrush;
    int backgroundCell;
    Color backgroundOne,backgroundTwo;
    Size size;
    Bitmap source;
    int tileSize;
    CreateBitmap createBitmap;
    CreateBitmapBrush createBitmapBrush;
    FillRectangle fillRectangle;
    BeginDraw begin;
    EndDraw end;
    ClearTarget clear;
    DrawBitmap draw;
    PushClip push;
    PopClip pop;
    ResizeTarget resize;
    const int RecreateTarget=unchecked((int)0x8899000C);

    void Open(IntPtr window,Size viewport){
        var iid=new Guid("06152247-6f50-465a-9245-118bfd3b6007");
        Check(D2D1CreateFactory(0,ref iid,IntPtr.Zero,out factory));
        var settings=new TargetProperties{Type=2,DpiX=96,DpiY=96}; // Hardware only.
        var properties=new WindowProperties{Window=window,Size=new SizeU(viewport.Width,viewport.Height)};
        Check(Method<CreateWindowTarget>(factory,14)(factory,ref settings,ref properties,out target));
        size=viewport;
        tileSize=(int)Math.Min(2048,Method<MaximumBitmapSize>(target,55)(target))-2;
        if(tileSize<=0)throw new NotSupportedException("Direct2D texture size is too small.");
        createBitmap=Method<CreateBitmap>(target,4);
        createBitmapBrush=Method<CreateBitmapBrush>(target,7);
        fillRectangle=Method<FillRectangle>(target,17);
        draw=Method<DrawBitmap>(target,26);
        push=Method<PushClip>(target,45);
        pop=Method<PopClip>(target,46);
        clear=Method<ClearTarget>(target,47);
        begin=Method<BeginDraw>(target,48);
        end=Method<EndDraw>(target,49);
        resize=Method<ResizeTarget>(target,58);
    }

    public void InvalidateImage(){
        foreach(var tile in tiles)Release(ref tile.Texture);
        tiles.Clear();
        source=null;
    }
    void Upload(Bitmap image,bool opaque){
        InvalidateImage();
        var format=opaque?PixelFormat.Format32bppArgb:PixelFormat.Format32bppPArgb;
        for(int y=0;y<image.Height;y+=tileSize)
        for(int x=0;x<image.Width;x+=tileSize){
            var area=new Rectangle(x,y,Math.Min(tileSize,image.Width-x),Math.Min(tileSize,image.Height-y));
            var samples=area;
            samples.Inflate(1,1);
            samples.Intersect(new Rectangle(0,0,image.Width,image.Height));
            using(var pixels=image.Clone(samples,PixelFormat.Format32bppArgb)){
                var data=pixels.LockBits(new Rectangle(Point.Empty,pixels.Size),ImageLockMode.ReadOnly,format);
                try{
                    if(data.Stride<=0)throw new NotSupportedException("Unexpected bitmap stride.");
                    var properties=new BitmapProperties{Format=87,Alpha=opaque?3:1,DpiX=96,DpiY=96};
                    var tile=new Tile{Area=area,Samples=samples};
                    int result=createBitmap(target,new SizeU(samples.Width,samples.Height),data.Scan0,(uint)data.Stride,ref properties,out tile.Texture);
                    if(result<0){Release(ref tile.Texture);Check(result);}
                    tiles.Add(tile);
                }finally{pixels.UnlockBits(data);}
            }
        }
        source=image;
    }
    void PrepareBackground(int cell,Color first,Color second){
        if(backgroundBrush!=IntPtr.Zero&&cell==backgroundCell&&first==backgroundOne&&second==backgroundTwo)return;
        Release(ref backgroundBrush);
        int side=cell*2;
        var pixels=new int[side*side];
        for(int y=0;y<side;y++)
            for(int x=0;x<side;x++)
                pixels[y*side+x]=((x<cell)==(y<cell)?first:second).ToArgb();
        var pinned=GCHandle.Alloc(pixels,GCHandleType.Pinned);
        IntPtr bitmap=IntPtr.Zero;
        try{
            var properties=new BitmapProperties{Format=87,Alpha=3,DpiX=96,DpiY=96};
            Check(createBitmap(target,new SizeU(side,side),pinned.AddrOfPinnedObject(),(uint)(side*4),ref properties,out bitmap));
            var options=new BitmapBrushProperties{ExtendX=1,ExtendY=1,Interpolation=0};
            Check(createBitmapBrush(target,bitmap,ref options,IntPtr.Zero,out backgroundBrush));
            backgroundCell=cell;backgroundOne=first;backgroundTwo=second;
        }finally{Release(ref bitmap);pinned.Free();}
    }
    static RectangleF Project(Rectangle rectangle,RectangleF bounds,float scale){
        return new RectangleF(bounds.X+rectangle.X*scale,bounds.Y+rectangle.Y*scale,rectangle.Width*scale,rectangle.Height*scale);
    }
    public bool Render(IntPtr window,Size viewport,Bitmap image,RectangleF bounds,float scale,Color background,
        bool opaque,int checkerSize,Color first,Color second){
        if(target==IntPtr.Zero)Open(window,viewport);
        if(viewport!=size){
            var dimensions=new SizeU(viewport.Width,viewport.Height);
            Check(resize(target,ref dimensions));
            size=viewport;
        }
        if(!ReferenceEquals(source,image))Upload(image,opaque);
        if(!opaque)PrepareBackground(checkerSize,first,second);
        var screen=new RectangleF(PointF.Empty,viewport);
        begin(target);
        var color=new Rgba(background);
        clear(target,ref color);
        if(!opaque){
            var backing=new Rect(bounds);
            fillRectangle(target,ref backing,backgroundBrush);
        }
        foreach(var tile in tiles){
            var area=Project(tile.Area,bounds,scale);
            if(!area.IntersectsWith(screen))continue;
            var clip=new Rect(area);
            var destination=new Rect(Project(tile.Samples,bounds,scale));
            push(target,ref clip,1);
            draw(target,tile.Texture,ref destination,1,scale>=4?0:1,IntPtr.Zero);
            pop(target);
        }
        int result=end(target,IntPtr.Zero,IntPtr.Zero);
        if(result==RecreateTarget){Dispose();return false;}
        Check(result);
        return true;
    }
    public void Dispose(){
        InvalidateImage();
        Release(ref backgroundBrush);
        Release(ref target);
        Release(ref factory);
    }
}
}
