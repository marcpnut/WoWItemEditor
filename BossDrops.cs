using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DBCD.Providers;
using MySqlConnector;
namespace AnoWowItemEditor;
internal sealed record InstanceBoss(int MapId,int CreatureId,int LootId,string Instance,string Name,int Min,int Max,string Difficulty)
{
    public string Key=>$"{MapId}:{CreatureId}";
    public string Label=>$"{Name} · {Difficulty} · Level {Min}–{Max}";
    public override string ToString()=>Label;
}
internal static class BossDrops
{
    internal static async Task<(List<InstanceBoss> Bosses,string Fingerprint)> Load(MySqlConnection db,MySqlTransaction? tx,WorldDropCatalog world)
    {
        var raw=await WorldDrops.Query(db,tx,"SELECT * FROM foreverclassic_world.instance_encounters ORDER BY entry");var variants=await WorldDrops.Query(db,tx,"SELECT entry,difficulty_entry_1,difficulty_entry_2,difficulty_entry_3 FROM foreverclassic_world.creature_template ORDER BY entry");var byVariant=variants.ToDictionary(r=>int.Parse(r["entry"]!));var reader=new DBCD.DBCD(new FilesystemDBCProvider(CatalogRepository.Db2Folder),new GithubDBDProvider(true));var encounters=await Task.Run(()=>TooltipData.LoadTable(reader,"DungeonEncounter"));var creatures=world.Creatures.ToDictionary(c=>c.Entry);var list=new List<InstanceBoss>();
        foreach(var row in raw.Where(r=>r["creditType"]=="0")){
            if(!encounters.TryGetValue(int.Parse(row["entry"]!),out var encounter))continue;int mapId=TooltipData.Number(encounter,"MapID"),npc=int.Parse(row["creditEntry"]!);if(!world.Maps.TryGetValue(mapId,out var map)||TooltipData.Number(map,"InstanceType") is not (1 or 2)||!creatures.TryGetValue(npc,out var creature))continue;string instance=TooltipData.Read(map,"MapName");if(creature.Loot>0)list.Add(new(mapId,npc,creature.Loot,instance,creature.Name,creature.Min,creature.Max,"Base"));if(byVariant.TryGetValue(npc,out var variant))for(int i=1;i<=3;i++)if(int.TryParse(variant["difficulty_entry_"+i],out int entry)&&entry>0&&creatures.TryGetValue(entry,out var alternate)&&alternate.Loot>0)list.Add(new(mapId,entry,alternate.Loot,instance,alternate.Name,alternate.Min,alternate.Max,"Variant "+i));
        }
        var file=new FileInfo(Path.Combine(CatalogRepository.Db2Folder,"DungeonEncounter.db2"));string fp=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(world.Fingerprint+JsonSerializer.Serialize(raw)+JsonSerializer.Serialize(variants)+file.Length+":"+file.LastWriteTimeUtc.Ticks)));return (list.DistinctBy(b=>b.Key).OrderBy(b=>b.Instance).ThenBy(b=>b.Name).ToList(),fp);
    }
    internal static DropPlan Plan(WorldDropCatalog world,CatalogItem item,IEnumerable<string> keys,decimal chance)
    {
        if(chance is <=0 or >100)throw new InvalidOperationException("Boss drop chance must be greater than 0 and no more than 100%.");var selected=keys.Distinct().Select(key=>world.Bosses.SingleOrDefault(b=>b.Key==key)??throw new InvalidOperationException("A selected instance boss no longer has a verified loot table.")).ToList();if(selected.Count==0)throw new InvalidOperationException("Select at least one boss.");var selectedNpcs=selected.Select(b=>b.CreatureId).ToHashSet();foreach(var boss in selected){var shared=world.Creatures.Where(c=>c.Loot==boss.LootId&&!selectedNpcs.Contains(c.Entry)).ToList();if(shared.Count>0)throw new InvalidOperationException($"{boss.Name} shares its loot table with {string.Join(", ",shared.Select(c=>c.Name))}. Select all affected bosses or choose an independent loot table.");}
        return new(selected.Min(b=>b.Min),selected.Max(b=>b.Max),chance,selected.Select(b=>new DropTarget(b.CreatureId,b.Name,b.Min,b.Max,b.LootId,b.Instance+" · "+b.Difficulty)).ToList(),world.BossFingerprint,"Boss",selected.Select(b=>b.Key).ToArray());
    }
}
internal static class ItemDropEditing
{
    internal static async Task<ItemSnapshot> Capture(MySqlConnection db,MySqlTransaction? tx,int id,bool locked=false){var item=await HistoryActions.CaptureAsync(db,tx,id,locked);foreach(var p in await WorldDrops.Capture(db,tx,new([id]),locked))item.Tables[p.Key]=p.Value;return item;}
    internal static async Task<ItemSnapshot> Load(int id){using var db=await DatabaseConnection.OpenAsync(CatalogRepository.Database);await ContentEditing.InitializeAsync(db);return await Capture(db,null,id);}
    internal static Task Save(CatalogItem item,ItemSnapshot expected,DropPlan plan)=>ContentEditing.Transaction(async(db,tx)=>{await SaveCore(db,tx,item,expected,plan);return true;});
    internal static async Task<string> SaveCore(MySqlConnection db,MySqlTransaction tx,CatalogItem item,ItemSnapshot expected,DropPlan plan)
    {
        var scope=new ContentScope("ItemDrops",item.Id,[],[],[],[],Drops:new([item.Id]));var before=await Capture(db,tx,item.Id,true);if(HistoryActions.Canonical(before)!=HistoryActions.Canonical(expected))throw new InvalidOperationException("Conflict: item or loot changed after loading. Reopen drop locations.");var world=await WorldDropCatalog.LoadAsync(db,tx);if(plan.InputFingerprint!=(plan.Mode=="Boss"?world.BossFingerprint:world.Fingerprint))throw new InvalidOperationException("Boss/world definitions changed. Refresh the drop preview.");var session=await ItemEditing.LoadAsync(item.Id,db,tx);ItemEditing.Validate(session,session.Current);var fresh=WorldDrops.Replan(world,session.Current,plan);await WorldDrops.Apply(db,tx,item.Id,fresh,item.Name);before=before with{OriginalItem=new(session.Current.Fields,StringComparer.OrdinalIgnoreCase)};var after=(await Capture(db,tx,item.Id,true)) with{OriginalItem=new(session.Current.Fields,StringComparer.OrdinalIgnoreCase)};string op=Guid.NewGuid().ToString();using var cmd=new MySqlCommand($"INSERT INTO {ContentEditing.Operations}(OperationId,Kind,EntityId,Name,IsActive,IsHidden,Version,BeforeJson,AfterJson,ExpectedJson,ScopeJson,Fingerprint,RecordedUtc) VALUES(@op,'ItemDrops',@id,@name,1,0,1,@before,@after,@after,@scope,@fp,UTC_TIMESTAMP(6))",db,tx);cmd.Parameters.AddWithValue("@op",op);cmd.Parameters.AddWithValue("@id",item.Id);cmd.Parameters.AddWithValue("@name",item.Name);cmd.Parameters.AddWithValue("@before",HistoryActions.Serialize(before));cmd.Parameters.AddWithValue("@after",HistoryActions.Serialize(after));cmd.Parameters.AddWithValue("@scope",JsonSerializer.Serialize(scope));cmd.Parameters.AddWithValue("@fp",ContentEditing.Fingerprint());await cmd.ExecuteNonQueryAsync();await ContentEditing.Audit(db,tx,scope,"Assign item drops",item.Name,op,before,after);return op;
    }
}
