using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DBCD.Providers;
using MySqlConnector;
namespace AnoWowItemEditor;

internal sealed record DropCreature(int Entry,string Name,int Min,int Max,int Loot,int Rank,ulong NpcFlags,int Type,uint Extra,bool Civilian,bool Scaling);
internal sealed record DropSpawn(ulong Guid,int Entry,int Map,int Zone,double X,double Y,string Script);
internal sealed record DropTarget(int Entry,string Name,int Min,int Max,int Loot,string Zones);
internal sealed record DropPlan(int Min,int Max,decimal Chance,List<DropTarget> Targets,string InputFingerprint,string Mode="World",string[]? BossKeys=null)
{
    public string Summary=>Mode=="Boss"?$"Boss loot only · {Chance:0.####}% per selected boss · {Targets.Count} bosses":$"Creature levels {Min}–{Max} · {Chance:0.####}% per piece · {Targets.Count} creature types · {Targets.Select(t=>t.Loot).Distinct().Count()} loot tables";
}
internal sealed record WorldDropScope(int[] Items);
internal sealed class WorldDropCatalog
{
    internal List<DropCreature> Creatures=[];
    internal List<DropSpawn> Spawns=[];
    internal Dictionary<int,Dictionary<string,string>> Areas=[];
    internal Dictionary<int,Dictionary<string,string>> Maps=[];
    private readonly Dictionary<string,(ushort Area,ushort[]? Cells)> tiles=[];
    private readonly Dictionary<string,string> files=[];
    private Dictionary<ulong,int> zones=[];
    private Dictionary<int,int> zoneLevel=[];
    internal string Fingerprint="";
    internal string BossFingerprint="";
    internal List<InstanceBoss> Bosses=[];
    internal const string MapFolder=@"C:\AnoCore\AnoCore-Server\data\maps";
    internal static async Task<WorldDropCatalog> LoadAsync(MySqlConnection? connection=null,MySqlTransaction? tx=null)
    {
        using var owned=connection is null?await DatabaseConnection.OpenAsync("foreverclassic_world"):null;var db=connection??owned!;var result=new WorldDropCatalog();var reader=new DBCD.DBCD(new FilesystemDBCProvider(CatalogRepository.Db2Folder),new GithubDBDProvider(true));
        result.Areas=await Task.Run(()=>TooltipData.LoadTable(reader,"AreaTable"));result.Maps=await Task.Run(()=>TooltipData.LoadTable(reader,"Map"));
        var raw=new Dictionary<string,List<Dictionary<string,string?>>>();
        raw["templates"]=await WorldDrops.Query(db,tx,"SELECT entry,name,minlevel,maxlevel,lootid,rank,npcflag,type,flags_extra,Civilian,ScriptName FROM foreverclassic_world.creature_template ORDER BY entry");
        raw["spawns"]=await WorldDrops.Query(db,tx,"SELECT guid,id,map,zoneId,position_x,position_y,ScriptName FROM foreverclassic_world.creature ORDER BY guid");
        raw["scaling"]=await WorldDrops.Query(db,tx,"SELECT * FROM foreverclassic_world.creature_template_scaling ORDER BY Entry,DifficultyID");
        var scaling=raw["scaling"].Where(r=>N(r,"ContentTuningID")!=0||N(r,"LevelScalingDeltaMin")!=0||N(r,"LevelScalingDeltaMax")!=0).Select(r=>N(r,"Entry")).ToHashSet();
        foreach(var r in raw["templates"])result.Creatures.Add(new(N(r,"entry"),r["name"]??"",N(r,"minlevel"),N(r,"maxlevel"),N(r,"lootid"),N(r,"rank"),ulong.Parse(r["npcflag"]!),N(r,"type"),uint.Parse(r["flags_extra"]!),r["Civilian"]=="1"||r["Civilian"]=="True",scaling.Contains(N(r,"entry"))||!string.IsNullOrWhiteSpace(r["ScriptName"])));
        foreach(var r in raw["spawns"])result.Spawns.Add(new(ulong.Parse(r["guid"]!),N(r,"id"),N(r,"map"),N(r,"zoneId"),double.Parse(r["position_x"]!,CultureInfo.InvariantCulture),double.Parse(r["position_y"]!,CultureInfo.InvariantCulture),r["ScriptName"]??""));
        await Task.Run(()=>{
            foreach(var spawn in result.Spawns)result.zones[spawn.Guid]=result.Zone(spawn);
            var normal=result.Creatures.Where(c=>c.Rank==0&&c.NpcFlags==0&&c.Type is >=1 and <=7&&!c.Civilian&&c.Min>0&&c.Max<=63).ToDictionary(c=>c.Entry);
            foreach(var group in result.Spawns.Where(s=>result.zones[s.Guid]>0&&normal.ContainsKey(s.Entry)).GroupBy(s=>result.zones[s.Guid])){var levels=group.Select(s=>normal[s.Entry].Min).Order().ToArray();if(levels.Length>=5)result.zoneLevel[group.Key]=levels[(int)((levels.Length-1)*.75)];}
        });
        var hashes=raw.Select(r=>r.Key+JsonSerializer.Serialize(r.Value)).Concat(result.files.OrderBy(p=>p.Key).Select(p=>p.Key+":"+p.Value)).Concat(new[]{"Map","AreaTable"}.Select(n=>{var f=new FileInfo(Path.Combine(CatalogRepository.Db2Folder,n+".db2"));return n+f.Length+":"+f.LastWriteTimeUtc.Ticks;}));result.Fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',hashes))));var boss=await BossDrops.Load(db,tx,result);result.Bosses=boss.Bosses;result.BossFingerprint=boss.Fingerprint;return result;
    }
    private static int N(Dictionary<string,string?> r,string key)=>int.TryParse(r.GetValueOrDefault(key),out int n)?n:0;
    internal int TopZone(int area){var visited=new HashSet<int>();while(area>0&&Areas.TryGetValue(area,out var row)&&visited.Add(area)){int parent=TooltipData.Number(row,"ParentAreaID");if(parent==0)return area;area=parent;}return 0;}
    internal int Zone(DropSpawn spawn)
    {
        if(spawn.Zone>0)return TopZone(spawn.Zone);
        int gx=(int)(32-spawn.X/533.3333333333334),gy=(int)(32-spawn.Y/533.3333333333334);if(gx is <0 or >63||gy is <0 or >63)return 0;string name=$"{spawn.Map:D4}_{gx:D2}_{gy:D2}.map";
        if(!tiles.TryGetValue(name,out var tile)){
            string path=Path.Combine(MapFolder,name);try{using var file=File.OpenRead(path);var header=new byte[44];file.ReadExactly(header);if(Encoding.ASCII.GetString(header,0,4)!="MAPS"||BitConverter.ToUInt32(header,4)!=10)return 0;uint offset=BitConverter.ToUInt32(header,12),size=BitConverter.ToUInt32(header,16);if(size<8||offset>file.Length-8)return 0;file.Position=offset;var area=new byte[8];file.ReadExactly(area);if(Encoding.ASCII.GetString(area,0,4)!="AREA")return 0;ushort flags=BitConverter.ToUInt16(area,4),grid=BitConverter.ToUInt16(area,6);ushort[]? cells=null;if((flags&1)==0){if(size<520)return 0;var bytes=new byte[512];file.ReadExactly(bytes);cells=Enumerable.Range(0,256).Select(i=>BitConverter.ToUInt16(bytes,i*2)).ToArray();}tile=(grid,cells);var info=new FileInfo(path);files[name]=$"{info.Length}:{info.LastWriteTimeUtc.Ticks}";tiles[name]=tile;}catch(IOException){files[name]="missing";tiles[name]=(0,null);return 0;}
        }
        int lx=(int)(16*(32-spawn.X/533.3333333333334))&15,ly=(int)(16*(32-spawn.Y/533.3333333333334))&15;return TopZone(tile.Cells?[lx*16+ly]??tile.Area);
    }
    internal static (int Min,int Max) Band(CatalogItem item){int target=Math.Clamp(Math.Max(item.RequiredLevel,(int)Math.Round(item.ItemLevel*.75,MidpointRounding.AwayFromZero)),1,60);return (Math.Max(1,target-5),Math.Min(60,target+5));}
    internal DropPlan Plan(CatalogItem item,decimal chance)
    {
        if(chance is <=0 or >1)throw new InvalidOperationException("World-drop chance must be greater than 0 and no more than 1% per piece.");var band=Band(item);var spawns=Spawns.ToLookup(s=>s.Entry);
        bool Eligible(DropCreature c){if(c.Rank!=0||c.NpcFlags!=0||c.Civilian||c.Type is <1 or >7||c.Scaling||c.Loot<=0||c.Min<band.Min||c.Max>band.Max||c.Extra!=0)return false;var places=spawns[c.Entry].ToArray();if(places.Length==0)return false;return places.All(s=>Maps.TryGetValue(s.Map,out var map)&&TooltipData.Number(map,"InstanceType")==0&&TooltipData.Number(map,"ExpansionID")==0&&string.IsNullOrWhiteSpace(s.Script)&&zones.GetValueOrDefault(s.Guid)>0&&zoneLevel.GetValueOrDefault(zones[s.Guid])>=band.Min);}
        var eligible=Creatures.Where(Eligible).Select(c=>c.Entry).ToHashSet();var lootUsers=Creatures.Where(c=>c.Loot>0).ToLookup(c=>c.Loot);var targets=Creatures.Where(c=>eligible.Contains(c.Entry)&&lootUsers[c.Loot].All(user=>eligible.Contains(user.Entry))).Select(c=>new DropTarget(c.Entry,c.Name,c.Min,c.Max,c.Loot,string.Join(", ",spawns[c.Entry].Select(s=>zones[s.Guid]).Distinct().Order().Select(z=>Areas.TryGetValue(z,out var a)?TooltipData.Read(a,"AreaName"):z.ToString())))).OrderBy(t=>t.Zones).ThenBy(t=>t.Name).ToList();return new(band.Min,band.Max,chance,targets,Fingerprint);
    }
}
internal static class WorldDrops
{
    internal const string Metadata="anocore_studio_backup.item_editor_world_drops";
    internal static string Ids(IEnumerable<int> values)=>string.Join(',',values.Distinct().DefaultIfEmpty(-1));
    internal static async Task<List<Dictionary<string,string?>>> Query(MySqlConnection db,MySqlTransaction? tx,string sql)
    {
        using var cmd=new MySqlCommand(sql,db,tx);using var r=await cmd.ExecuteReaderAsync();var rows=new List<Dictionary<string,string?>>();while(await r.ReadAsync()){var row=new Dictionary<string,string?>(StringComparer.OrdinalIgnoreCase);for(int n=0;n<r.FieldCount;n++)row[r.GetName(n)]=r.IsDBNull(n)?null:r.GetValue(n) is bool flag?(flag?"1":"0"):r.GetValue(n) is DateTime date?date.ToString("yyyy-MM-dd HH:mm:ss.ffffff",CultureInfo.InvariantCulture):Convert.ToString(r.GetValue(n),CultureInfo.InvariantCulture);rows.Add(row);}return rows;
    }
    internal static async Task Initialize(MySqlConnection db)
    {
        using var create=new MySqlCommand($"CREATE TABLE IF NOT EXISTS {Metadata}(ItemId INT NOT NULL PRIMARY KEY,Chance DECIMAL(9,6) NOT NULL,PlanJson LONGTEXT NOT NULL,UpdatedUtc DATETIME(6) NOT NULL) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",db);await create.ExecuteNonQueryAsync();
    }
    internal static async Task<Dictionary<string,List<Dictionary<string,string?>>>> Capture(MySqlConnection db,MySqlTransaction? tx,WorldDropScope scope,bool locked=false)
    {
        string ids=Ids(scope.Items),suffix=locked?" FOR UPDATE":"";return new(){["world_loot"]=await Query(db,tx,$"SELECT * FROM foreverclassic_world.creature_loot_template WHERE Item IN({ids}) AND Reference=0 ORDER BY Entry,Item"+suffix),["world_drop_policy"]=await Query(db,tx,$"SELECT * FROM {Metadata} WHERE ItemId IN({ids}) ORDER BY ItemId"+suffix)};
    }
    internal static async Task Apply(MySqlConnection db,MySqlTransaction tx,int item,DropPlan plan,string name)
    {
        if(plan.Targets.Count==0)throw new InvalidOperationException($"No verified world-drop creatures fit {name} at levels {plan.Min}–{plan.Max}.");using(var engine=new MySqlCommand("SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA='foreverclassic_world' AND TABLE_NAME='creature_loot_template'",db,tx))if(Convert.ToString(await engine.ExecuteScalarAsync())!="InnoDB")throw new InvalidOperationException("Creature loot must support transactions.");
        using(var delete=new MySqlCommand("DELETE FROM foreverclassic_world.creature_loot_template WHERE Item=@id AND Reference=0",db,tx)){delete.Parameters.AddWithValue("@id",item);await delete.ExecuteNonQueryAsync();}
        foreach(int loot in plan.Targets.Select(t=>t.Loot).Distinct()){using var cmd=new MySqlCommand("INSERT INTO foreverclassic_world.creature_loot_template(Entry,Item,Reference,Chance,QuestRequired,LootMode,GroupId,MinCount,MaxCount,Comment) VALUES(@entry,@item,0,@chance,0,1,0,1,1,@comment)",db,tx);cmd.Parameters.AddWithValue("@entry",loot);cmd.Parameters.AddWithValue("@item",item);cmd.Parameters.AddWithValue("@chance",plan.Chance);cmd.Parameters.AddWithValue("@comment",("AnoWoW set creator: "+name)[..Math.Min(255,("AnoWoW set creator: "+name).Length)]);await cmd.ExecuteNonQueryAsync();}
        using var policy=new MySqlCommand($"INSERT INTO {Metadata}(ItemId,Chance,PlanJson,UpdatedUtc) VALUES(@id,@chance,@plan,UTC_TIMESTAMP(6)) ON DUPLICATE KEY UPDATE Chance=VALUES(Chance),PlanJson=VALUES(PlanJson),UpdatedUtc=VALUES(UpdatedUtc)",db,tx);policy.Parameters.AddWithValue("@id",item);policy.Parameters.AddWithValue("@chance",plan.Chance);policy.Parameters.AddWithValue("@plan",JsonSerializer.Serialize(plan));await policy.ExecuteNonQueryAsync();
    }
    internal static DropPlan Replan(WorldDropCatalog world,CatalogItem item,DropPlan plan)=>plan.Mode=="Boss"?BossDrops.Plan(world,item,plan.BossKeys??[],plan.Chance):world.Plan(item,plan.Chance);
    internal static async Task Restore(MySqlConnection db,MySqlTransaction tx,WorldDropScope scope,ItemSnapshot target)
    {
        if(target.Tables["world_loot"].Count>0&&target.Tables["world_drop_policy"].Count>0){var expected=target.Tables["world_drop_policy"].Select(r=>JsonSerializer.Deserialize<DropPlan>(r["PlanJson"]!)!).ToArray();var world=await WorldDropCatalog.LoadAsync(db,tx);if(expected.Length==0||expected.Any(p=>p.InputFingerprint!=(p.Mode=="Boss"?world.BossFingerprint:world.Fingerprint)))throw new InvalidOperationException("World-drop eligibility changed after this snapshot. Automatic loot restoration is blocked; review the current world data.");}
        string ids=Ids(scope.Items);using(var cmd=new MySqlCommand($"DELETE FROM foreverclassic_world.creature_loot_template WHERE Item IN({ids}) AND Reference=0",db,tx))await cmd.ExecuteNonQueryAsync();using(var cmd=new MySqlCommand($"DELETE FROM {Metadata} WHERE ItemId IN({ids})",db,tx))await cmd.ExecuteNonQueryAsync();foreach(var r in target.Tables["world_loot"])await BonusSpells.WriteAsync(db,tx,"foreverclassic_world.creature_loot_template",r);foreach(var r in target.Tables["world_drop_policy"]){using var cmd=new MySqlCommand($"INSERT INTO {Metadata}(ItemId,Chance,PlanJson,UpdatedUtc) VALUES(@id,@c,@p,@utc)",db,tx);cmd.Parameters.AddWithValue("@id",r["ItemId"]);cmd.Parameters.AddWithValue("@c",r["Chance"]);cmd.Parameters.AddWithValue("@p",r["PlanJson"]);cmd.Parameters.AddWithValue("@utc",r["UpdatedUtc"]);await cmd.ExecuteNonQueryAsync();}
    }
}
