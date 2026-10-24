using System.Globalization;
using System.Text.Json;
using DBCD.Providers;
using MySqlConnector;
namespace AnoWowItemEditor;

internal sealed record ItemColumn(string Name,string Type,bool Nullable,string? Default);
internal sealed record ItemEditSession(CatalogItem Current,CatalogItem? Native,Dictionary<string,string> Identity,Dictionary<string,string> NativeIdentity,ItemSnapshot Expected,string Fingerprint,List<ItemColumn> Columns,int Build,int IdentityBuild)
{
    internal string IdentityFingerprint {get;init;}="";
    internal bool ManagedDrops {get;init;}
    internal ItemCalculations Calculations {get;init;}=new();
}
internal static class ItemEditing
{
    internal static string IdentityFingerprint(){var f=new System.IO.FileInfo(System.IO.Path.Combine(CatalogRepository.Db2Folder,"Item.db2"));return $"{f.Length}:{f.LastWriteTimeUtc.Ticks}";}
    internal static string SnapshotSummary(ItemSnapshot snapshot)=>snapshot.OriginalItem is {} fields?string.Join("\n",fields.OrderBy(p=>p.Key).Select(p=>$"{p.Key}: {p.Value}")):"Item snapshot unavailable";
    internal static async Task<ItemEditSession> LoadAsync(int id,MySqlConnection? connection=null,MySqlTransaction? transaction=null)
    {
        string fingerprint=ContentEditing.Fingerprint(),identityFingerprint=IdentityFingerprint();
        var native=await Task.Run(()=>{
            var sparse=CatalogRepository.LoadBaseline().GetValueOrDefault(id);
            var reader=new DBCD.DBCD(new FilesystemDBCProvider(CatalogRepository.Db2Folder),new GithubDBDProvider(true));
            return (Sparse:sparse,Identity:TooltipData.LoadTable(reader,"Item").GetValueOrDefault(id)??new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase));
        });
        using var ownedDb=connection is null?await DatabaseConnection.OpenAsync(CatalogRepository.Database):null;var db=connection??ownedDb!;
        using var ownedTx=transaction is null?await db.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead):null;var tx=transaction??ownedTx!;
        var snapshot=await HistoryActions.CaptureAsync(db,tx,id);bool managedDrops=false;if((await WorldDrops.Query(db,tx,"SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA='anocore_studio_backup' AND TABLE_NAME='item_editor_world_drops'")).Count>0){managedDrops=(await WorldDrops.Query(db,tx,$"SELECT ItemId FROM {WorldDrops.Metadata} WHERE ItemId={id}")).Count>0;if(managedDrops)foreach(var p in await WorldDrops.Capture(db,tx,new([id])))snapshot.Tables[p.Key]=p.Value;}
        var sparse=await HotfixReader.LoadAsync(db,"item_sparse",TooltipData.Hash("ItemSparse"),tx);var identity=await HotfixReader.LoadAsync(db,"item",TooltipData.Hash("Item"),tx);
        if(sparse.Registrations.GetValueOrDefault(id)?.Status==2)throw new InvalidOperationException("This item was removed after the catalog loaded. Refresh the catalog.");
        var payload=sparse.ActiveRows.GetValueOrDefault(id);var fields=HotfixReader.Merge(native.Sparse?.Fields,payload??new Dictionary<string,string>());
        if(!fields.ContainsKey("Display"))throw new InvalidOperationException("No current item definition is available.");
        var item=HotfixReader.Merge(native.Identity,identity.ActiveRows.GetValueOrDefault(id)??new Dictionary<string,string>());
        foreach(string key in new[]{"ClassID","SubclassID"})if(item.TryGetValue(key,out var value))fields[key]=value;
        var columns=new List<ItemColumn>();using(var cmd=new MySqlCommand("SHOW COLUMNS FROM item_sparse",db,tx))using(var r=await cmd.ExecuteReaderAsync())while(await r.ReadAsync())columns.Add(new(Convert.ToString(r["Field"])!,Convert.ToString(r["Type"])!,Convert.ToString(r["Null"])=="YES",r["Default"] is DBNull?null:Convert.ToString(r["Default"],CultureInfo.InvariantCulture)));
        var calculations=await ItemCalculations.LoadAsync(db,new DBCD.DBCD(new FilesystemDBCProvider(CatalogRepository.Db2Folder),new GithubDBDProvider(true)),true,tx);
        if(ownedTx is not null)await tx.CommitAsync();if(fingerprint!=ContentEditing.Fingerprint()||identityFingerprint!=IdentityFingerprint())throw new InvalidOperationException("Client definitions changed while loading; reopen this item.");
        int build=payload is not null?int.Parse(payload["VerifiedBuild"]):snapshot.Tables["item_sparse"].OrderBy(r=>r["VerifiedBuild"]=="0"?0:1).FirstOrDefault() is {} retained?int.Parse(retained["VerifiedBuild"]!):0;
        int identityBuild=identity.ActiveRows.TryGetValue(id,out var active)?int.Parse(active["VerifiedBuild"]):0;
        return new(new(fields,payload is not null),native.Sparse?.Fields.ContainsKey("Display")==true?native.Sparse:null,item,native.Identity,snapshot,fingerprint,columns,build,identityBuild){IdentityFingerprint=identityFingerprint,ManagedDrops=managedDrops,Calculations=calculations};
    }
    internal static List<FieldComparison> Changes(ItemEditSession session,CatalogItem draft)=>OverrideEntry.Compare(session.Current,draft).Where(f=>f.Changed).ToList();
    internal static void Validate(ItemEditSession session,CatalogItem draft)
    {
        var problems=session.Calculations.ServerProblems(draft);if(problems.Count>0)throw new InvalidOperationException("Save blocked: "+string.Join(" ",problems)+" A missing server lookup can crash worldserver. [scales] never means a missing table row is safe.");
        if(draft.Id!=session.Current.Id)throw new InvalidOperationException("The item ID cannot be changed.");
        if(!int.TryParse(draft.Text("ClassID"),out int itemClass)||itemClass is <0 or >18||!int.TryParse(draft.Text("SubclassID"),out int subclass)||subclass is <0 or >255)throw new InvalidOperationException("Item class and subclass must be valid whole numbers.");
        if(string.IsNullOrWhiteSpace(draft.Name)||string.IsNullOrWhiteSpace(draft.Text("Display")))throw new InvalidOperationException("Enter an item name.");
        foreach(var column in session.Columns.Where(c=>c.Name is not ("ID" or "VerifiedBuild")&&draft.Fields.ContainsKey(c.Name))){
            if(OverrideEntry.Equivalent(session.Current.Text(column.Name),draft.Text(column.Name)))continue;
            string value=draft.Text(column.Name),type=column.Type;
            if(type.Contains("text")||type.Contains("char")){if(value.IndexOf('\0')>=0)throw new InvalidOperationException(column.Name+" cannot contain a null character.");continue;}
            if(type.StartsWith("float")||type.StartsWith("double")){if(!float.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out float number)||!float.IsFinite(number))throw new InvalidOperationException(column.Name+" must be a finite number using a decimal point.");continue;}
            if(!long.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out long n))throw new InvalidOperationException(column.Name+" must be a whole number.");
            int bits=type.StartsWith("tinyint")?8:type.StartsWith("smallint")?16:32;bool unsigned=type.Contains("unsigned");long min=unsigned?0:-(1L<<(bits-1)),max=unsigned?(1L<<bits)-1:(1L<<(bits-1))-1;if(n<min||n>max)throw new InvalidOperationException($"{column.Name} must be from {min} to {max}.");
        }
        if(draft.Quality is <0 or >8||!CatalogItem.Slots.ContainsKey(draft.Slot)&&draft.Slot!=session.Current.Slot||draft.ItemLevel<0||draft.ClassId<0||draft.Number("SubclassID")<0||draft.ClassId>18||draft.Number("SubclassID")>255)throw new InvalidOperationException("Check item quality, slot, item level, class and subclass.");
    }
    internal static async Task<string> SaveAsync(ItemEditSession session,CatalogItem draft)
    {
        Validate(session,draft);return await ContentEditing.Transaction(async(db,tx)=>await SaveCoreAsync(db,tx,session,draft));
    }
    internal static async Task<string> SaveCoreAsync(MySqlConnection db,MySqlTransaction tx,ItemEditSession session,CatalogItem draft)
    {
        Validate(session,draft);if(Changes(session,draft).Count==0)throw new InvalidOperationException("No item fields have changed.");
        var freshCalculations=await ItemCalculations.LoadAsync(db,new DBCD.DBCD(new FilesystemDBCProvider(CatalogRepository.Db2Folder),new GithubDBDProvider(true)),true,tx);var unsafeRows=freshCalculations.ServerProblems(draft);if(unsafeRows.Count>0)throw new InvalidOperationException("Save blocked after refreshing server tables: "+string.Join(" ",unsafeRows));
        int id=session.Current.Id;var scope=new ContentScope("Item",id,[id],[],session.Native is null?[]:[id],session.NativeIdentity.Count==0?[]:[id],ItemFingerprint:session.IdentityFingerprint,Drops:session.ManagedDrops?new([id]):null);
        var before=await ContentEditing.CaptureAsync(db,tx,scope,true);
        if(ContentEditing.Fingerprint()!=session.Fingerprint||IdentityFingerprint()!=session.IdentityFingerprint||HistoryActions.Canonical(before)!=HistoryActions.Canonical(session.Expected))throw new InvalidOperationException("Conflict: this item changed after loading. Reload the item before saving.");
        var payload=new Dictionary<string,string?>(StringComparer.OrdinalIgnoreCase);
        foreach(var column in session.Columns){
            string? value=draft.Fields.GetValueOrDefault(column.Name)??column.Default;
            var existing=session.Current.Overridden?session.Expected.Tables["item_sparse"].FirstOrDefault(r=>r["VerifiedBuild"]==session.Build.ToString()):null;
            if(existing is not null&&existing.TryGetValue(column.Name,out var oldValue)&&OverrideEntry.Equivalent(session.Current.Text(column.Name),draft.Text(column.Name)))value=oldValue;
            if(column.Name=="ID")value=id.ToString();if(column.Name=="VerifiedBuild")value=session.Build.ToString();
            if(value is null&&!column.Nullable)throw new InvalidOperationException("Cannot map required server field: "+column.Name);payload[column.Name]=value;
        }
        await ContentEditing.Upsert(db,tx,"item_sparse",payload);await ContentEditing.Register(db,tx,"item_sparse",id);
        bool identityChanged=new[]{"ClassID","SubclassID","InventoryType"}.Any(k=>!OverrideEntry.Equivalent(session.Current.Text(k),draft.Text(k)));
        if(identityChanged){
            if(session.Identity.Count==0)throw new InvalidOperationException("The item identity definition is unavailable; type changes are blocked.");
            var identity=session.Identity.ToDictionary(p=>p.Key,p=>(string?)p.Value,StringComparer.OrdinalIgnoreCase);identity["ID"]=id.ToString();identity["VerifiedBuild"]=session.IdentityBuild.ToString();foreach(string key in new[]{"ClassID","SubclassID","InventoryType"})identity[key]=draft.Text(key);
            using(var columns=new MySqlCommand("SHOW COLUMNS FROM item",db,tx))using(var r=await columns.ExecuteReaderAsync())while(await r.ReadAsync()){
                string name=Convert.ToString(r["Field"])!;if(identity.ContainsKey(name))continue;
                string? value=r["Default"] is DBNull?null:Convert.ToString(r["Default"],CultureInfo.InvariantCulture);if(value=="NULL")value=null;if(value is null&&Convert.ToString(r["Null"])!="YES")throw new InvalidOperationException("Cannot map required identity field "+name);identity[name]=value;
            }
            await ContentEditing.Upsert(db,tx,"item",identity);await ContentEditing.Register(db,tx,"item",id);
        }
        if(session.ManagedDrops&&new[]{"ItemLevel","RequiredLevel"}.Any(k=>!OverrideEntry.Equivalent(session.Current.Text(k),draft.Text(k)))){decimal chance=decimal.Parse(session.Expected.Tables["world_drop_policy"].Single()["Chance"]!,CultureInfo.InvariantCulture);var world=await WorldDropCatalog.LoadAsync(db,tx);await WorldDrops.Apply(db,tx,id,WorldDrops.Replan(world,draft,JsonSerializer.Deserialize<DropPlan>(session.Expected.Tables["world_drop_policy"].Single()["PlanJson"]!)!),draft.Name);}
        before=before with{OriginalItem=new(session.Current.Fields,StringComparer.OrdinalIgnoreCase)};
        var after=(await ContentEditing.CaptureAsync(db,tx,scope,true)) with{OriginalItem=new(draft.Fields,StringComparer.OrdinalIgnoreCase)};string op=Guid.NewGuid().ToString();
        using(var cmd=new MySqlCommand($"INSERT INTO {ContentEditing.Operations}(OperationId,Kind,EntityId,Name,IsActive,IsHidden,Version,BeforeJson,AfterJson,ExpectedJson,ScopeJson,Fingerprint,RecordedUtc) VALUES(@op,'Item',@id,@name,1,0,1,@before,@after,@after,@scope,@fingerprint,UTC_TIMESTAMP(6))",db,tx)){
            cmd.Parameters.AddWithValue("@op",op);cmd.Parameters.AddWithValue("@id",id);cmd.Parameters.AddWithValue("@name",draft.Name);cmd.Parameters.AddWithValue("@before",HistoryActions.Serialize(before));cmd.Parameters.AddWithValue("@after",HistoryActions.Serialize(after));cmd.Parameters.AddWithValue("@scope",JsonSerializer.Serialize(scope));cmd.Parameters.AddWithValue("@fingerprint",session.Fingerprint);await cmd.ExecuteNonQueryAsync();
        }
        using(var visible=new MySqlCommand("DELETE FROM anocore_studio_backup.item_editor_hidden_history WHERE ItemId=@id",db,tx)){visible.Parameters.AddWithValue("@id",id);await visible.ExecuteNonQueryAsync();}
        await ContentEditing.Audit(db,tx,scope,"Edit item",draft.Name,op,before,after);return op;
    }
}
