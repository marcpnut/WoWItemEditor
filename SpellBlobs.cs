using System.Globalization;
using System.IO;
using System.Text;
using MySqlConnector;
namespace AnoWowItemEditor;

internal static class SpellBlobs
{
    internal static uint LayoutHash(){using var f=File.OpenRead(Path.Combine(CatalogRepository.Db2Folder,"Spell.db2"));var h=new byte[160];f.ReadExactly(h);return BitConverter.ToUInt32(h,156);}
    internal static byte[] Encode(Dictionary<string,string> row)=>Encoding.UTF8.GetBytes(string.Join('\0',new[]{"NameSubtext","Description","AuraDescription"}.Select(k=>row.GetValueOrDefault(k)??""))+"\0");
    internal static Dictionary<string,string> Decode(byte[] bytes)
    {
        var parts=new UTF8Encoding(false,true).GetString(bytes).Split('\0');if(parts.Length!=4||parts[^1]!="")throw new InvalidOperationException("Unsupported Spell hotfix text layout.");return new(StringComparer.OrdinalIgnoreCase){["NameSubtext"]=parts[0],["Description"]=parts[1],["AuraDescription"]=parts[2]};
    }
    internal static async Task OverlayAsync(MySqlConnection db,Dictionary<int,Dictionary<string,string>> rows,MySqlTransaction? tx=null)
    {
        uint layout=LayoutHash();
        var active=new Dictionary<int,int>();using(var cmd=new MySqlCommand("SELECT RecordId,Status FROM hotfix_data WHERE TableHash=@hash ORDER BY Id,UniqueId",db,tx)){cmd.Parameters.AddWithValue("@hash",TooltipData.Hash("Spell"));using var r=await cmd.ExecuteReaderAsync();while(await r.ReadAsync())active[Convert.ToInt32(r[0])]=Convert.ToInt32(r[1]);}
        var selected=new HashSet<int>();using var query=new MySqlCommand("SELECT RecordId,`Blob`,LayoutHash FROM hotfix_blob WHERE TableHash=@hash AND locale='enUS' AND VerifiedBuild>=0 ORDER BY (VerifiedBuild=0) DESC,VerifiedBuild DESC",db,tx);query.Parameters.AddWithValue("@hash",TooltipData.Hash("Spell"));using var reader=await query.ExecuteReaderAsync();
        while(await reader.ReadAsync()){int id=Convert.ToInt32(reader[0]);if(active.GetValueOrDefault(id)!=1||!selected.Add(id))continue;if(Convert.ToUInt32(reader[2])!=layout)throw new InvalidOperationException($"Spell {id} text uses a different client layout.");var fields=Decode((byte[])reader[1]);if(!rows.TryGetValue(id,out var row))rows[id]=row=new(StringComparer.OrdinalIgnoreCase){["ID"]=id.ToString(CultureInfo.InvariantCulture)};foreach(var pair in fields)row[pair.Key]=pair.Value;}
    }
}
