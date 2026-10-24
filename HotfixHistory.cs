using System.Globalization;
using System.Text.Json;
using System.Windows.Media;
using MySqlConnector;
namespace AnoWowItemEditor;

public sealed record RecordedChange(string Id,int ItemId,string RecordedUtc,string Actor,string Action,string Summary,string Provenance,string BeforeJson,string AfterJson);
public sealed record FieldComparison(string Field,string Original,string Modified,bool Changed,bool Known);
public sealed record OverrideEntry(int Id,int Build,bool Active,CatalogItem? OriginalItem,CatalogItem ModifiedItem,string OriginalSource,string Source,string StateReason,List<RecordedChange> Activity)
{
    public string Key=>$"{Id}/{Build}";
    public string Name=>ModifiedItem.Name;
    public Brush NameBrush=>ModifiedItem.NameBrush;
    public string Status=>Active?"Active":"Inactive";
    public string RecordedUtc=>Activity.FirstOrDefault()?.RecordedUtc??"Not recorded";
    public string Attribution=>Activity.Count>0?"Recorded activity available":"Unattributed database record";
    public IReadOnlyList<FieldComparison> Fields { get; }=Compare(OriginalItem,ModifiedItem);
    public string Summary=>OriginalItem is null?"Original unavailable":$"{Fields.Count(f=>f.Changed)} known fields differ";
    internal static bool Equivalent(string? a,string? b)=>decimal.TryParse(a,NumberStyles.Float,CultureInfo.InvariantCulture,out var x)&&decimal.TryParse(b,NumberStyles.Float,CultureInfo.InvariantCulture,out var y)?x==y:string.Equals(a??"",b??"",StringComparison.Ordinal);
    internal static List<FieldComparison> Compare(CatalogItem? original,CatalogItem modified)
    {
        return (original?.Fields.Keys.AsEnumerable()??[]).Union(modified.Fields.Keys,StringComparer.OrdinalIgnoreCase)
            .Where(k=>!k.StartsWith('_')&&k is not "ID" and not "VerifiedBuild")
            .Select(k=> {bool known=original?.Fields.ContainsKey(k)==true;string before=known?original!.Text(k):"[unavailable]";string after=modified.Fields.ContainsKey(k)?modified.Text(k):"[unavailable]";return new FieldComparison(k,before,after,known&&!Equivalent(before,after),known);})
            .OrderByDescending(f=>f.Changed).ThenBy(f=>f.Field).ToList();
    }
}

public sealed class HotfixHistory
{
    public List<OverrideEntry> Entries { get; }=[];
    public List<string> Warnings { get; }=[];
    internal static async Task<bool> HasTable(MySqlConnection db,string schema,string table)
    {
        using var cmd=new MySqlCommand("SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=@schema AND TABLE_NAME=@table",db);
        cmd.Parameters.AddWithValue("@schema",schema);cmd.Parameters.AddWithValue("@table",table);return Convert.ToInt32(await cmd.ExecuteScalarAsync())>0;
    }
    internal static CatalogItem? SnapshotItem(string json,int id,bool original=false)
    {
        using var document=JsonDocument.Parse(json);var root=document.RootElement;
        Dictionary<string,string>? fields=null;
        if(original&&root.TryGetProperty("OriginalItem",out var baseline)&&baseline.ValueKind==JsonValueKind.Object&&baseline.TryGetProperty("ID",out var originalId)&&originalId.ToString()==id.ToString(CultureInfo.InvariantCulture))fields=ReadFields(baseline);
        if(root.TryGetProperty("Tables",out var tables)) {
            if(fields is null&&tables.TryGetProperty("item_sparse",out var sparse)&&sparse.ValueKind==JsonValueKind.Array)
                fields=sparse.EnumerateArray().Where(r=>r.TryGetProperty("ID",out var value)&&value.ToString()==id.ToString(CultureInfo.InvariantCulture)).OrderBy(r=>r.TryGetProperty("VerifiedBuild",out var build)&&build.ToString()=="0"?0:1).Select(ReadFields).FirstOrDefault();
            if(fields is not null&&tables.TryGetProperty("item",out var identity)&&identity.ValueKind==JsonValueKind.Array)
                foreach(var row in identity.EnumerateArray())if(row.TryGetProperty("ID",out var value)&&value.ToString()==id.ToString(CultureInfo.InvariantCulture))foreach(var field in new[]{"ClassID","SubclassID"})if(row.TryGetProperty(field,out var v))fields[field]=v.ToString();
        }
        return fields is not null&&fields.ContainsKey("Display")?new(fields):null;
    }
    private static Dictionary<string,string> ReadFields(JsonElement element)=>element.EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.ValueKind==JsonValueKind.Null?"":p.Value.ToString(),StringComparer.OrdinalIgnoreCase);
    internal static async Task<HotfixHistory> LoadAsync(MySqlConnection db,HotfixTable sparse,Dictionary<int,CatalogItem> originals,Dictionary<int,CatalogItem> current)
    {
        var history=new HotfixHistory();var events=new Dictionary<string,RecordedChange>();
        foreach(var (schema,table) in new[]{(CatalogRepository.Database,"anocore_studio_changes"),("anocore_studio_backup","changes")}) {
            try {
                if(!await HasTable(db,schema,table))continue;
                using var cmd=new MySqlCommand($"SELECT ChangeId,EntityId,ChangedUtc,Actor,Action,Summary,Provenance,BeforeJson,AfterJson FROM `{schema}`.`{table}` WHERE EntityType='Item' ORDER BY ChangedUtc DESC,ChangeId",db);
                using var r=await cmd.ExecuteReaderAsync();while(await r.ReadAsync()) {
                    string id=Convert.ToString(r.GetValue(0),CultureInfo.InvariantCulture)!;if(events.ContainsKey(id))continue;
                    events[id]=new(id,Convert.ToInt32(r[1]),r.GetDateTime(2).ToString("yyyy-MM-dd HH:mm:ss 'UTC'",CultureInfo.InvariantCulture),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),r.GetString(8));
                }
            }catch(MySqlException){history.Warnings.Add($"Recorded activity from {schema}.{table} could not be read.");}
        }
        var activity=events.Values.GroupBy(e=>e.ItemId).ToDictionary(g=>g.Key,g=>g.OrderByDescending(e=>e.RecordedUtc,StringComparer.Ordinal).ThenBy(e=>e.Id).ToList());
        var rows=new List<(Dictionary<string,string> Fields,string Source)>();rows.AddRange(sparse.Rows.Select(r=>(r,"Database override")));
        foreach(var (id,changes) in activity)if(!rows.Any(r=>int.Parse(r.Fields["ID"],CultureInfo.InvariantCulture)==id)) {
            try {
                var retained=changes.Select(e=>SnapshotItem(e.AfterJson,id)).FirstOrDefault(i=>i is not null);
                if(retained is not null)rows.Add((retained.Fields,"Retained activity snapshot"));
            }catch(JsonException){history.Warnings.Add($"A retained snapshot for item {id} could not be decoded.");}
        }
        var active=sparse.ActiveRows;
        foreach(var (row,source) in rows) {
            int id=int.Parse(row["ID"],CultureInfo.InvariantCulture),build=int.TryParse(row.GetValueOrDefault("VerifiedBuild"),out var b)?b:70450;
            var changes=activity.GetValueOrDefault(id)??[];
            var baseline=originals.GetValueOrDefault(id);if(baseline?.Fields.ContainsKey("Display")!=true)baseline=null;
            string baselineSource="Original installed DB2";
            if(baseline is null)foreach(var change in changes.AsEnumerable().Reverse()) {
                try{baseline=SnapshotItem(change.BeforeJson,id,true);}catch(JsonException){history.Warnings.Add($"An original snapshot for item {id} could not be decoded.");}
                if(baseline is not null){baselineSource="Preserved original from recorded activity";break;}
            }
            bool isActive=source=="Database override"&&active.TryGetValue(id,out var effective)&&effective["VerifiedBuild"]==row.GetValueOrDefault("VerifiedBuild");
            var fields=HotfixReader.Merge(baseline?.Fields,row);
            if(current.TryGetValue(id,out var live)){foreach(string key in new[]{"ClassID","SubclassID"})if(live.Fields.TryGetValue(key,out var value))fields[key]=value;}
            else if(originals.TryGetValue(id,out var identity)){foreach(string key in new[]{"ClassID","SubclassID"})if(identity.Fields.TryGetValue(key,out var value))fields[key]=value;}
            var modified=isActive&&current.TryGetValue(id,out var now)?now:new CatalogItem(fields,true);
            string reason=isActive?"Latest registration is valid; this is the selected database payload.":source!="Database override"?"Retained snapshot; no matching database payload is active.":!sparse.Registrations.TryGetValue(id,out var registration)?"No hotfix registration is present.":$"Not selected as an active payload; latest registration status {registration.Status}.";
            history.Entries.Add(new(id,build,isActive,baseline,modified,baseline is null?"Original definition unavailable":baselineSource,source,reason,changes));
        }
        return history;
    }
}
