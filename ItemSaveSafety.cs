using DBCD.Providers;
using MySqlConnector;
namespace AnoWowItemEditor;

internal static class ItemSaveSafety
{
    internal static async Task ValidateSnapshot(MySqlConnection db,MySqlTransaction tx,ItemSnapshot snapshot,int? nativeFallback=null)
    {
        var definitions=new List<(ItemSnapshot Snapshot,int Id)>();
        if(snapshot.Tables.TryGetValue("item_sparse",out var sparse))foreach(int id in sparse.Select(r=>int.Parse(r["ID"]!)).Distinct())definitions.Add((snapshot,id));
        if(nativeFallback.HasValue&&(!snapshot.Tables.TryGetValue("item_sparse",out var rows)||rows.Count==0))definitions.Add((snapshot,nativeFallback.Value));
        foreach(var key in snapshot.Tables.Keys.Where(k=>k.StartsWith("piece_")&&k.EndsWith("_item_sparse"))){string prefix=key[..^"item_sparse".Length];var part=new ItemSnapshot(snapshot.Tables.Where(p=>p.Key.StartsWith(prefix)).ToDictionary(p=>p.Key[prefix.Length..],p=>p.Value));foreach(int id in part.Tables["item_sparse"].Select(r=>int.Parse(r["ID"]!)).Distinct())definitions.Add((part,id));}
        if(definitions.Count==0)return;
        var native=await Task.Run(CatalogRepository.LoadBaseline);var calculations=await ItemCalculations.LoadAsync(db,new DBCD.DBCD(new FilesystemDBCProvider(CatalogRepository.Db2Folder),new GithubDBDProvider(true)),true,tx);
        foreach(var (part,id) in definitions){var effective=HotfixHistory.SnapshotItem(HistoryActions.Serialize(part),id);var fields=HotfixReader.Merge(native.GetValueOrDefault(id)?.Fields,effective?.Fields??new());if(!fields.ContainsKey("Display"))throw new InvalidOperationException($"Safety check cannot resolve item {id}. Restoration is blocked.");var item=new CatalogItem(fields);var problems=calculations.ServerProblems(item);if(problems.Count>0)throw new InvalidOperationException($"Restoration blocked for {item.Name} ({id}): "+string.Join(" ",problems)+" No unsafe item snapshot was applied.");}
    }
}
