using System.Globalization;
using System.IO;
using System.Text;
using DBCD.Providers;
using MySqlConnector;
namespace AnoWowItemEditor;

public sealed record CatalogResult(List<CatalogItem> Items,int Overrides,string Server,TooltipData Tooltips,Dictionary<int,CatalogItem> Originals,TooltipData OriginalTooltips,HotfixHistory History);
public sealed class CatalogRepository
{
    internal const string Db2Folder = @"C:\AnoCore\AnoCore-Server\data\dbc\enUS";
    internal const string Database = "foreverclassic_hotfixes";
    private const uint SparseHash = 2442913102;
    public async Task<CatalogResult> LoadAsync()
    {
        using var db = await DatabaseConnection.OpenAsync(Database);
        // Obtain the actual TableHash from the installed file, validating the known build convention.
        using (var file = File.OpenRead(Path.Combine(Db2Folder,"ItemSparse.db2")))
        {
            var header=new byte[160]; file.ReadExactly(header);
            if(Encoding.ASCII.GetString(header,0,4)!="WDC5" || BitConverter.ToUInt32(header,152)!=SparseHash)
                throw new InvalidOperationException("Installed ItemSparse DB2 does not match the supported server build.");
        }
        var items = await Task.Run(LoadBaseline);
        var originals=items.ToDictionary(p=>p.Key,p=>new CatalogItem(new(p.Value.Fields,StringComparer.OrdinalIgnoreCase)));
        var sparse=await HotfixReader.LoadAsync(db,"item_sparse",SparseHash);
        var active=sparse.ActiveRows;
        foreach(var (id,row) in active)items[id]=new(HotfixReader.Merge(items.GetValueOrDefault(id)?.Fields,row),true);
        foreach(var registration in sparse.Registrations.Values.Where(r=>r.Status==2))items.Remove(registration.RecordId);
        var identity=await HotfixReader.LoadAsync(db,"item",TooltipData.Hash("Item"));
        foreach(var (id,row) in identity.ActiveRows)if(items.TryGetValue(id,out var item))foreach(string col in new[]{"ClassID","SubclassID"})item.Fields[col]=row[col];
        var catalog=items.Values.Where(i=>i.Text("Display").Length>0 || i.Overridden).OrderBy(i=>i.Name,StringComparer.OrdinalIgnoreCase).ToList();
        var originalTooltips=await TooltipData.LoadAsync(db,originals.Values.Where(i=>i.Text("Display").Length>0).ToList(),false);
        var tooltips=await TooltipData.LoadAsync(db,catalog);
        var history=await HotfixHistory.LoadAsync(db,sparse,originals,items);
        return new(catalog,active.Count,db.DataSource,tooltips,originals,originalTooltips,history);
    }
    private static string Value(object? value) => value is null or DBNull ? "" : Convert.ToString(value,CultureInfo.InvariantCulture) ?? "";
    internal static Dictionary<int,CatalogItem> LoadBaseline()
    {
        var reader = new DBCD.DBCD(new FilesystemDBCProvider(Db2Folder),new GithubDBDProvider(true));
        var sparse=reader.Load("ItemSparse","7.3.5.70450");
        var items=new Dictionary<int,CatalogItem>();
        var aliases=new Dictionary<string,string> { ["Display_lang"]="Display",["Description_lang"]="Description",["Display1_lang"]="Display1",["Display2_lang"]="Display2",["Display3_lang"]="Display3",["Gem_properties"]="GemProperties",["Socket_match_enchantment_ID"]="SocketMatchEnchantmentId",["DamageType"]="DamageDamageType",["OppositeFactionItemID"]="FactionRelated" };
        foreach(var row in sparse.Values)
        {
            var fields=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(string col in sparse.AvailableColumns)
            {
                object? value=row[col];
                if(value is Array array)
                {
                    string name=col=="StatModifier_bonusStat" ? "StatModifierBonusStat" : col;
                    for(int i=0;i<array.Length;i++) fields[name+(i+1)]=Value(array.GetValue(i));
                }
                else fields[aliases.GetValueOrDefault(col) ?? col]=Value(value);
            }
            fields["ID"]=Convert.ToString(row.ID,CultureInfo.InvariantCulture)!;
            items[Convert.ToInt32(row.ID)]=new(fields);
        }
        var identity=reader.Load("Item","7.3.5.70450");
        foreach(var row in identity.Values) {
            int id=Convert.ToInt32(row.ID);
            if(!items.TryGetValue(id,out var item))items[id]=item=new(new() { ["ID"]=id.ToString(CultureInfo.InvariantCulture) });
            item.Fields["ClassID"]=Value(row["ClassID"]);item.Fields["SubclassID"]=Value(row["SubclassID"]);
        }
        return items;
    }
}
