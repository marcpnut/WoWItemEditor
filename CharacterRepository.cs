using System.Globalization;
using System.Windows.Media;
using MySqlConnector;
namespace AnoWowItemEditor;

public sealed record CharacterInfo(ulong Guid,string Name,int Level,int ClassId,bool Online,int Specialization)
{
    public override string ToString()=>$"{Name} · Level {Level}"+(Online?" · Online":"");
}
public sealed record OwnedItem(ulong Guid,int Entry,int Count,ulong Bag,int Slot,string Location,int Durability,string Enchantments,string BonusLists,int FixedLevel,int Context,bool Equipped,int RandomProperty=0,int[]? Gems=null);
public sealed record CharacterSnapshot(CharacterInfo Character,List<OwnedItem> Items,Dictionary<int,int>? Skills=null);
public sealed class CharacterItemRow
{
    public OwnedItem Instance { get; }
    public CatalogItem Item { get; }
    public CharacterItemRow(OwnedItem instance,CatalogItem item){Instance=instance;Item=item;}
    public string Name=>Item.Name+(Item.Fields.ContainsKey("Display")?"":" [definition unavailable]");
    public Brush NameBrush=>Item.NameBrush;
    public string Location=>Instance.Location;
    public string SubtypeName=>Item.SubtypeName;
    public int Count=>Instance.Count;
    public int Id=>Item.Id;
    public int? RequiredLevel=>Item.Fields.ContainsKey("RequiredLevel")?Item.RequiredLevel:null;
}
public static class CharacterRepository
{
    internal const string Database="foreverclassic_characters";
    public static async Task<List<CharacterInfo>> ListAsync()
    {
        using var db=await DatabaseConnection.OpenAsync(Database);
        using var cmd=new MySqlCommand("SELECT guid,name,level,class,online,primarySpecialization FROM characters WHERE deleteDate IS NULL OR deleteDate=0 ORDER BY name,guid",db);
        using var r=await cmd.ExecuteReaderAsync();var list=new List<CharacterInfo>();
        while(await r.ReadAsync())list.Add(new(Convert.ToUInt64(r[0]),r.GetString(1),Convert.ToInt32(r[2]),Convert.ToInt32(r[3]),Convert.ToBoolean(r[4]),Convert.ToInt32(r[5])));
        return list;
    }
    public static async Task<CharacterSnapshot> LoadAsync(CharacterInfo character)
    {
        using var db=await DatabaseConnection.OpenAsync(Database);
        using var tx=await db.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var list=new List<OwnedItem>();
        // Read every owned instance, including items outside inventory (mail, void storage below).
        const string sql="SELECT i.guid,i.itemEntry,i.count,ci.bag,ci.slot,i.durability,i.enchantments,i.bonusListIDs,COALESCE(m.fixedScalingLevel,0),i.context,mi.mail_id,i.randomPropertyId FROM item_instance i LEFT JOIN character_inventory ci ON ci.item=i.guid AND ci.guid=@guid LEFT JOIN item_instance_modifiers m ON m.itemGuid=i.guid LEFT JOIN mail_items mi ON mi.item_guid=i.guid AND mi.receiver=@guid WHERE i.owner_guid=@guid OR ci.guid=@guid OR mi.receiver=@guid ORDER BY COALESCE(ci.bag,0),ci.slot,i.guid";
        using(var cmd=new MySqlCommand(sql,db,tx)) {
            cmd.Parameters.AddWithValue("@guid",character.Guid);
            using var r=await cmd.ExecuteReaderAsync();
            var seen=new HashSet<ulong>();
            while(await r.ReadAsync()) {
                ulong guid=Convert.ToUInt64(r[0]);if(!seen.Add(guid))continue;
                bool inventory=!r.IsDBNull(4);ulong bag=inventory?Convert.ToUInt64(r[3]):0;int slot=inventory?Convert.ToInt32(r[4]):-1;
                string location=inventory?(bag==0?(slot<19?"Equipped":"Bags / bank"):$"Bag {bag}")+" · slot "+slot:!r.IsDBNull(10)?"Mail":"Other owned item";
                list.Add(new(guid,Convert.ToInt32(r[1]),Convert.ToInt32(r[2]),bag,slot,location,Convert.ToInt32(r[5]),Convert.ToString(r[6],CultureInfo.InvariantCulture)??"",Convert.ToString(r[7],CultureInfo.InvariantCulture)??"",Convert.ToInt32(r[8]),Convert.ToInt32(r[9]),inventory&&bag==0&&slot<19,Convert.ToInt32(r[11])));
            }
        }
        using(var cmd=new MySqlCommand("SELECT itemId,itemEntry,slot,bonusListIDs,fixedScalingLevel,context,randomProperty FROM character_void_storage WHERE playerGuid=@guid",db,tx)) {
            cmd.Parameters.AddWithValue("@guid",character.Guid);using var r=await cmd.ExecuteReaderAsync();
            while(await r.ReadAsync())list.Add(new(Convert.ToUInt64(r[0]),Convert.ToInt32(r[1]),1,0,Convert.ToInt32(r[2]),"Void storage · slot "+Convert.ToString(r[2]),0,"",Convert.ToString(r[3])??"",Convert.ToInt32(r[4]),Convert.ToInt32(r[5]),false,Convert.ToInt32(r[6])));
        }
        var skills=new Dictionary<int,int>();
        using(var cmd=new MySqlCommand("SELECT skill,value FROM character_skills WHERE guid=@guid",db,tx)) {
            cmd.Parameters.AddWithValue("@guid",character.Guid);using var r=await cmd.ExecuteReaderAsync();while(await r.ReadAsync())skills[Convert.ToInt32(r[0])]=Convert.ToInt32(r[1]);
        }
        var gems=new Dictionary<ulong,int[]>();
        using(var cmd=new MySqlCommand("SELECT g.itemGuid,g.gemItemId1,g.gemItemId2,g.gemItemId3 FROM item_instance_gems g JOIN item_instance i ON i.guid=g.itemGuid WHERE i.owner_guid=@guid",db,tx)) {
            cmd.Parameters.AddWithValue("@guid",character.Guid);using var r=await cmd.ExecuteReaderAsync();while(await r.ReadAsync())gems[Convert.ToUInt64(r[0])]=new[]{Convert.ToInt32(r[1]),Convert.ToInt32(r[2]),Convert.ToInt32(r[3])};
        }
        for(int i=0;i<list.Count;i++)if(!list[i].Location.StartsWith("Void")&&gems.TryGetValue(list[i].Guid,out var sockets))list[i]=list[i] with {Gems=sockets};
        await tx.CommitAsync();
        return new(character,list,skills);
    }
}
