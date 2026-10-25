using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Windows.Media.Media3D;
namespace AnoWowItemEditor;

internal sealed record CharacterPose(Matrix4x4[] Bones,Dictionary<int,Matrix3D> Attachments,int Animation,int AnimationFile,int DrivenBones);
internal static class CharacterRig
{
    private static int I(byte[] b,int at){Check(b,at,4);return BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at,4));}
    private static ushort U(byte[] b,int at){Check(b,at,2);return BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at,2));}
    private static short S(byte[] b,int at)=>unchecked((short)U(b,at));
    private static float F(byte[] b,int at){float value=BitConverter.Int32BitsToSingle(I(b,at));if(!float.IsFinite(value))throw new InvalidDataException("Invalid rig coordinate.");return value;}
    private static void Check(byte[] b,int at,int count){if(at<0||count<0||(long)at+count>b.Length)throw new InvalidDataException("Rig track exceeds its asset bounds.");}
    private static (int Count,int Offset) Array(byte[] b,int at,int stride,int max=4096){int n=I(b,at),offset=I(b,at+4);if(n<0||n>max)throw new InvalidDataException("Rig track count exceeds supported bounds.");Check(b,offset,checked(n*stride));return(n,offset);}
    private static Vector3 V(byte[] b,int at)=>new(F(b,at),F(b,at+4),F(b,at+8));
    private static Dictionary<string,byte[]> Chunks(byte[] b)
    {
        if(b.Length>=4&&System.Text.Encoding.ASCII.GetString(b,0,4)=="MD20")return new(){{"MD21",b}};var result=new Dictionary<string,byte[]>();for(int p=0;p<b.Length;){Check(b,p,8);string name=System.Text.Encoding.ASCII.GetString(b,p,4);int size=I(b,p+4);Check(b,p+8,size);result[name]=b.AsSpan(p+8,size).ToArray();p=checked(p+8+size);}return result;
    }
    private static Matrix3D Wpf(Matrix4x4 m)
    {
        // Source (x,y,z) becomes WPF (x,z,-y); conjugate the complete attachment matrix.
        var basis=new Matrix4x4(1,0,0,0,0,0,-1,0,0,1,0,0,0,0,0,1);Matrix4x4.Invert(basis,out var inverse);var a=inverse*m*basis;return new(a.M11,a.M12,a.M13,a.M14,a.M21,a.M22,a.M23,a.M24,a.M31,a.M32,a.M33,a.M34,a.M41,a.M42,a.M43,a.M44);
    }
    internal static async Task<CharacterPose> Stand(ItemModelAssets assets,int file)
    {
        string identity=assets.BuildIdentity;var model=Chunks(await assets.Read(file,identity));var chain=new List<Dictionary<string,byte[]>>();int skeleton=model.TryGetValue("SKID",out var id)?I(id,0):0;var visited=new HashSet<int>();while(skeleton>0&&visited.Add(skeleton)&&chain.Count<8){var part=Chunks(await assets.Read(skeleton,identity));chain.Add(part);skeleton=part.TryGetValue("SKPD",out var parent)&&parent.Length>=12?I(parent,8):0;}chain.Reverse();
        byte[] boneData=model["MD21"],sequenceData=boneData,attachmentData=boneData;int boneHeader=44,sequenceHeader=28,attachmentHeader=240;var animFiles=new Dictionary<(int,int),int>();
        foreach(var part in new[]{model}.Concat(chain)){
            if(part.TryGetValue("SKB1",out var bones)&&I(bones,0)>0){boneData=bones;boneHeader=0;}
            if(part.TryGetValue("SKS1",out var seq)&&I(seq,8)>0){sequenceData=seq;sequenceHeader=8;}
            if(part.TryGetValue("SKA1",out var att)&&I(att,0)>0){attachmentData=att;attachmentHeader=0;}
            if(part.TryGetValue("AFID",out var files))for(int p=0;p+8<=files.Length;p+=8)animFiles[(U(files,p),U(files,p+2))]=I(files,p+4);
        }
        var sequences=Array(sequenceData,sequenceHeader,64);int chosen=Enumerable.Range(0,sequences.Count).FirstOrDefault(n=>U(sequenceData,sequences.Offset+n*64)==0&&U(sequenceData,sequences.Offset+n*64+2)==0,-1);if(chosen<0)throw new InvalidDataException("This character has no standing animation.");
        int original=chosen;for(int n=0;n<32&&(I(sequenceData,sequences.Offset+chosen*64+12)&64)!=0;n++){int next=U(sequenceData,sequences.Offset+chosen*64+62);if(next>=sequences.Count||next==chosen)break;chosen=next;}
        int sequence=sequences.Offset+chosen*64,animation=U(sequenceData,sequence),variation=U(sequenceData,sequence+2),flags=I(sequenceData,sequence+12),animationFile=0;byte[] keys=boneData;
        if((flags&32)==0){if(!animFiles.TryGetValue((animation,variation),out animationFile)||animationFile<=0)throw new InvalidDataException($"Standing animation {animation}/{variation} has no asset mapping.");byte[] raw=await assets.Read(animationFile,identity);try{var chunks=Chunks(raw);keys=chunks.GetValueOrDefault("AFSB")??chunks.GetValueOrDefault("AFM2")??(chunks.Count==1?chunks.Values.First():raw);}catch(InvalidDataException){keys=raw;}}
        var bonesArray=Array(boneData,boneHeader,88);int count=bonesArray.Count,driven=0;var matrices=new Matrix4x4[count];var local=new Matrix4x4[count];var parents=new int[count];
        // Sample the first frame of Stand. Track descriptors live in SKB1; key arrays can live in AFSB.
        (byte[] Data,int At,int Count)? Track(int at,int stride){int source=S(boneData,at+2)>=0?0:chosen;var refs=Array(boneData,at+12,8);if(source>=refs.Count)return null;int header=refs.Offset+source*8;int n=I(boneData,header),offset=I(boneData,header+4);if(n==0)return null;if(n<0||n>100000)throw new InvalidDataException("Animation key count exceeds supported bounds.");byte[] data=S(boneData,at+2)>=0||(flags&32)!=0?boneData:keys;Check(data,offset,checked(stride*n*(U(boneData,at)>=2?3:1)));return(data,offset,n);}
        for(int n=0;n<count;n++){int p=bonesArray.Offset+n*88;parents[n]=S(boneData,p+8);var pivot=V(boneData,p+76);var translation=Track(p+16,12);var rotation=Track(p+36,8);var scale=Track(p+56,12);Vector3 t=translation is {} tr?V(tr.Data,tr.At):Vector3.Zero,s=scale is {} sc?V(sc.Data,sc.At):Vector3.One;var q=System.Numerics.Quaternion.Identity;if(rotation is {} ro){float C(int at){short value=S(ro.Data,at);return (value<0?value+32768:value-32767)/32767f;}q=new(C(ro.At),C(ro.At+2),C(ro.At+4),C(ro.At+6));if(q.LengthSquared()<.0001f)throw new InvalidDataException("Invalid standing-pose quaternion.");q=System.Numerics.Quaternion.Normalize(q);}if(translation is not null||rotation is not null||scale is not null)driven++;local[n]=Matrix4x4.CreateTranslation(-pivot)*Matrix4x4.CreateScale(s)*Matrix4x4.CreateFromQuaternion(q)*Matrix4x4.CreateTranslation(pivot+t);}
        var state=new byte[count];Matrix4x4 Bone(int n){if(n<0||n>=count)return Matrix4x4.Identity;if(state[n]==2)return matrices[n];if(state[n]==1)throw new InvalidDataException("Cyclic character skeleton.");state[n]=1;matrices[n]=local[n]*(parents[n]>=0?Bone(parents[n]):Matrix4x4.Identity);state[n]=2;return matrices[n];}for(int n=0;n<count;n++)Bone(n);
        var attachments=new Dictionary<int,Matrix3D>();var rows=Array(attachmentData,attachmentHeader,40);for(int n=0;n<rows.Count;n++){int p=rows.Offset+n*40;int bone=U(attachmentData,p+4);if(bone>=count)continue;attachments[I(attachmentData,p)]=Wpf(Matrix4x4.CreateTranslation(V(attachmentData,p+8))*matrices[bone]);}
        if(driven==0)throw new InvalidDataException("Standing-pose tracks are unavailable; the bind pose is not used as a substitute.");return new(matrices,attachments,animation,animationFile,driven);
    }
}
