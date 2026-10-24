using System.Buffers.Binary;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace AnoWowItemEditor;

// BLP2 first-mip decoder. Blocks follow the published DXT1/3/5 layouts.
internal static class ModelTexture
{
    internal static BitmapSource Decode(byte[] data)
    {
        if(data.Length<148||System.Text.Encoding.ASCII.GetString(data,0,4)!="BLP2")throw new InvalidDataException("This texture is not a supported BLP2 image.");
        int width=I(data,12),height=I(data,16),offset=I(data,20),length=I(data,84);if(width is <1 or >4096||height is <1 or >4096||offset<148||length<0||(long)offset+length>data.Length)throw new InvalidDataException("Invalid BLP image dimensions or mip range.");
        byte[] pixels=new byte[checked(width*height*4)];int encoding=data[8],depth=data[9],alpha=data[10];var mip=data.AsSpan(offset,length);
        if(encoding==1){int count=checked(width*height);if(data.Length<1172||mip.Length<count)throw new InvalidDataException("Truncated BLP palette or indices.");int alphaBytes=(count*depth+7)/8;if(depth is not (0 or 1 or 4 or 8)||mip.Length<count+alphaBytes)throw new InvalidDataException("Unsupported/truncated palette alpha.");for(int n=0;n<count;n++){int p=148+mip[n]*4;pixels[n*4]=data[p];pixels[n*4+1]=data[p+1];pixels[n*4+2]=data[p+2];pixels[n*4+3]=depth switch{0=>255,1=>(byte)(((mip[count+n/8]>>(n%8))&1)*255),4=>(byte)(((mip[count+n/2]>>((n%2)*4))&15)*17),_=>(byte)mip[count+n]};}}
        else if(encoding==3){if(mip.Length<pixels.Length)throw new InvalidDataException("Truncated BGRA image.");mip[..pixels.Length].CopyTo(pixels);}
        else if(encoding==2){int block=alpha==0?8:alpha is 1 or 7?16:throw new InvalidDataException($"Unsupported BLP compression variant {alpha}.");int nx=(width+3)/4,ny=(height+3)/4;if((long)nx*ny*block>mip.Length)throw new InvalidDataException("Truncated DXT blocks.");for(int by=0;by<ny;by++)for(int bx=0;bx<nx;bx++)Block(mip.Slice((by*nx+bx)*block,block),pixels,width,height,bx*4,by*4,alpha,depth);}
        else throw new InvalidDataException($"BLP encoding {encoding} is not supported.");
        var bitmap=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);bitmap.Freeze();return bitmap;
    }
    private static int I(byte[] b,int p)=>BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(p,4));
    private static void Block(ReadOnlySpan<byte> b,byte[] output,int width,int height,int x0,int y0,int mode,int depth)
    {
        int colorOffset=mode==0?0:8;ushort c0=BinaryPrimitives.ReadUInt16LittleEndian(b[colorOffset..]),c1=BinaryPrimitives.ReadUInt16LittleEndian(b[(colorOffset+2)..]);Span<byte> colors=stackalloc byte[16];Color(c0,colors);Color(c1,colors[4..]);
        bool four=c0>c1||mode!=0;for(int k=0;k<3;k++){colors[8+k]=(byte)(four?(2*colors[k]+colors[4+k])/3:(colors[k]+colors[4+k])/2);colors[12+k]=four?(byte)((colors[k]+2*colors[4+k])/3):(byte)0;}colors[11]=255;colors[15]=(byte)(four||depth==0?255:0);
        uint indices=BinaryPrimitives.ReadUInt32LittleEndian(b[(colorOffset+4)..]);Span<byte> alphas=stackalloc byte[8];ulong alphaBits=0;
        if(mode==7){alphas[0]=b[0];alphas[1]=b[1];if(b[0]>b[1])for(int n=1;n<=6;n++)alphas[n+1]=(byte)(((7-n)*b[0]+n*b[1])/7);else{for(int n=1;n<=4;n++)alphas[n+1]=(byte)(((5-n)*b[0]+n*b[1])/5);alphas[6]=0;alphas[7]=255;}for(int n=0;n<6;n++)alphaBits|=(ulong)b[n+2]<<(8*n);}
        for(int n=0;n<16;n++){int x=x0+n%4,y=y0+n/4;if(x>=width||y>=height)continue;int pixel=(y*width+x)*4,index=(int)((indices>>(n*2))&3)*4;output[pixel]=colors[index];output[pixel+1]=colors[index+1];output[pixel+2]=colors[index+2];output[pixel+3]=mode switch{1=>(byte)(((b[n/2]>>((n%2)*4))&15)*17),7=>alphas[(int)((alphaBits>>(n*3))&7)],_=>colors[index+3]};}
    }
    private static void Color(ushort v,Span<byte> c){int r=(v>>11)&31,g=(v>>5)&63,b=v&31;c[0]=(byte)((b<<3)|(b>>2));c[1]=(byte)((g<<2)|(g>>4));c[2]=(byte)((r<<3)|(r>>2));c[3]=255;}
}
