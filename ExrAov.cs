using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace LumaView {
// Reads the flat FLOAT channels used by Arnold Cryptomatte and depth AOVs.
// Ordinary color EXRs continue through the general-purpose decoder.
static class ExrAov {
    sealed class Channel {
        public string Name;
        public int Type, XSampling, YSampling;
    }
    sealed class Block {
        public int X,Y,Width,Height,RawLength;
        public byte[] Packed;
    }
    static string Name(BinaryReader reader){
        var bytes=new List<byte>();
        byte next;
        while((next=reader.ReadByte())!=0){
            if(bytes.Count==255)throw new InvalidDataException("Invalid EXR field name.");
            bytes.Add(next);
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
    static byte[] Read(BinaryReader reader,int length){
        var bytes=reader.ReadBytes(length);
        if(bytes.Length!=length)throw new EndOfStreamException("Incomplete EXR block.");
        return bytes;
    }
    static byte[] DecodeBlock(byte[] packed,int length,int compression){
        if(packed.Length==length)return packed;
        if(compression==0||packed.Length<6||packed.Length>length||(packed[0]&15)!=8)
            throw new InvalidDataException("Invalid EXR ZIP block.");
        var predicted=new byte[length];
        using(var input=new MemoryStream(packed,2,packed.Length-6,false))
        using(var zip=new DeflateStream(input,CompressionMode.Decompress)){
            int offset=0;
            while(offset<length){
                int count=zip.Read(predicted,offset,length-offset);
                if(count==0)throw new InvalidDataException("Incomplete EXR ZIP block.");
                offset+=count;
            }
            if(zip.ReadByte()!=-1)throw new InvalidDataException("EXR ZIP block is too large.");
        }
        for(int i=1;i<length;i++)predicted[i]=unchecked((byte)(predicted[i-1]+predicted[i]-128));
        var pixels=new byte[length];
        int even=0,odd=(length+1)/2;
        for(int i=0;i<length;i++)pixels[i]=(i&1)==0?predicted[even++]:predicted[odd++];
        return pixels;
    }
    static byte Byte(double value){return (byte)Math.Max(0,Math.Min(255,Math.Round(value)));}
    static uint ColorHash(uint id){
        unchecked{
            id^=id>>16;id*=0x85ebca6b;id^=id>>13;id*=0xc2b2ae35;return id^(id>>16);
        }
    }
    internal static Bitmap TryDecode(string path,int maxWidth,int maxHeight){
        using(var stream=new BufferedStream(File.OpenRead(path),128*1024))
        using(var reader=new BinaryReader(stream)){
            if(stream.Length<8||reader.ReadUInt32()!=20000630)return null;
            uint version=reader.ReadUInt32();
            if((version&255)!=2||(version&~0x6ffu)!=0)return null;
            bool tiled=(version&0x200)!=0;
            var channels=new List<Channel>();
            var cryptoLayers=new List<string>();
            int xMin=0,yMin=0,xMax=-1,yMax=-1,compression=-1,tileWidth=0,tileHeight=0;
            while(true){
                if(stream.Position>8*1024*1024)throw new InvalidDataException("EXR header exceeds the preview limit.");
                string key=Name(reader);
                if(key.Length==0)break;
                string type=Name(reader);
                int length=reader.ReadInt32();
                if(length<0||length>8*1024*1024||stream.Position+length>stream.Length)
                    throw new InvalidDataException("Invalid EXR header size.");
                if(key=="channels"&&type=="chlist"){
                    using(var list=new BinaryReader(new MemoryStream(Read(reader,length)))){
                        string channel;
                        while((channel=Name(list)).Length>0){
                            var item=new Channel{Name=channel,Type=list.ReadInt32()};
                            list.ReadInt32();
                            item.XSampling=list.ReadInt32();item.YSampling=list.ReadInt32();
                            channels.Add(item);
                            if(channels.Count>256)return null;
                        }
                    }
                }else if(key=="dataWindow"&&length==16){
                    xMin=reader.ReadInt32();yMin=reader.ReadInt32();xMax=reader.ReadInt32();yMax=reader.ReadInt32();
                }else if(key=="compression"&&length==1)compression=reader.ReadByte();
                else if(key=="tiles"&&length==9){
                    tileWidth=reader.ReadInt32();tileHeight=reader.ReadInt32();
                    if((reader.ReadByte()&15)!=0)return null;
                }else if(key.StartsWith("cryptomatte/",StringComparison.Ordinal)&&key.EndsWith("/name",StringComparison.Ordinal)&&length<=255){
                    cryptoLayers.Add(Encoding.UTF8.GetString(Read(reader,length)));
                }else stream.Seek(length,SeekOrigin.Current);
            }
            string stem=Path.GetFileNameWithoutExtension(path);
            bool namedDepth=stem.EndsWith("_Z",StringComparison.OrdinalIgnoreCase);
            bool hasBeauty=channels.Any(c=>c.Name=="R"||c.Name=="G"||c.Name=="B");
            int depth=(!hasBeauty||namedDepth)?channels.FindIndex(c=>c.Name=="Z"):-1;
            if(depth<0&&channels.Count==1&&channels[0].Name=="Y"&&namedDepth)depth=0;
            string layer=cryptoLayers.FirstOrDefault(n=>n=="crypto_material")??cryptoLayers.FirstOrDefault();
            var pairs=new List<int[]>();
            if(layer!=null&&(!hasBeauty||stem.IndexOf(layer,StringComparison.OrdinalIgnoreCase)>=0)){
                for(int level=0;level<100;level++){
                    string prefix=layer+level.ToString("D2",System.Globalization.CultureInfo.InvariantCulture)+".";
                    foreach(string pair in new[]{"RG","BA"}){
                        int id=channels.FindIndex(c=>c.Name==prefix+pair[0]);
                        int weight=channels.FindIndex(c=>c.Name==prefix+pair[1]);
                        if(id>=0&&weight>=0)pairs.Add(new[]{id,weight});
                    }
                }
            }
            bool crypto=pairs.Count>0;
            if(!crypto&&depth<0)return null;
            if((compression!=0&&compression!=2&&compression!=3)||
                channels.Any(c=>(c.Type<0||c.Type>2)||c.XSampling!=1||c.YSampling!=1)||
                (crypto?pairs.Any(p=>channels[p[0]].Type!=2||channels[p[1]].Type!=2):channels[depth].Type!=2))
                return null;
            long wide=(long)xMax-xMin+1,high=(long)yMax-yMin+1;
            if(wide<=0||high<=0||wide>32000||high>32000||wide*high>32000000)
                throw new InvalidDataException("EXR AOV dimensions exceed the preview limit.");
            int width=(int)wide,height=(int)high;
            if(!tiled){tileWidth=width;tileHeight=compression==3?16:1;}
            if(tileWidth<=0||tileHeight<=0||tileWidth>32000||tileHeight>32000)
                throw new InvalidDataException("Invalid EXR tile dimensions.");
            int columns=(width+tileWidth-1)/tileWidth, rows=(height+tileHeight-1)/tileHeight;
            int blockCount=checked(columns*rows);
            if(blockCount>1000000||stream.Position+blockCount*8L>stream.Length)
                throw new InvalidDataException("EXR tile table exceeds the preview limit.");
            var offsets=new long[blockCount];
            for(int i=0;i<blockCount;i++)offsets[i]=reader.ReadInt64();
            long dataStart=stream.Position;
            var sizes=channels.Select(c=>c.Type==1?2:4).ToArray();
            var channelOffsets=new int[sizes.Length];int bytesPerPixel=0;
            for(int i=0;i<sizes.Length;i++){channelOffsets[i]=bytesPerPixel;bytesPerPixel+=sizes[i];}
            var colors=new int[width*height];
            var distances=crypto?null:new float[colors.Length];
            double closest=Double.PositiveInfinity;
            var blocks=ReadBlocks(reader,offsets,dataStart,tiled,yMin,width,height,tileWidth,tileHeight,bytesPerPixel);
            Action<Block> render=delegate(Block block){
                int x=block.X,y=block.Y,w=block.Width,h=block.Height;
                var data=DecodeBlock(block.Packed,block.RawLength,compression);
                double blockClosest=Double.PositiveInfinity;
                for(int yy=0;yy<h;yy++){
                    int line=yy*w*bytesPerPixel;
                    for(int xx=0;xx<w;xx++){
                        int pixel=(y+yy)*width+x+xx, sample=line+xx*4;
                        if(crypto){
                            double red=0,green=0,blue=0;
                            foreach(var pair in pairs){
                                float weight=BitConverter.ToSingle(data,sample+channelOffsets[pair[1]]*w);
                                if(!(weight>0&&weight<=1))continue;
                                uint id=BitConverter.ToUInt32(data,sample+channelOffsets[pair[0]]*w);
                                if(id==0)continue;
                                uint color=ColorHash(id);
                                red+=weight*(5+(color&255)%48);
                                green+=weight*(5+((color>>8)&255)%48);
                                blue+=weight*(5+((color>>16)&255)%48);
                            }
                            colors[pixel]=unchecked((int)0xff000000)|(Byte(red)<<16)|(Byte(green)<<8)|Byte(blue);
                        }else{
                            float distance=BitConverter.ToSingle(data,sample+channelOffsets[depth]*w);
                            // Arnold's empty background is written near 1e8.
                            if(distance>0&&distance<1e7){distances[pixel]=distance;blockClosest=Math.Min(blockClosest,distance);}
                        }
                    }
                }
                if(!Double.IsPositiveInfinity(blockClosest))lock(distances){closest=Math.Min(closest,blockClosest);}
            };
            // Each worker owns one validated, non-overlapping block. Keep
            // large blocks serial to bound temporary decompression buffers.
            long blockBytes=(long)tileWidth*tileHeight*bytesPerPixel;
            int workers=(int)Math.Min(Math.Min(Environment.ProcessorCount,4),Math.Max(1,8L*1024*1024/blockBytes));
            if(workers==1||blockCount<4){
                foreach(var block in blocks)render(block);
            }else{
                try{
                    Parallel.ForEach(Partitioner.Create(blocks,EnumerablePartitionerOptions.NoBuffering),
                        new ParallelOptions{MaxDegreeOfParallelism=workers},render);
                }catch(AggregateException ex){
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.GetBaseException()).Throw();
                }
            }
            if(!crypto){
                double far=closest*3.5;
                for(int i=0;i<colors.Length;i++){
                    int gray=distances[i]>0?Byte(255*(1-distances[i]/far)):0;
                    colors[i]=unchecked((int)0xff000000)|(gray*0x010101);
                }
            }
            return ToBitmap(colors,width,height,maxWidth,maxHeight);
        }
    }
    static IEnumerable<Block> ReadBlocks(BinaryReader reader,long[] offsets,long dataStart,bool tiled,int yMin,
        int width,int height,int tileWidth,int tileHeight,int bytesPerPixel){
        var stream=reader.BaseStream;
        int columns=(width+tileWidth-1)/tileWidth,rows=(height+tileHeight-1)/tileHeight;
        var visited=new bool[offsets.Length];
        // Coordinates in each chunk determine its destination. Reading in
        // physical order retains read-ahead across adjacent chunks on SMB.
        Array.Sort(offsets);
        foreach(long offset in offsets){
            if(offset<dataStart||offset>stream.Length-(tiled?20:8))throw new InvalidDataException("Invalid EXR tile offset.");
            if(stream.Position!=offset)stream.Position=offset;
            int tx=tiled?reader.ReadInt32():0,ty;
            if(tiled){
                ty=reader.ReadInt32();
                if(reader.ReadInt32()!=0||reader.ReadInt32()!=0)throw new InvalidDataException("Unexpected EXR tile level.");
            }else{
                long scanline=(long)reader.ReadInt32()-yMin;
                if(scanline<0||scanline>=height||scanline%tileHeight!=0)throw new InvalidDataException("Invalid EXR scanline.");
                ty=(int)(scanline/tileHeight);
            }
            if(tx<0||tx>=columns||ty<0||ty>=rows||visited[ty*columns+tx])
                throw new InvalidDataException("Invalid or duplicate EXR tile.");
            visited[ty*columns+tx]=true;
            int x=tx*tileWidth,y=ty*tileHeight;
            int w=Math.Min(tileWidth,width-x),h=Math.Min(tileHeight,height-y);
            int packedLength=reader.ReadInt32();
            long unpacked=(long)w*h*bytesPerPixel;
            if(unpacked>64*1024*1024||packedLength<=0||packedLength>unpacked||stream.Position+packedLength>stream.Length)
                throw new InvalidDataException("EXR tile exceeds the preview limit.");
            yield return new Block{X=x,Y=y,Width=w,Height=h,RawLength=(int)unpacked,Packed=Read(reader,packedLength)};
        }
    }
    static Bitmap ToBitmap(int[] pixels,int width,int height,int maxWidth,int maxHeight){
        var image=new Bitmap(width,height,PixelFormat.Format32bppRgb);
        try{
            var data=image.LockBits(new Rectangle(0,0,width,height),ImageLockMode.WriteOnly,PixelFormat.Format32bppRgb);
            try{
                for(int y=0;y<height;y++)Marshal.Copy(pixels,y*width,IntPtr.Add(data.Scan0,y*data.Stride),width);
            }finally{image.UnlockBits(data);}
            if(maxWidth<=0||maxHeight<=0||(width<=maxWidth&&height<=maxHeight))return image;
            double scale=Math.Min((double)maxWidth/width,(double)maxHeight/height);
            var resized=new Bitmap(image,Math.Max(1,(int)(width*scale)),Math.Max(1,(int)(height*scale)));
            image.Dispose();
            return resized;
        }catch{image.Dispose();throw;}
    }
}
}
