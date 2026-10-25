using System.Globalization;
using MySqlConnector;
namespace WoWItemEditor;

internal sealed record HotfixRegistration(long Id,int RecordId,int Status,int Build);
internal sealed record HotfixTable(List<Dictionary<string,string>> Rows,Dictionary<int,HotfixRegistration> Registrations)
{
    internal Dictionary<int,Dictionary<string,string>> ActiveRows=>Rows.GroupBy(r=>int.Parse(r["ID"],CultureInfo.InvariantCulture))
        .Where(g=>Registrations.TryGetValue(g.Key,out var registration)&&registration.Status==1)
        .ToDictionary(g=>g.Key,g=>g.OrderBy(r=>int.Parse(r["VerifiedBuild"],CultureInfo.InvariantCulture)==0?0:1).ThenByDescending(r=>int.Parse(r["VerifiedBuild"],CultureInfo.InvariantCulture)).First());
}

internal static class HotfixReader
{
    // Payload VerifiedBuild is a verification provenance, not a join key to a hotfix push's build.
    // Registrations are keyed by table hash + record ID and processed in push-ID order.
    internal static async Task<HotfixTable> LoadAsync(MySqlConnection db,string table,uint hash,MySqlTransaction? tx=null)
    {
        var rows=new List<Dictionary<string,string>>();
        using(var cmd=new MySqlCommand($"SELECT * FROM `{table}` WHERE VerifiedBuild>=0 ORDER BY ID,VerifiedBuild DESC",db,tx))
        using(var r=await cmd.ExecuteReaderAsync())while(await r.ReadAsync()) {
            var row=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            for(int n=0;n<r.FieldCount;n++)row[r.GetName(n)]=r.IsDBNull(n)?"":Convert.ToString(r.GetValue(n),CultureInfo.InvariantCulture)??"";
            rows.Add(row);
        }
        var registrations=new Dictionary<int,HotfixRegistration>();
        using(var cmd=new MySqlCommand("SELECT Id,RecordId,Status,VerifiedBuild FROM hotfix_data WHERE TableHash=@hash ORDER BY Id,UniqueId",db,tx)) {
            cmd.Parameters.AddWithValue("@hash",hash);using var r=await cmd.ExecuteReaderAsync();
            while(await r.ReadAsync()){int id=Convert.ToInt32(r[1]);registrations[id]=new(Convert.ToInt64(r[0]),id,Convert.ToInt32(r[2]),Convert.ToInt32(r[3]));}
        }
        return new(rows,registrations);
    }
    internal static Dictionary<string,string> Merge(IReadOnlyDictionary<string,string>? original,IReadOnlyDictionary<string,string> row)
    {
        var merged=original is null?new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase):new(original,StringComparer.OrdinalIgnoreCase);
        foreach(var pair in row)merged[pair.Key]=pair.Value;
        return merged;
    }
}
