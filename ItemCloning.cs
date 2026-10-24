using System.Globalization;
using System.IO;
using System.Text.Json;
using DBCD.Providers;
using MySqlConnector;
namespace AnoWowItemEditor;

internal sealed record CloneScope(Dictionary<string,int[]> Records,int[] EffectIds,string Fingerprint,bool IncludeVoid=false);
internal sealed record CloneBlueprint(ItemEditSession Item,Dictionary<string,List<Dictionary<string,string>>> Rows,CloneScope SourceScope,ItemSnapshot SourceExpected);
internal sealed record CloneResult(int Id,string Operation,CloneScope? Scope=null,ItemSnapshot? Before=null,ItemSnapshot? After=null);
internal static class ItemCloning
{
    internal static readonly Dictionary<string,string> Tables=new(){["item_sparse"]="ItemSparse",["item"]="Item",["item_x_item_effect"]="ItemXItemEffect",["item_modified_appearance"]="ItemModifiedAppearance"};
    internal static string Fingerprint()=>string.Join('/',Tables.Values.Append("ItemEffect").Select(n=>{var f=new FileInfo(Path.Combine(CatalogRepository.Db2Folder,n+".db2"));return $"{n}:{f.Length}:{f.LastWriteTimeUtc.Ticks}";}));
    private static string Ids(IEnumerable<int> ids)=>string.Join(',',ids.Distinct().DefaultIfEmpty(-1));
    private static async Task<List<Dictionary<string,string?>>> Rows(MySqlConnection db,MySqlTransaction? tx,string sql)
    {
        using var cmd=new MySqlCommand(sql,db,tx);using var r=await cmd.ExecuteReaderAsync();var rows=new List<Dictionary<string,string?>>();while(await r.ReadAsync()){var row=new Dictionary<string,string?>(StringComparer.OrdinalIgnoreCase);for(int n=0;n<r.FieldCount;n++)row[r.GetName(n)]=r.IsDBNull(n)?null:Convert.ToString(r.GetValue(n),CultureInfo.InvariantCulture);rows.Add(row);}return rows;
    }
    internal static async Task<CloneBlueprint> LoadAsync(int id)
    {
        string fingerprint=Fingerprint();using var db=await DatabaseConnection.OpenAsync(CatalogRepository.Database);using var tx=await db.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);var item=await ItemEditing.LoadAsync(id,db,tx);var reader=new DBCD.DBCD(new FilesystemDBCProvider(CatalogRepository.Db2Folder),new GithubDBDProvider(true));
        var rows=new Dictionary<string,List<Dictionary<string,string>>>();foreach(string table in new[]{"item_x_item_effect","item_modified_appearance"}){var native=await Task.Run(()=>TooltipData.LoadTable(reader,Tables[table]));await TooltipData.Overlay(db,table,Tables[table],native,tx);rows[table]=native.Values.Where(r=>TooltipData.Number(r,"ItemID")==id).ToList();}
        var effects=rows["item_x_item_effect"].Select(r=>TooltipData.Number(r,"ItemEffectID")).ToArray();var scope=new CloneScope(new(){["item_sparse"]=[id],["item"]=[id],["item_x_item_effect"]=rows["item_x_item_effect"].Select(r=>TooltipData.Number(r,"ID")).ToArray(),["item_modified_appearance"]=rows["item_modified_appearance"].Select(r=>TooltipData.Number(r,"ID")).ToArray()},effects,fingerprint,true);
        var expected=await CaptureAsync(db,tx,id,scope);await tx.CommitAsync();if(fingerprint!=Fingerprint())throw new InvalidOperationException("Source definitions changed while loading. Reopen duplicate.");return new(item,rows,scope,expected);
    }
    internal static async Task<ItemSnapshot> CaptureAsync(MySqlConnection db,MySqlTransaction? tx,int id,CloneScope scope,bool locked=false)
    {
        string suffix=locked?" FOR UPDATE":"";var tables=new Dictionary<string,List<Dictionary<string,string?>>>();var hashes=new List<string>();foreach(var (table,dbc) in Tables){string predicate=table is "item" or "item_sparse"?$"ID={id}":$"ItemID={id} OR ID IN({Ids(scope.Records[table])})";tables[table]=await Rows(db,tx,$"SELECT * FROM {table} WHERE {predicate} ORDER BY ID,VerifiedBuild"+suffix);hashes.Add($"(TableHash={TooltipData.Hash(dbc)} AND RecordId IN({Ids(scope.Records[table].Concat(tables[table].Select(r=>int.Parse(r["ID"]!))))}))");}
        tables["item_effect"]=await Rows(db,tx,$"SELECT * FROM item_effect WHERE ID IN({Ids(scope.EffectIds)}) ORDER BY ID,VerifiedBuild"+suffix);hashes.Add($"(TableHash={TooltipData.Hash("ItemEffect")} AND RecordId IN({Ids(scope.EffectIds)}))");
        tables["hotfix_data"]=await Rows(db,tx,$"SELECT * FROM hotfix_data WHERE {string.Join(" OR ",hashes)} ORDER BY Id,UniqueId"+suffix);
        tables["owned_instances"]=await Rows(db,tx,$"SELECT guid,itemEntry FROM foreverclassic_characters.item_instance WHERE itemEntry={id} ORDER BY guid"+suffix);
        if(scope.IncludeVoid)tables["void_instances"]=await Rows(db,tx,$"SELECT itemId,playerGuid,itemEntry FROM foreverclassic_characters.character_void_storage WHERE itemEntry={id} ORDER BY itemId"+suffix);
        return new(tables);
    }
    private static int NativeMax(string dbc){using var f=File.OpenRead(Path.Combine(CatalogRepository.Db2Folder,dbc+".db2"));var h=new byte[168];f.ReadExactly(h);return checked((int)BitConverter.ToUInt32(h,164));}
    private static async Task<int> Next(MySqlConnection db,MySqlTransaction tx,string table,string dbc)
    {
        using var cmd=new MySqlCommand($"SELECT GREATEST(COALESCE((SELECT MAX(ID) FROM {table}),0),COALESCE((SELECT MAX(RecordId) FROM hotfix_data WHERE TableHash={TooltipData.Hash(dbc)}),0))",db,tx);return checked((int)Math.Max(NativeMax(dbc),Convert.ToInt64(await cmd.ExecuteScalarAsync()))+1);
    }
    internal static Task<CloneResult> SaveAsync(CloneBlueprint source,CatalogItem draft)=>ContentEditing.Transaction((db,tx)=>SaveCoreAsync(db,tx,source,draft));
    internal static async Task<CloneResult> SaveCoreAsync(MySqlConnection db,MySqlTransaction tx,CloneBlueprint source,CatalogItem draft,bool recordOperation=true)
    {
        ItemEditing.Validate(source.Item,draft);var fresh=await ItemCalculations.LoadAsync(db,new DBCD.DBCD(new FilesystemDBCProvider(CatalogRepository.Db2Folder),new GithubDBDProvider(true)),true,tx);var unsafeRows=fresh.ServerProblems(draft);if(unsafeRows.Count>0)throw new InvalidOperationException("Copy blocked: "+string.Join(" ",unsafeRows));if(source.SourceScope.Fingerprint!=Fingerprint()||HistoryActions.Canonical(await CaptureAsync(db,tx,source.Item.Current.Id,source.SourceScope,true))!=HistoryActions.Canonical(source.SourceExpected))throw new InvalidOperationException("Source item, appearances or effects changed. Reload it before duplicating.");
        using(var engine=new MySqlCommand("SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='item_modified_appearance'",db,tx))if(Convert.ToString(await engine.ExecuteScalarAsync())!="InnoDB")throw new InvalidOperationException("Appearance mappings must support transactions.");
        int id=Math.Max(await Next(db,tx,"item","Item"),await Next(db,tx,"item_sparse","ItemSparse"));
        using(var used=new MySqlCommand("SELECT GREATEST(COALESCE((SELECT MAX(itemEntry) FROM foreverclassic_characters.item_instance),0),COALESCE((SELECT MAX(itemEntry) FROM foreverclassic_characters.character_void_storage),0))",db,tx))id=Math.Max(id,checked(Convert.ToInt32(await used.ExecuteScalarAsync())+1));
        var records=new Dictionary<string,int[]>{["item"]=[id],["item_sparse"]=[id]};foreach(string table in source.Rows.Keys){int first=await Next(db,tx,table,Tables[table]);records[table]=Enumerable.Range(first,source.Rows[table].Count).ToArray();}
        var clone=new CloneScope(records,source.SourceScope.EffectIds,Fingerprint(),true);var scope=new ContentScope("Clone",id,[id],[],[],[],CopyScope:clone);var before=await CaptureAsync(db,tx,id,clone,true);if(before.Tables.Where(p=>Tables.ContainsKey(p.Key)).Any(p=>p.Value.Count>0)||before.Tables["owned_instances"].Count>0||before.Tables.GetValueOrDefault("void_instances")?.Count>0)throw new InvalidOperationException("The allocated item ID is already in use. Retry.");
        var fields=new Dictionary<string,string>(draft.Fields,StringComparer.OrdinalIgnoreCase){["ID"]=id.ToString(),["VerifiedBuild"]="0"};
        var sparse=await Map(db,tx,"item_sparse",fields);await ContentEditing.Upsert(db,tx,"item_sparse",sparse);await ContentEditing.Register(db,tx,"item_sparse",id);
        var identity=new Dictionary<string,string>(source.Item.Identity,StringComparer.OrdinalIgnoreCase){["ID"]=id.ToString(),["VerifiedBuild"]="0"};foreach(string k in new[]{"ClassID","SubclassID","InventoryType"})identity[k]=draft.Text(k);await ContentEditing.Upsert(db,tx,"item",await Map(db,tx,"item",identity));await ContentEditing.Register(db,tx,"item",id);
        foreach(var (table,rows) in source.Rows)for(int n=0;n<rows.Count;n++){var row=new Dictionary<string,string>(rows[n],StringComparer.OrdinalIgnoreCase){["ID"]=records[table][n].ToString(),["ItemID"]=id.ToString(),["VerifiedBuild"]="0"};await BonusSpells.WriteAsync(db,tx,table,row.ToDictionary(p=>p.Key,p=>(string?)p.Value));await BonusSpells.RegisterAsync(db,tx,Tables[table],records[table][n],1);}
        before=before with{OriginalItem=new(){["Display"]="No item existed at this ID",["ID"]=id.ToString()}};var after=(await CaptureAsync(db,tx,id,clone,true)) with{OriginalItem=fields};string op=Guid.NewGuid().ToString();
        if(!recordOperation)return new(id,"",clone,before,after);
        using(var cmd=new MySqlCommand($"INSERT INTO {ContentEditing.Operations}(OperationId,Kind,EntityId,Name,IsActive,IsHidden,Version,BeforeJson,AfterJson,ExpectedJson,ScopeJson,Fingerprint,RecordedUtc) VALUES(@op,'Clone',@id,@name,1,0,1,@before,@after,@after,@scope,@fingerprint,UTC_TIMESTAMP(6))",db,tx)){cmd.Parameters.AddWithValue("@op",op);cmd.Parameters.AddWithValue("@id",id);cmd.Parameters.AddWithValue("@name",draft.Name);cmd.Parameters.AddWithValue("@before",HistoryActions.Serialize(before));cmd.Parameters.AddWithValue("@after",HistoryActions.Serialize(after));cmd.Parameters.AddWithValue("@scope",JsonSerializer.Serialize(scope));cmd.Parameters.AddWithValue("@fingerprint",ContentEditing.Fingerprint());await cmd.ExecuteNonQueryAsync();}
        await ContentEditing.Audit(db,tx,scope,"Duplicate item",$"{draft.Name}; copied from {source.Item.Current.Id}",op,before,after);return new(id,op,clone,before,after);
    }
    private static async Task<Dictionary<string,string?>> Map(MySqlConnection db,MySqlTransaction tx,string table,Dictionary<string,string> fields)
    {
        var columns=await Rows(db,tx,$"SHOW COLUMNS FROM {table}");var result=new Dictionary<string,string?>(StringComparer.OrdinalIgnoreCase);foreach(var c in columns){string key=c["Field"]!;string? value=fields.GetValueOrDefault(key)??c["Default"];if(value=="NULL")value=null;if(value is null&&c["Null"]!="YES")throw new InvalidOperationException("Cannot map clone field "+table+"."+key);result[key]=value;}return result;
    }
    internal static async Task RestoreAsync(MySqlConnection db,MySqlTransaction tx,ContentScope scope,ItemSnapshot target)
    {
        var clone=scope.CopyScope!;if(clone.Fingerprint!=Fingerprint())throw new InvalidOperationException("Client definitions changed; clone restoration is blocked.");foreach(var (table,dbc) in Tables){using(var delete=new MySqlCommand($"DELETE FROM {table} WHERE ID IN({Ids(clone.Records[table])})",db,tx))await delete.ExecuteNonQueryAsync();foreach(var row in target.Tables[table])await BonusSpells.WriteAsync(db,tx,table,row);foreach(int id in clone.Records[table])await BonusSpells.RegisterAsync(db,tx,dbc,id,target.Tables[table].Any(r=>r["ID"]==id.ToString())?1:2);}
    }
}
