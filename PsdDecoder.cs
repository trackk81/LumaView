using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace LumaView {
// Native streaming Adobe PSD (v1) and PSB (v2) merged-composite decoder.
// Seeks directly past Layer & Mask info and streams row-by-row so huge
// multi-layer (>100MP) Photoshop PSD/PSB files open quickly without OOM.
static class PsdDecoder {
    const int MaxPsbDimension=300000;
    internal const long MaxDisplayPixels=64000000;
    internal const int MaxDisplaySide=16384;

    static ushort ReadU16(BinaryReader r){
        var b=r.ReadBytes(2);
        if(b.Length!=2)throw new EndOfStreamException();
        return (ushort)((b[0]<<8)|b[1]);
    }
    static short ReadI16(BinaryReader r){return unchecked((short)ReadU16(r));}
    static uint ReadU32(BinaryReader r){
        var b=r.ReadBytes(4);
        if(b.Length!=4)throw new EndOfStreamException();
        return ((uint)b[0]<<24)|((uint)b[1]<<16)|((uint)b[2]<<8)|b[3];
    }
    static long ReadU64(BinaryReader r){
        var b=r.ReadBytes(8);
        if(b.Length!=8)throw new EndOfStreamException();
        ulong v=0;
        for(int i=0;i<8;i++)v=(v<<8)|b[i];
        if(v>long.MaxValue)throw new InvalidDataException("PSD section too large.");
        return (long)v;
    }
    static byte[] ReadExact(Stream s,int count){
        var buffer=new byte[count];
        int offset=0;
        while(offset<count){
            int read=s.Read(buffer,offset,count-offset);
            if(read<=0)throw new EndOfStreamException();
            offset+=read;
        }
        return buffer;
    }
    static void ReadInto(Stream s,byte[] buffer,int count){
        int offset=0;
        while(offset<count){
            int read=s.Read(buffer,offset,count-offset);
            if(read<=0)throw new EndOfStreamException();
            offset+=read;
        }
    }
    static byte ClampByte(double v){
        if(double.IsNaN(v)||v<=0)return 0;
        if(v>=255)return 255;
        return (byte)Math.Round(v);
    }
    static byte LinearToSrgb(float v){
        if(float.IsNaN(v)||v<=0f)return 0;
        if(v>=1f)return 255;
        double s=v<=0.0031308f?12.92*v:1.055*Math.Pow(v,1.0/2.4)-0.055;
        return ClampByte(s*255.0);
    }
    static byte SampleToByte(byte[] row,int x,int depth){
        if(depth==8)return row[x];
        if(depth==16)return row[x*2];
        if(depth==32){
            int o=x*4;
            byte[] le={row[o+3],row[o+2],row[o+1],row[o]};
            return LinearToSrgb(BitConverter.ToSingle(le,0));
        }
        return 0;
    }
    static byte SampleLinearByte(byte[] row,int x,int depth){
        if(depth==8)return row[x];
        if(depth==16)return row[x*2];
        if(depth==32){
            int o=x*4;
            byte[] le={row[o+3],row[o+2],row[o+1],row[o]};
            float v=BitConverter.ToSingle(le,0);
            if(float.IsNaN(v)||v<=0f)return 0;
            if(v>=1f)return 255;
            return ClampByte(v*255.0);
        }
        return 0;
    }
    static byte UnmatteWhite(byte value,byte alpha){
        if(alpha==0)return 0;
        if(alpha==255)return value;
        int unmatted=(value*255-(255-alpha)*255)/alpha;
        if(unmatted<0)return 0;
        if(unmatted>255)return 255;
        return (byte)unmatted;
    }
    static void LabToRgb(byte lByte,byte aByte,byte bByte,out byte r,out byte g,out byte b){
        double L=lByte*(100.0/255.0);
        double a=aByte-128.0;
        double bb=bByte-128.0;
        double fy=(L+16.0)/116.0;
        double fx=fy+a/500.0;
        double fz=fy-bb/200.0;
        const double delta=6.0/29.0;
        double xr=fx>delta?fx*fx*fx:(fx-16.0/116.0)*3.0*delta*delta;
        double yr=fy>delta?fy*fy*fy:(fy-16.0/116.0)*3.0*delta*delta;
        double zr=fz>delta?fz*fz*fz:(fz-16.0/116.0)*3.0*delta*delta;
        double X=xr*0.96422,Y=yr*1.0,Z=zr*0.82521;
        double rl= 3.1338561*X-1.6168667*Y-0.4906146*Z;
        double gl=-0.9787684*X+1.9161415*Y+0.0334540*Z;
        double bl= 0.0719453*X-0.2289914*Y+1.4052427*Z;
        r=LinearToSrgb((float)rl);
        g=LinearToSrgb((float)gl);
        b=LinearToSrgb((float)bl);
    }

    internal static Size ComputeTargetSize(int width,int height,int maxWidth,int maxHeight){
        double scale=1.0;
        if(maxWidth>0&&maxHeight>0&&(width>maxWidth||height>maxHeight))
            scale=Math.Min(scale,Math.Min((double)maxWidth/width,(double)maxHeight/height));
        if(width>MaxDisplaySide||height>MaxDisplaySide)
            scale=Math.Min(scale,Math.Min((double)MaxDisplaySide/width,(double)MaxDisplaySide/height));
        long pixels=(long)width*height;
        if(pixels>MaxDisplayPixels)
            scale=Math.Min(scale,Math.Sqrt((double)MaxDisplayPixels/pixels));
        if(scale>=1.0)return new Size(width,height);
        return new Size(Math.Max(1,(int)Math.Round(width*scale)),Math.Max(1,(int)Math.Round(height*scale)));
    }

    struct ResourceInfo {
        public bool HasVersionInfo;
        public bool HasRealMergedData;
        public int TransparentIndex;
        public long ThumbnailOffset;
        public int ThumbnailLength;
    }

    static ResourceInfo ParseResources(Stream stream,BinaryReader reader,long end){
        var info=new ResourceInfo{HasRealMergedData=true,TransparentIndex=-1,ThumbnailOffset=-1};
        try{
            while(stream.Position+12<=end){
                var sig=reader.ReadBytes(4);
                if(sig.Length<4||sig[0]!='8'||sig[1]!='B'||sig[2]!='I'||sig[3]!='M')break;
                ushort id=ReadU16(reader);
                int nameLen=stream.ReadByte();
                if(nameLen<0)break;
                int skipName=nameLen+((nameLen+1)%2==1?1:0);
                if(stream.Position+skipName+4>end)break;
                if(skipName>0)stream.Seek(skipName,SeekOrigin.Current);
                uint dataLen=ReadU32(reader);
                long dataStart=stream.Position;
                long padded=dataLen+(dataLen&1);
                if(dataStart+dataLen>end)break;
                if(id==1057&&dataLen>=5){
                    info.HasVersionInfo=true;
                    stream.Seek(4,SeekOrigin.Current);
                    int merged=stream.ReadByte();
                    info.HasRealMergedData=merged!=0;
                }else if(id==1047&&dataLen>=2){
                    info.TransparentIndex=ReadU16(reader);
                }else if((id==1036||id==1033)&&dataLen>28&&dataLen<16*1024*1024){
                    if(id==1036||info.ThumbnailOffset<0){
                        info.ThumbnailOffset=dataStart;
                        info.ThumbnailLength=(int)dataLen;
                    }
                }
                long next=dataStart+padded;
                if(next>end)next=end;
                stream.Position=next;
            }
        }catch(EndOfStreamException){}
        stream.Position=end;
        return info;
    }

    static void DecodePackBitsRow(byte[] packed,int len,byte[] dst,int rowBytes){
        int srcPos=0,dstPos=0;
        while(srcPos<len&&dstPos<rowBytes){
            sbyte n=unchecked((sbyte)packed[srcPos++]);
            if(n>=0){
                int count=n+1;
                int avail=Math.Min(count,Math.Min(len-srcPos,rowBytes-dstPos));
                Buffer.BlockCopy(packed,srcPos,dst,dstPos,avail);
                srcPos+=count;dstPos+=avail;
            }else if(n!=-128){
                int count=1-n;
                if(srcPos>=len)break;
                byte val=packed[srcPos++];
                int avail=Math.Min(count,rowBytes-dstPos);
                for(int k=0;k<avail;k++)dst[dstPos++]=val;
            }
        }
        while(dstPos<rowBytes)dst[dstPos++]=0;
    }

    static void UndoZipPredictionRow(byte[] row,byte[] temp,int width,int depth,int rowBytes){
        if(depth==8){
            for(int x=1;x<rowBytes;x++)row[x]=unchecked((byte)(row[x]+row[x-1]));
        }else if(depth==16){
            int prev=(row[0]<<8)|row[1];
            for(int x=1;x<width;x++){
                int o=x*2;
                int val=(prev+((row[o]<<8)|row[o+1]))&0xFFFF;
                row[o]=(byte)(val>>8);row[o+1]=(byte)(val&0xFF);
                prev=val;
            }
        }else if(depth==32){
            for(int x=1;x<rowBytes;x++)row[x]=unchecked((byte)(row[x]+row[x-1]));
            for(int x=0;x<width;x++){
                temp[x*4]=row[x];
                temp[x*4+1]=row[width+x];
                temp[x*4+2]=row[width*2+x];
                temp[x*4+3]=row[width*3+x];
            }
            Buffer.BlockCopy(temp,0,row,0,rowBytes);
        }
    }

    static void WritePlaneRow(byte[] bgra,int dstY,int outW,int[] srcXMap,byte[] rowBuf,int plane,ushort colorMode,int baseChannels,int depth,byte[] palette,int transparentIndex){
        int rowBase=dstY*outW*4;
        switch(colorMode){
            case 0:
                for(int x=0;x<outW;x++){
                    int sx=srcXMap[x];
                    byte v=(byte)(((rowBuf[sx>>3]>>(7-(sx&7)))&1)!=0?0:255);
                    int o=rowBase+x*4;
                    bgra[o]=v;bgra[o+1]=v;bgra[o+2]=v;bgra[o+3]=255;
                }
                break;
            case 1:
            case 8:
                if(plane==0){
                    for(int x=0;x<outW;x++){
                        byte v=SampleToByte(rowBuf,srcXMap[x],depth);
                        int o=rowBase+x*4;
                        bgra[o]=v;bgra[o+1]=v;bgra[o+2]=v;bgra[o+3]=255;
                    }
                }else if(plane==1){
                    for(int x=0;x<outW;x++)bgra[rowBase+x*4+3]=SampleLinearByte(rowBuf,srcXMap[x],depth);
                }
                break;
            case 2:
                for(int x=0;x<outW;x++){
                    byte idx=rowBuf[srcXMap[x]];
                    int o=rowBase+x*4;
                    bgra[o]=palette[512+idx];bgra[o+1]=palette[256+idx];bgra[o+2]=palette[idx];
                    bgra[o+3]=(byte)(idx==transparentIndex?0:255);
                }
                break;
            case 3:
                if(plane==0){
                    for(int x=0;x<outW;x++){int o=rowBase+x*4;bgra[o+2]=SampleToByte(rowBuf,srcXMap[x],depth);bgra[o+3]=255;}
                }else if(plane==1){
                    for(int x=0;x<outW;x++)bgra[rowBase+x*4+1]=SampleToByte(rowBuf,srcXMap[x],depth);
                }else if(plane==2){
                    for(int x=0;x<outW;x++)bgra[rowBase+x*4]=SampleToByte(rowBuf,srcXMap[x],depth);
                }else if(plane==3){
                    for(int x=0;x<outW;x++)bgra[rowBase+x*4+3]=SampleLinearByte(rowBuf,srcXMap[x],depth);
                }
                break;
            case 4:
                if(plane==0){
                    for(int x=0;x<outW;x++){int o=rowBase+x*4;bgra[o+2]=SampleLinearByte(rowBuf,srcXMap[x],depth);bgra[o+3]=255;}
                }else if(plane==1){
                    for(int x=0;x<outW;x++)bgra[rowBase+x*4+1]=SampleLinearByte(rowBuf,srcXMap[x],depth);
                }else if(plane==2){
                    for(int x=0;x<outW;x++)bgra[rowBase+x*4]=SampleLinearByte(rowBuf,srcXMap[x],depth);
                }else if(plane==3){
                    for(int x=0;x<outW;x++){
                        int o=rowBase+x*4;
                        int k=SampleLinearByte(rowBuf,srcXMap[x],depth);
                        bgra[o+2]=(byte)((bgra[o+2]*k+127)/255);
                        bgra[o+1]=(byte)((bgra[o+1]*k+127)/255);
                        bgra[o]=(byte)((bgra[o]*k+127)/255);
                    }
                }else if(plane==4){
                    for(int x=0;x<outW;x++)bgra[rowBase+x*4+3]=SampleLinearByte(rowBuf,srcXMap[x],depth);
                }
                break;
            case 7:
                if(baseChannels>=3){
                    if(plane==0)for(int x=0;x<outW;x++){int o=rowBase+x*4;bgra[o+2]=SampleToByte(rowBuf,srcXMap[x],depth);bgra[o+3]=255;}
                    else if(plane==1)for(int x=0;x<outW;x++)bgra[rowBase+x*4+1]=SampleToByte(rowBuf,srcXMap[x],depth);
                    else if(plane==2)for(int x=0;x<outW;x++)bgra[rowBase+x*4]=SampleToByte(rowBuf,srcXMap[x],depth);
                }else if(plane==0){
                    for(int x=0;x<outW;x++){
                        byte v=SampleToByte(rowBuf,srcXMap[x],depth);
                        int o=rowBase+x*4;
                        bgra[o]=v;bgra[o+1]=v;bgra[o+2]=v;bgra[o+3]=255;
                    }
                }
                break;
            case 9:
                if(plane==0){
                    for(int x=0;x<outW;x++){int o=rowBase+x*4;bgra[o+2]=SampleLinearByte(rowBuf,srcXMap[x],depth);bgra[o+3]=255;}
                }else if(plane==1){
                    for(int x=0;x<outW;x++)bgra[rowBase+x*4+1]=SampleLinearByte(rowBuf,srcXMap[x],depth);
                }else if(plane==2){
                    for(int x=0;x<outW;x++){
                        int o=rowBase+x*4;
                        byte r,g,b;
                        LabToRgb(bgra[o+2],bgra[o+1],SampleLinearByte(rowBuf,srcXMap[x],depth),out r,out g,out b);
                        bgra[o+2]=r;bgra[o+1]=g;bgra[o]=b;
                    }
                }else if(plane==3){
                    for(int x=0;x<outW;x++)bgra[rowBase+x*4+3]=SampleLinearByte(rowBuf,srcXMap[x],depth);
                }
                break;
        }
    }

    public static Size? TryReadSize(Func<Stream> open){
        try{
            using(var fs=open())
            using(var reader=new BinaryReader(fs)){
                if(fs.Length<26)return null;
                var sig=reader.ReadBytes(4);
                if(sig.Length!=4||sig[0]!='8'||sig[1]!='B'||sig[2]!='P'||sig[3]!='S')return null;
                ushort version=ReadU16(reader);
                if(version!=1&&version!=2)return null;
                fs.Seek(8,SeekOrigin.Current);
                uint h=ReadU32(reader),w=ReadU32(reader);
                if(w==0||h==0||w>MaxPsbDimension||h>MaxPsbDimension)return null;
                return new Size((int)w,(int)h);
            }
        }catch{return null;}
    }

    public static Bitmap TryDecode(Func<Stream> open,int maxWidth,int maxHeight,out bool missingMergedData){
        missingMergedData=false;
        try{
            using(var fs=open())
            using(var stream=new BufferedStream(fs,128*1024))
            using(var reader=new BinaryReader(stream)){
                if(fs.Length<26)return null;
                var sig=reader.ReadBytes(4);
                if(sig.Length!=4||sig[0]!='8'||sig[1]!='B'||sig[2]!='P'||sig[3]!='S')return null;
                ushort version=ReadU16(reader);
                if(version!=1&&version!=2)return null;
                bool isPsb=version==2;
                stream.Seek(6,SeekOrigin.Current);
                ushort channels=ReadU16(reader);
                uint rawHeight=ReadU32(reader);
                uint rawWidth=ReadU32(reader);
                ushort depth=ReadU16(reader);
                ushort colorMode=ReadU16(reader);
                if(channels==0||rawWidth==0||rawHeight==0||rawWidth>MaxPsbDimension||rawHeight>MaxPsbDimension)return null;
                if(depth!=1&&depth!=8&&depth!=16&&depth!=32)return null;
                if(depth==1&&colorMode!=0)return null;
                int width=(int)rawWidth,height=(int)rawHeight;

                // Section 2: Color Mode Data
                uint cmLen=ReadU32(reader);
                if(stream.Position+cmLen>fs.Length)return null;
                byte[] palette=null;
                if(colorMode==2&&cmLen==768)palette=ReadExact(stream,768);
                else if(cmLen>0)stream.Seek(cmLen,SeekOrigin.Current);

                // Section 3: Image Resources
                uint resLen=ReadU32(reader);
                if(stream.Position+resLen>fs.Length)return null;
                var resources=ParseResources(stream,reader,stream.Position+resLen);
                if(!resources.HasRealMergedData){missingMergedData=true;return null;}

                // Section 4: Layer and Mask Information
                long lmLen=isPsb?ReadU64(reader):ReadU32(reader);
                long lmStart=stream.Position;
                if(lmLen<0||lmStart+lmLen>fs.Length)return null;
                bool mergedHasAlpha=false;
                if(lmLen>=(isPsb?10:6)){
                    long layerInfoLen=isPsb?ReadU64(reader):ReadU32(reader);
                    if(layerInfoLen>=2&&stream.Position+2<=lmStart+lmLen){
                        short layerCount=ReadI16(reader);
                        if(layerCount<0)mergedHasAlpha=true;
                    }
                }
                // Seek directly past all layers to Section 5 (Merged Image Data).
                stream.Position=lmStart+lmLen;
                if(stream.Position+2>fs.Length)return null;

                int baseChannels;
                switch(colorMode){
                    case 0:baseChannels=1;break;
                    case 1:baseChannels=1;break;
                    case 2:if(palette==null)return null;baseChannels=1;break;
                    case 3:baseChannels=3;break;
                    case 4:baseChannels=4;break;
                    case 7:baseChannels=Math.Min((int)channels,3);break;
                    case 8:baseChannels=1;break;
                    case 9:baseChannels=3;break;
                    default:return null;
                }
                if(channels<baseChannels)return null;
                bool useAlpha=mergedHasAlpha&&channels>baseChannels&&colorMode!=0&&colorMode!=2;
                int neededPlanes=baseChannels+(useAlpha?1:0);
                int rowBytes=checked((int)(((long)width*depth+7)/8));

                ushort compression=ReadU16(reader);
                if(compression>3)return null;

                var target=ComputeTargetSize(width,height,maxWidth,maxHeight);
                int outW=target.Width,outH=target.Height;
                var srcXMap=new int[outW];
                for(int x=0;x<outW;x++)srcXMap[x]=(int)((long)x*width/outW);
                var srcYMap=new int[outH];
                for(int y=0;y<outH;y++)srcYMap[y]=(int)((long)y*height/outH);

                var bgra=new byte[checked(outW*outH*4)];
                var rowBuf=new byte[rowBytes];

                if(compression==0){
                    long dataStart=stream.Position;
                    long planeBytes=(long)height*rowBytes;
                    for(int p=0;p<neededPlanes;p++){
                        long planeStart=dataStart+p*planeBytes;
                        for(int dstY=0;dstY<outH;dstY++){
                            long rowPos=planeStart+(long)srcYMap[dstY]*rowBytes;
                            if(stream.Position!=rowPos)stream.Position=rowPos;
                            ReadInto(stream,rowBuf,rowBytes);
                            WritePlaneRow(bgra,dstY,outW,srcXMap,rowBuf,p,colorMode,baseChannels,depth,palette,resources.TransparentIndex);
                        }
                    }
                }else if(compression==1){
                    int neededRows=checked(neededPlanes*height);
                    var lengths=new int[neededRows];
                    var offsets=new long[neededRows];
                    long entryBytes=isPsb?4L:2L;
                    long remainingEntries=(long)(channels-neededPlanes)*height;
                    long rel=0;
                    for(int i=0;i<neededRows;i++){
                        uint len=isPsb?ReadU32(reader):ReadU16(reader);
                        if(len>(uint)(rowBytes*2+64))return null;
                        offsets[i]=rel;
                        lengths[i]=(int)len;
                        rel+=len;
                    }
                    if(remainingEntries>0)stream.Seek(remainingEntries*entryBytes,SeekOrigin.Current);
                    long dataStart=stream.Position;
                    var packed=new byte[Math.Max(256,rowBytes*2+64)];
                    for(int p=0;p<neededPlanes;p++){
                        for(int dstY=0;dstY<outH;dstY++){
                            int rowIdx=p*height+srcYMap[dstY];
                            int len=lengths[rowIdx];
                            long rowPos=dataStart+offsets[rowIdx];
                            if(stream.Position!=rowPos)stream.Position=rowPos;
                            if(len>packed.Length)packed=new byte[len];
                            ReadInto(stream,packed,len);
                            DecodePackBitsRow(packed,len,rowBuf,rowBytes);
                            WritePlaneRow(bgra,dstY,outW,srcXMap,rowBuf,p,colorMode,baseChannels,depth,palette,resources.TransparentIndex);
                        }
                    }
                }else{
                    int cmf=stream.ReadByte(),flg=stream.ReadByte();
                    if(cmf<0||flg<0||(cmf&0x0f)!=8)return null;
                    byte[] temp=compression==3&&depth==32?new byte[rowBytes]:null;
                    using(var deflate=new DeflateStream(stream,CompressionMode.Decompress,true)){
                        for(int p=0;p<neededPlanes;p++){
                            int dstY=0;
                            for(int srcY=0;srcY<height;srcY++){
                                ReadInto(deflate,rowBuf,rowBytes);
                                if(dstY<outH&&srcYMap[dstY]==srcY){
                                    if(compression==3)UndoZipPredictionRow(rowBuf,temp,width,depth,rowBytes);
                                    while(dstY<outH&&srcYMap[dstY]==srcY){
                                        WritePlaneRow(bgra,dstY,outW,srcXMap,rowBuf,p,colorMode,baseChannels,depth,palette,resources.TransparentIndex);
                                        dstY++;
                                    }
                                }
                            }
                        }
                    }
                }

                bool hasAlpha=useAlpha||(colorMode==2&&resources.TransparentIndex>=0&&resources.TransparentIndex<256);
                bool whiteMatted=useAlpha&&resources.HasVersionInfo;
                if(whiteMatted){
                    for(int i=0;i<bgra.Length;i+=4){
                        byte a=bgra[i+3];
                        if(a==255)continue;
                        bgra[i]=UnmatteWhite(bgra[i],a);
                        bgra[i+1]=UnmatteWhite(bgra[i+1],a);
                        bgra[i+2]=UnmatteWhite(bgra[i+2],a);
                    }
                }

                var format=hasAlpha?PixelFormat.Format32bppArgb:PixelFormat.Format32bppRgb;
                var bitmap=new Bitmap(outW,outH,format);
                try{
                    var data=bitmap.LockBits(new Rectangle(0,0,outW,outH),ImageLockMode.WriteOnly,format);
                    try{
                        int stride=outW*4;
                        for(int y=0;y<outH;y++)Marshal.Copy(bgra,y*stride,IntPtr.Add(data.Scan0,y*data.Stride),stride);
                    }finally{bitmap.UnlockBits(data);}
                    if(!hasAlpha)Album.OpaqueImages.Add(bitmap,new object());
                    if(outW!=width||outH!=height)Album.SetOriginalSize(bitmap,new Size(width,height));
                    return bitmap;
                }catch{bitmap.Dispose();throw;}
            }
        }catch(InvalidDataException){
            return null;
        }catch(EndOfStreamException){
            return null;
        }catch(OverflowException){
            return null;
        }
    }

    public static Bitmap TryDecodeThumbnail(Func<Stream> open){
        try{
            using(var fs=open())
            using(var stream=new BufferedStream(fs,64*1024))
            using(var reader=new BinaryReader(stream)){
                if(fs.Length<34)return null;
                var sig=reader.ReadBytes(4);
                if(sig.Length!=4||sig[0]!='8'||sig[1]!='B'||sig[2]!='P'||sig[3]!='S')return null;
                ushort version=ReadU16(reader);
                if(version!=1&&version!=2)return null;
                stream.Seek(20,SeekOrigin.Current);
                uint cmLen=ReadU32(reader);
                if(stream.Position+cmLen+4>fs.Length)return null;
                if(cmLen>0)stream.Seek(cmLen,SeekOrigin.Current);
                uint resLen=ReadU32(reader);
                if(stream.Position+resLen>fs.Length)return null;
                var resources=ParseResources(stream,reader,stream.Position+resLen);
                if(resources.ThumbnailOffset<0||resources.ThumbnailLength<=28)return null;
                stream.Position=resources.ThumbnailOffset;
                uint thumbFormat=ReadU32(reader);
                if(thumbFormat!=1)return null;
                stream.Seek(24,SeekOrigin.Current);
                var jpeg=ReadExact(stream,resources.ThumbnailLength-28);
                using(var ms=new MemoryStream(jpeg))
                using(var img=Image.FromStream(ms,true,true)){
                    var bmp=new Bitmap(img);
                    var orig=TryReadSize(open);
                    if(orig.HasValue&&(orig.Value.Width!=bmp.Width||orig.Value.Height!=bmp.Height))Album.SetOriginalSize(bmp,orig.Value);
                    return bmp;
                }
            }
        }catch{return null;}
    }
}
}
