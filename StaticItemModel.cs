using System.Buffers.Binary;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
namespace AnoWowItemEditor;

internal sealed record StaticItemMesh(Model3DGroup Model,int Triangles,int Textures,string Note);
internal static class StaticItemModel
{
    private readonly record struct ArrayRef(int Count,int Offset);
    private static int I(byte[] b,int p){Range(b,p,4);return BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(p,4));}
    private static ushort U(byte[] b,int p){Range(b,p,2);return BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(p,2));}
    private static float F(byte[] b,int p){float value=BitConverter.Int32BitsToSingle(I(b,p));if(!float.IsFinite(value))throw new InvalidDataException("Non-finite model coordinate.");return value;}
    private static void Range(byte[] b,int offset,int length){if(offset<0||length<0||(long)offset+length>b.Length)throw new InvalidDataException("Model array exceeds its asset bounds.");}
    private static ArrayRef Array(byte[] b,int position,int stride,int limit=2000000){int count=I(b,position),offset=I(b,position+4);if(count<0||count>limit)throw new InvalidDataException($"Model array at {position} has count {count}; supported maximum {limit}.");Range(b,offset,checked(count*stride));return new(count,offset);}
    private static Dictionary<string,byte[]> Chunks(byte[] bytes)
    {
        var result=new Dictionary<string,byte[]>();if(bytes.Length>=4&&System.Text.Encoding.ASCII.GetString(bytes,0,4)=="MD20"){result["MD21"]=bytes;return result;}
        for(int p=0;p<bytes.Length;){Range(bytes,p,8);string name=System.Text.Encoding.ASCII.GetString(bytes,p,4);int size=I(bytes,p+4);Range(bytes,p+8,size);if(name is "MD21" or "SFID" or "TXID" or "SKID" or "SKA1" or "SKB1" or "SKPD")result[name]=bytes.AsSpan(p+8,size).ToArray();p=checked(p+8+size);}return result;
    }
    internal static async Task<Dictionary<int,Point3D>> Attachments(ItemModelAssets assets,int file)
    {
        string identity=assets.BuildIdentity;var chunks=Chunks(await assets.Read(file,identity));var result=new Dictionary<int,Point3D>();var bones=new Dictionary<int,Point3D>();var anchors=new Dictionary<int,int>();
        void Bones(byte[] bytes,int offset){var array=Array(bytes,offset,88,4096);for(int n=0;n<array.Count;n++){int p=array.Offset+n*88+76;bones[n]=new(F(bytes,p),F(bytes,p+8),-F(bytes,p+4));}}
        void Read(byte[] bytes,int offset){var array=Array(bytes,offset,40,1024);for(int n=0;n<array.Count;n++){int p=array.Offset+n*40;result[I(bytes,p)]=new(F(bytes,p+8),F(bytes,p+16),-F(bytes,p+12));anchors[I(bytes,p)]=U(bytes,p+4);}}
        if(chunks.TryGetValue("MD21",out var mesh)&&mesh.Length>=52&&I(mesh,44)>0)Bones(mesh,44);
        if(chunks.TryGetValue("MD21",out var body)&&body.Length>=248&&I(body,240)>0)Read(body,240);
        if(chunks.TryGetValue("SKID",out var skel)&&skel.Length>=4){int id=I(skel,0);bool found=false;for(int depth=0;depth<4&&id>0;depth++){var parts=Chunks(await assets.Read(id,identity));if(parts.TryGetValue("SKA1",out var att)&&!found){Read(att,0);found=true;}if(parts.TryGetValue("SKB1",out var bone))Bones(bone,0);id=parts.TryGetValue("SKPD",out var parent)&&parent.Length>=12?I(parent,8):0;}}
        foreach(int id in result.Keys.ToArray())if(((Vector3D)result[id]).LengthSquared<1e-10&&bones.TryGetValue(anchors[id],out var pivot))result[id]=pivot;
        return result;
    }
    internal static async Task<object> Describe(ItemModelAssets assets,int file)
    {
        var chunks=Chunks(await assets.Read(file));var model=chunks["MD21"];var skin=await assets.Read(I(chunks["SFID"],0));var subs=Array(skin,28,48,65536);var tex=Array(model,80,16,1024);
        return new{geosets=Enumerable.Range(0,subs.Count).Select(n=>U(skin,subs.Offset+n*48)).ToArray(),textureTypes=Enumerable.Range(0,tex.Count).Select(n=>I(model,tex.Offset+n*16)).ToArray(),attachments=await Attachments(assets,file)};
    }
    internal static async Task<StaticItemMesh> Load(ItemModelAssets assets,ItemModelPart part,int? overrideTexture=null,string? identity=null,IReadOnlyDictionary<int,BitmapSource>? typeTextures=null,Func<int,bool>? geosetVisible=null)
    {
        identity??=assets.BuildIdentity;var chunks=Chunks(await assets.Read(part.Model,identity));if(!chunks.TryGetValue("MD21",out var model)||model.Length<136||System.Text.Encoding.ASCII.GetString(model,0,4)!="MD20")throw new InvalidDataException("This item model has no supported MD20/MD21 mesh payload.");
        if(!chunks.TryGetValue("SFID",out var skinIds)||skinIds.Length<4)throw new InvalidDataException("This model uses a legacy named skin. Only file-ID based item meshes are supported in this version.");
        var skin=await assets.Read(I(skinIds,0),identity);if(skin.Length<48||System.Text.Encoding.ASCII.GetString(skin,0,4)!="SKIN")throw new InvalidDataException("Invalid model skin mesh.");
        ArrayRef vertices=Array(model,60,48,500000),textureDefs=Array(model,80,16,1024),materials=Array(model,112,4,1024),lookup=Array(model,128,2,65536);ArrayRef indices=Array(skin,4,2),triangles=Array(skin,12,2),submeshes=Array(skin,28,48,65536),units=Array(skin,36,24,65536);
        var bitmaps=new Dictionary<int,BitmapSource>();var slotImages=new Dictionary<int,BitmapSource>();var textureSlots=new Dictionary<int,int>();var notes=new HashSet<string>();
        for(int n=0;n<textureDefs.Count;n++){
            int type=I(model,textureDefs.Offset+n*16),id=0;if(typeTextures?.TryGetValue(type,out var supplied)==true){slotImages[n]=supplied;continue;}
            if(type==0&&chunks.TryGetValue("TXID",out var ids)&&ids.Length>=(n+1)*4)id=I(ids,n*4);
            else if(type==2)id=overrideTexture??part.Textures.FirstOrDefault();
            if(id>0){textureSlots[n]=id;if(!bitmaps.ContainsKey(id))bitmaps[id]=ModelTexture.Decode(await assets.Read(id,identity));slotImages[n]=bitmaps[id];}
        }
        var positions=new Point3DCollection(vertices.Count);var normals=new Vector3DCollection(vertices.Count);var uv=new PointCollection(vertices.Count);for(int n=0;n<vertices.Count;n++){int p=vertices.Offset+n*48;positions.Add(new(F(model,p),F(model,p+8),-F(model,p+4)));normals.Add(new(F(model,p+20),F(model,p+28),-F(model,p+24)));uv.Add(new(F(model,p+32),F(model,p+36)));}positions.Freeze();normals.Freeze();uv.Freeze();
        var result=new Model3DGroup();int count=0;var seen=new HashSet<int>();
        for(int n=0;n<units.Count;n++){
            int unit=units.Offset+n*24,mesh=U(skin,unit+4),layer=U(skin,unit+12),combo=U(skin,unit+16),material=U(skin,unit+10);if(mesh>=submeshes.Count||material>=materials.Count||combo>=lookup.Count)throw new InvalidDataException("Invalid submesh/material reference.");
            if(geosetVisible is not null&&!geosetVisible(U(skin,submeshes.Offset+mesh*48)))continue;if(!seen.Add(mesh)){notes.Add("Additional shader passes are not simulated.");continue;}if(layer!=0)notes.Add("Layered shader appearance is approximated by its base texture.");if(U(skin,unit+14)>1)notes.Add("Multi-texture shader effects are not simulated.");int slot=U(model,lookup.Offset+combo*2);
            if(!slotImages.TryGetValue(slot,out var image)){notes.Add($"Some geometry has an unresolved texture type (slot {slot}); it is omitted rather than shown untextured.");continue;}
            int sub=submeshes.Offset+mesh*48,start=U(skin,sub+8)+(U(skin,sub+2)<<16),length=U(skin,sub+10);if(length%3!=0||start<0||(long)start+length>triangles.Count)throw new InvalidDataException("Invalid skin triangle range.");
            var faces=new Int32Collection(length);for(int t=start;t<start+length;t++){int index=U(skin,triangles.Offset+t*2);if(index>=indices.Count)throw new InvalidDataException("Invalid triangle index.");int vertex=U(skin,indices.Offset+index*2);if(vertex>=vertices.Count)throw new InvalidDataException("Invalid vertex reference.");faces.Add(vertex);}faces.Freeze();
            var geometry=new MeshGeometry3D{Positions=positions,Normals=normals,TextureCoordinates=uv,TriangleIndices=faces};var brush=new ImageBrush(image){ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,1,1),TileMode=TileMode.Tile,Stretch=Stretch.Fill};Material surface=(U(model,materials.Offset+material*4)&1)!=0?new EmissiveMaterial(brush):new DiffuseMaterial(brush);var piece=new GeometryModel3D(geometry,surface);if((U(model,materials.Offset+material*4)&4)!=0)piece.BackMaterial=surface;result.Children.Add(piece);count+=length/3;
            int blend=U(model,materials.Offset+material*4+2);if(blend>1)notes.Add("Additive/glow material blending is approximated.");
        }
        if(result.Children.Count==0)throw new InvalidDataException("No textured geometry could be resolved for this item. No substitute model is shown.");result.Freeze();return new(result,count,slotImages.Values.Distinct().Count(),string.Join(" ",notes));
    }
}
