using System.Globalization;
using System.IO;
using System.Text.Json;
using DBCD.Providers;
using MySqlConnector;
namespace WoWItemEditor;

internal sealed record ContentRow(int Id,int EffectId,int SpellId,int Pieces=2,int Specialization=0,int Trait=0,int Trigger=2,int Cooldown=-1,int CategoryCooldown=-1,int Charges=0,int Slot=0,int Category=0,int Condition=0,BonusDraft? Bonus=null)
{
    public string TriggerLabel=>ReferenceCatalog.Trigger(Trigger);
}
internal sealed record ContentScope(string Kind,int EntityId,int[] RowIds,int[] EffectIds,int[] NativeRowIds,int[] NativeEffectIds,BonusScope? Bonus=null,string? ItemFingerprint=null,CloneScope? CopyScope=null,SetCreationScope? Creation=null,WorldDropScope? Drops=null);
internal sealed record ContentSession(ContentScope Scope,List<ContentRow> Initial,ItemSnapshot Expected,string Fingerprint,Dictionary<string,Dictionary<int,Dictionary<string,string>>> Native,Dictionary<int,Dictionary<string,string>> EffectiveRows,Dictionary<int,Dictionary<string,string>> EffectiveEffects,int MaxPieces);
internal sealed record ContentOperation(string Id,string Kind,int EntityId,string Name,bool Active,bool Hidden,int Version,string Before,string After,string Expected,string Scope,string Fingerprint,string RecordedUtc);

internal static class ContentEditing
{
    internal static string Operations => $"{AppSettings.Current.BackupDatabase}.item_editor_content_operations";
    internal static readonly Dictionary<string,string> Tables=new(){["item_set_spell"]="ItemSetSpell",["item_effect"]="ItemEffect",["item_x_item_effect"]="ItemXItemEffect",["item_set"]="ItemSet",["item_sparse"]="ItemSparse",["item"]="Item"};
    internal static string Fingerprint()=>string.Join("/",new[]{"ItemSetSpell","ItemEffect","ItemXItemEffect","ItemSet","ItemSparse"}.Select(n=>{var f=new FileInfo(Path.Combine(CatalogRepository.Db2Folder,n+".db2"));return $"{f.Length}:{f.LastWriteTimeUtc.Ticks}";}));
    private static int N(IReadOnlyDictionary<string,string> row,string key)=>int.TryParse(row.GetValueOrDefault(key),out int n)?n:0;
    internal static async Task InitializeAsync(MySqlConnection db,EditorSettings? configuration=null)
    {
        var cfg=configuration??AppSettings.Current;string operations=$"{cfg.BackupDatabase}.item_editor_content_operations";
        await HistoryActions.InitializeAsync(db,cfg);await WorldDrops.Initialize(db,cfg);
        using(var cmd=new MySqlCommand($"CREATE TABLE IF NOT EXISTS {operations} (OperationId CHAR(36) NOT NULL PRIMARY KEY,Kind VARCHAR(16) NOT NULL,EntityId INT NOT NULL,Name VARCHAR(255) NOT NULL,IsActive TINYINT NOT NULL,IsHidden TINYINT NOT NULL DEFAULT 0,Version INT NOT NULL,BeforeJson LONGTEXT NOT NULL,AfterJson LONGTEXT NOT NULL,ExpectedJson LONGTEXT NOT NULL,ScopeJson LONGTEXT NOT NULL,Fingerprint TEXT NOT NULL,RecordedUtc DATETIME(6) NOT NULL) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",db))await cmd.ExecuteNonQueryAsync();
        foreach(string table in Tables.Keys.Select(t=>$"{cfg.HotfixDatabase}."+t).Append(operations)){
            var p=table.Split('.');using var cmd=new MySqlCommand("SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA=@s AND TABLE_NAME=@t",db);cmd.Parameters.AddWithValue("@s",p[0]);cmd.Parameters.AddWithValue("@t",p[1]);if(!string.Equals(Convert.ToString(await cmd.ExecuteScalarAsync()),"InnoDB",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException(table+" must support transactions.");
        }
    }
    internal static async Task<ContentSession> LoadAsync(string kind,int id,int maxPieces=17)
    {
        if(kind is not ("Set" or "Proc"))throw new ArgumentException("Unsupported editor");
        string fingerprint=Fingerprint();
        var native=await Task.Run(()=>{var r=new DBCD.DBCD(new FilesystemDBCProvider(CatalogRepository.Db2Folder),new GithubDBDProvider(true));return Tables.ToDictionary(t=>t.Key,t=>TooltipData.LoadTable(r,t.Value));});
        using var db=await DatabaseConnection.OpenAsync(CatalogRepository.Database);
        using var tx=await db.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var effective=new Dictionary<string,Dictionary<int,Dictionary<string,string>>>();
        foreach(var (table,dbc) in Tables){var copy=native[table].ToDictionary(p=>p.Key,p=>new Dictionary<string,string>(p.Value,StringComparer.OrdinalIgnoreCase));await TooltipData.Overlay(db,table,dbc,copy,tx);effective[table]=copy;}
        var rows=new List<ContentRow>();var own=effective[kind=="Set"?"item_set_spell":"item_x_item_effect"].Where(p=>N(p.Value,kind=="Set"?"ItemSetID":"ItemID")==id).ToDictionary();
        foreach(var (rowId,row) in own){
            if(kind=="Set")rows.Add(new(rowId,0,N(row,"SpellID"),N(row,"Threshold"),N(row,"ChrSpecID"),N(row,"TraitSubTreeID")));
            else{int effectId=N(row,"ItemEffectID");if(!effective["item_effect"].TryGetValue(effectId,out var e))throw new InvalidOperationException($"Item-effect {effectId} is missing; editing would lose that assignment.");rows.Add(new(rowId,effectId,N(e,"SpellID"),Specialization:N(e,"ChrSpecializationID"),Trigger:N(e,"TriggerType"),Cooldown:N(e,"CoolDownMSec"),CategoryCooldown:N(e,"CategoryCoolDownMSec"),Charges:N(e,"Charges"),Slot:N(e,"LegacySlotIndex"),Category:N(e,"SpellCategoryID"),Condition:N(e,"PlayerConditionID")));}
        }
        var nativeIds=native[kind=="Set"?"item_set_spell":"item_x_item_effect"].Where(p=>N(p.Value,kind=="Set"?"ItemSetID":"ItemID")==id).Select(p=>p.Key);
        var effectIds=rows.Select(r=>r.EffectId).Where(n=>n>0).Distinct().ToArray();
        var scope=new ContentScope(kind,id,rows.Select(r=>r.Id).Concat(nativeIds).Distinct().ToArray(),effectIds,nativeIds.ToArray(),effectIds.Where(n=>native["item_effect"].ContainsKey(n)).ToArray());
        var expected=await CaptureAsync(db,tx,scope);await tx.CommitAsync();
        if(fingerprint!=Fingerprint())throw new InvalidOperationException("Client definitions changed while loading; reopen the editor.");
        return new(scope,rows.OrderBy(r=>kind=="Set"?r.Pieces:r.Slot).ToList(),expected,fingerprint,native,own,effective["item_effect"],maxPieces);
    }
    private static string Ids(IEnumerable<int> ids)=>string.Join(',',ids.Distinct().DefaultIfEmpty(-1));
    private static async Task<List<Dictionary<string,string?>>> Query(MySqlConnection db,MySqlTransaction? tx,string sql,int id)
    {
        using var cmd=new MySqlCommand(sql,db,tx);cmd.Parameters.AddWithValue("@id",id);using var r=await cmd.ExecuteReaderAsync();var rows=new List<Dictionary<string,string?>>();while(await r.ReadAsync()){var row=new Dictionary<string,string?>(StringComparer.OrdinalIgnoreCase);for(int n=0;n<r.FieldCount;n++)row[r.GetName(n)]=r.IsDBNull(n)?null:Convert.ToString(r.GetValue(n),CultureInfo.InvariantCulture);rows.Add(row);}return rows;
    }
    internal static async Task<ItemSnapshot> CaptureAsync(MySqlConnection db,MySqlTransaction? tx,ContentScope scope,bool locked=false)
    {
        if(scope.Creation is not null)return await SetCreation.Capture(db,tx,scope.Creation,locked);
        if(scope.Kind=="Clone")return await ItemCloning.CaptureAsync(db,tx,scope.EntityId,scope.CopyScope!,locked);
        if(scope.Kind is "Item" or "ItemDrops"){var item=await HistoryActions.CaptureAsync(db,tx,scope.EntityId,locked);if(scope.Drops is not null)foreach(var p in await WorldDrops.Capture(db,tx,scope.Drops,locked))item.Tables[p.Key]=p.Value;return item;}
        string suffix=locked?" FOR UPDATE":"";int id=scope.EntityId;var tables=new Dictionary<string,List<Dictionary<string,string?>>>();
        string main=scope.Kind=="Set"?"item_set_spell":"item_x_item_effect",parent=scope.Kind=="Set"?"ItemSetID":"ItemID";
        tables[main]=await Query(db,tx,$"SELECT * FROM {main} WHERE {parent}=@id OR ID IN({Ids(scope.RowIds)}) ORDER BY ID,VerifiedBuild"+suffix,id);
        var rowIds=scope.RowIds.Concat(tables[main].Select(r=>int.Parse(r["ID"]!))).Distinct().ToArray();
        var hashes=new List<string>{$"(TableHash={TooltipData.Hash(Tables[main])} AND RecordId IN({Ids(rowIds)}))"};
        if(scope.Kind=="Proc"){
            var effectIds=scope.EffectIds.Concat(tables[main].Select(r=>int.Parse(r["ItemEffectID"]!))).Distinct().ToArray();
            tables["item_effect"]=await Query(db,tx,$"SELECT * FROM item_effect WHERE ID IN({Ids(effectIds)}) ORDER BY ID,VerifiedBuild"+suffix,id);
            tables["external_links"]=await Query(db,tx,$"SELECT * FROM item_x_item_effect WHERE ItemEffectID IN({Ids(effectIds)}) AND ItemID<>@id ORDER BY ID,VerifiedBuild"+suffix,id);
            tables["item_sparse"]=await Query(db,tx,"SELECT * FROM item_sparse WHERE ID=@id ORDER BY VerifiedBuild"+suffix,id);
            tables["item"]=await Query(db,tx,"SELECT * FROM item WHERE ID=@id ORDER BY VerifiedBuild"+suffix,id);
            hashes.Add($"(TableHash={TooltipData.Hash("ItemEffect")} AND RecordId IN({Ids(effectIds)}))");hashes.Add($"(TableHash={TooltipData.Hash("ItemSparse")} AND RecordId=@id)");hashes.Add($"(TableHash={TooltipData.Hash("Item")} AND RecordId=@id)");
        }else{
            tables["item_set"]=await Query(db,tx,"SELECT * FROM item_set WHERE ID=@id ORDER BY VerifiedBuild"+suffix,id);hashes.Add($"(TableHash={TooltipData.Hash("ItemSet")} AND RecordId=@id)");
        }
        tables["hotfix_data"]=await Query(db,tx,$"SELECT * FROM hotfix_data WHERE {string.Join(" OR ",hashes)} ORDER BY Id,UniqueId"+suffix,id);
        if(scope.Bonus is not null)foreach(var p in (await BonusSpells.CaptureAsync(db,tx,scope.Bonus,locked)).Tables)tables[p.Key]=p.Value;
        return new(tables);
    }
    internal static void Validate(ContentSession session,IReadOnlyList<ContentRow> draft,ReferenceCatalog references)
    {
        if(draft.Where(r=>r.Id>0).GroupBy(r=>r.Id).Any(g=>g.Count()>1)||draft.Any(r=>r.Id<0||r.Id>0&&!session.Initial.Any(o=>o.Id==r.Id)))throw new InvalidOperationException("Draft contains an unknown or duplicate row.");
        var known=references.Spells.Select(s=>s.SpellId).ToHashSet();
        foreach(var row in draft){
            if(session.Initial.Contains(row))continue;
            if(row.SpellId<=0||!known.Contains(row.SpellId))throw new InvalidOperationException($"Spell {row.SpellId} is not in the available spell catalog.");
            if(row.Bonus is not null){if(session.Scope.Kind!="Set"||row.Bonus.Source.Root!=row.SpellId)throw new InvalidOperationException("Bonus values belong to another spell.");BonusSpells.Validate(row.Bonus);}
            if(row.Specialization is <0 or >65535||row.Trait is <0 or >65535)throw new InvalidOperationException("Specialization and trait IDs must be 0–65535.");
            if(session.Scope.Kind=="Set"&& (row.Pieces<1||row.Pieces>Math.Min(255,session.MaxPieces)))throw new InvalidOperationException($"Pieces must be from 1 to {Math.Min(255,session.MaxPieces)} for this set.");
            if(session.Scope.Kind=="Proc"&& (row.Trigger is not (0 or 1 or 2 or 5 or 6)||row.Cooldown< -1||row.CategoryCooldown< -1||row.Charges is < -32768 or >32767||row.Slot is <0 or >4||row.Category is <0 or >65535||row.Condition<0))throw new InvalidOperationException("Check trigger, cooldowns (milliseconds; -1 = spell default), charges, slot (0–4), category and condition IDs.");
        }
        if(session.Scope.Kind=="Set"&&draft.GroupBy(r=>(r.Pieces,r.Specialization,r.Trait)).Any(g=>g.Count()>1)&&!draft.SequenceEqual(session.Initial))throw new InvalidOperationException("Each pieces/specialization/trait combination must be unique.");
        if(session.Scope.Kind=="Proc"&&draft.Where(r=>!session.Initial.Contains(r)).Any(r=>draft.Count(d=>d.Slot==r.Slot)>1))throw new InvalidOperationException("Choose an unused item-effect slot (0–4).");
    }
    internal static bool Changed(ContentSession session,IReadOnlyList<ContentRow> draft)=>!session.Initial.OrderBy(r=>r.Id).SequenceEqual(draft.OrderBy(r=>r.Id));
    private static async Task<int> Allocate(MySqlConnection db,MySqlTransaction tx,ContentSession session,string table)
    {
        using var cmd=new MySqlCommand($"SELECT GREATEST(COALESCE((SELECT MAX(ID) FROM {table}),0),COALESCE((SELECT MAX(RecordId) FROM hotfix_data WHERE TableHash=@hash),0))",db,tx);cmd.Parameters.AddWithValue("@hash",TooltipData.Hash(Tables[table]));long max=Math.Max(Convert.ToInt64(await cmd.ExecuteScalarAsync()),session.Native[table].Keys.DefaultIfEmpty().Max());return checked((int)(max+1));
    }
    internal static async Task Upsert(MySqlConnection db,MySqlTransaction tx,string table,Dictionary<string,string?> values)
    {
        if(!Tables.ContainsKey(table))throw new InvalidOperationException("Unsupported content table");
        var columns=await Query(db,tx,$"SELECT COLUMN_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='{table}' ORDER BY ORDINAL_POSITION",0);
        string[] keys=columns.Select(r=>r["COLUMN_NAME"]!).ToArray();
        if(keys.Any(k=>!values.ContainsKey(k)))throw new InvalidOperationException($"Unmapped {table} field; saving is blocked to preserve the server schema.");
        using var cmd=new MySqlCommand($"INSERT INTO {table} ({string.Join(',',keys.Select(k=>$"`{k}`"))}) VALUES({string.Join(',',keys.Select((_,n)=>"@v"+n))}) ON DUPLICATE KEY UPDATE {string.Join(',',keys.Where(k=>k is not ("ID" or "VerifiedBuild")).Select(k=>$"`{k}`=VALUES(`{k}`)"))}",db,tx);for(int n=0;n<keys.Length;n++)cmd.Parameters.AddWithValue("@v"+n,(object?)values[keys[n]]??DBNull.Value);await cmd.ExecuteNonQueryAsync();
    }
    internal static async Task Register(MySqlConnection db,MySqlTransaction tx,string table,int id,int status=1)
    {
        using var next=new MySqlCommand("SELECT COALESCE(MAX(Id),0)+1 FROM hotfix_data FOR UPDATE",db,tx);long push=Convert.ToInt64(await next.ExecuteScalarAsync());if(push>int.MaxValue)throw new InvalidOperationException("Hotfix push IDs exhausted.");
        using var cmd=new MySqlCommand("INSERT INTO hotfix_data(Id,UniqueId,TableHash,RecordId,Status,VerifiedBuild) VALUES(@push,@push,@hash,@id,@status,0)",db,tx);cmd.Parameters.AddWithValue("@push",push);cmd.Parameters.AddWithValue("@hash",TooltipData.Hash(Tables[table]));cmd.Parameters.AddWithValue("@id",id);cmd.Parameters.AddWithValue("@status",status);await cmd.ExecuteNonQueryAsync();
    }
    private static Dictionary<string,string?> Values(Dictionary<string,string>? source=null)=>source is null?new(StringComparer.OrdinalIgnoreCase):source.ToDictionary(p=>p.Key,p=>(string?)p.Value,StringComparer.OrdinalIgnoreCase);
    internal static async Task<string> SaveAsync(ContentSession session,ContentRow[] draft,string name,ReferenceCatalog references)
    {
        Validate(session,draft,references);return await Transaction(async(db,tx)=>await SaveCoreAsync(db,tx,session,draft,name));
    }
    internal static async Task<T> Transaction<T>(Func<MySqlConnection,MySqlTransaction,Task<T>> action)
    {
        using var db=await DatabaseConnection.OpenAsync(CatalogRepository.Database);await InitializeAsync(db);
        using(var gate=new MySqlCommand("SELECT GET_LOCK('anocore_studio_forge',8)",db))if(Convert.ToInt32(await gate.ExecuteScalarAsync())!=1)throw new InvalidOperationException("Another editor is saving; retry shortly.");
        try{using var tx=await db.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);try{T result=await action(db,tx);await tx.CommitAsync();return result;}catch{await tx.RollbackAsync();throw;}}
        finally{using var release=new MySqlCommand("SELECT RELEASE_LOCK('anocore_studio_forge')",db);await release.ExecuteScalarAsync();}
    }
    internal static async Task<string> SaveCoreAsync(MySqlConnection db,MySqlTransaction tx,ContentSession session,ContentRow[] draft,string name)
    {
        if(!Changed(session,draft))throw new InvalidOperationException("There are no changes to save.");
        if(Fingerprint()!=session.Fingerprint||HistoryActions.Canonical(await CaptureAsync(db,tx,session.Scope,true))!=HistoryActions.Canonical(session.Expected))throw new InvalidOperationException("Conflict: this content changed after it was loaded. Reopen it before saving.");
        string main=session.Scope.Kind=="Set"?"item_set_spell":"item_x_item_effect";
        var writes=new List<(string Table,int Id,Dictionary<string,string?> Row)>();var removed=session.Initial.Where(old=>draft.All(r=>r.Id!=old.Id)).ToList();
        var bonusPlans=new List<(int Index,BonusDraft Draft,int Root,BonusScope Scope)>();var reservations=new Dictionary<string,int>();
        for(int n=0;n<draft.Length;n++)if(draft[n].Bonus is {} bonus){var allocated=await BonusSpells.AllocateAsync(db,tx,bonus,reservations);bonusPlans.Add((n,bonus,allocated.Root,allocated.Scope));}
        var retained=draft.ToArray();foreach(var plan in bonusPlans)retained[plan.Index]=retained[plan.Index] with{SpellId=plan.Root,Bonus=null};
        int nextMain=await Allocate(db,tx,session,main),nextEffect=await Allocate(db,tx,session,"item_effect");
        foreach(var row in retained.Where(r=>!session.Initial.Contains(r))){
            int id=row.Id==0?nextMain++:row.Id;var values=Values(session.EffectiveRows.GetValueOrDefault(row.Id));
            values["ID"]=id.ToString();values["VerifiedBuild"]="0";
            if(session.Scope.Kind=="Set"){
                values["ItemSetID"]=session.Scope.EntityId.ToString();values["SpellID"]=row.SpellId.ToString();values["Threshold"]=row.Pieces.ToString();values["ChrSpecID"]=row.Specialization.ToString();values["TraitSubTreeID"]=row.Trait.ToString();
            }else{
                int effect=nextEffect++;var e=Values(session.EffectiveEffects.GetValueOrDefault(row.EffectId));
                foreach(var (k,v) in new Dictionary<string,int>{["ID"]=effect,["VerifiedBuild"]=0,["SpellID"]=row.SpellId,["TriggerType"]=row.Trigger,["CoolDownMSec"]=row.Cooldown,["CategoryCoolDownMSec"]=row.CategoryCooldown,["Charges"]=row.Charges,["LegacySlotIndex"]=row.Slot,["SpellCategoryID"]=row.Category,["ChrSpecializationID"]=row.Specialization,["PlayerConditionID"]=row.Condition})e[k]=v.ToString();
                writes.Add(("item_effect",effect,e));values["ItemID"]=session.Scope.EntityId.ToString();values["ItemEffectID"]=effect.ToString();
            }
            writes.Add((main,id,values));
        }
        var scope=session.Scope with{RowIds=session.Scope.RowIds.Concat(writes.Where(w=>w.Table==main).Select(w=>w.Id)).Distinct().ToArray(),EffectIds=session.Scope.EffectIds.Concat(writes.Where(w=>w.Table=="item_effect").Select(w=>w.Id)).Distinct().ToArray()};
        if(bonusPlans.Count>0)scope=scope with{Bonus=BonusSpells.Merge(bonusPlans.Select(p=>p.Scope))};
        var before=(await CaptureAsync(db,tx,scope,true)) with{ContentRows=session.Initial.ToArray()};
        foreach(var plan in bonusPlans)await BonusSpells.ApplyAsync(db,tx,plan.Draft,plan.Scope);
        foreach(var write in writes){await Upsert(db,tx,write.Table,write.Row);await Register(db,tx,write.Table,write.Id);}
        foreach(var row in removed)await Register(db,tx,main,row.Id,2);
        var after=(await CaptureAsync(db,tx,scope,true)) with{ContentRows=retained};string operation=Guid.NewGuid().ToString();
        using(var cmd=new MySqlCommand($"INSERT INTO {Operations}(OperationId,Kind,EntityId,Name,IsActive,IsHidden,Version,BeforeJson,AfterJson,ExpectedJson,ScopeJson,Fingerprint,RecordedUtc) VALUES(@op,@kind,@id,@name,1,0,1,@before,@after,@after,@scope,@fingerprint,UTC_TIMESTAMP(6))",db,tx)){
            cmd.Parameters.AddWithValue("@op",operation);cmd.Parameters.AddWithValue("@kind",scope.Kind);cmd.Parameters.AddWithValue("@id",scope.EntityId);cmd.Parameters.AddWithValue("@name",name);cmd.Parameters.AddWithValue("@before",HistoryActions.Serialize(before));cmd.Parameters.AddWithValue("@after",HistoryActions.Serialize(after));cmd.Parameters.AddWithValue("@scope",JsonSerializer.Serialize(scope));cmd.Parameters.AddWithValue("@fingerprint",session.Fingerprint);await cmd.ExecuteNonQueryAsync();
        }
        await Audit(db,tx,scope,$"Save {scope.Kind} edit",name,operation,before,after);return operation;
    }
    internal static async Task Audit(MySqlConnection db,MySqlTransaction tx,ContentScope scope,string action,string name,string parent,ItemSnapshot before,ItemSnapshot after)
    {
        string eventId=Guid.NewGuid().ToString();using(var cmd=new MySqlCommand("INSERT INTO anocore_studio_changes(ChangeId,ChangedUtc,Actor,EntityType,EntityId,Action,Summary,BeforeJson,AfterJson,Provenance,SourceKey) VALUES(@event,UTC_TIMESTAMP(6),@actor,@kind,@id,@action,@summary,@before,@after,@provenance,@key)",db,tx)){
            cmd.Parameters.AddWithValue("@event",eventId);cmd.Parameters.AddWithValue("@actor",Environment.UserDomainName+"\\"+Environment.UserName);cmd.Parameters.AddWithValue("@kind",scope.Kind);cmd.Parameters.AddWithValue("@id",scope.EntityId);cmd.Parameters.AddWithValue("@action",action);cmd.Parameters.AddWithValue("@summary",$"{name} · {action}. Server reload pending.");cmd.Parameters.AddWithValue("@before",HistoryActions.Serialize(before));cmd.Parameters.AddWithValue("@after",HistoryActions.Serialize(after));cmd.Parameters.AddWithValue("@provenance","WoWItemEditor Item Editor; operation "+parent);cmd.Parameters.AddWithValue("@key","ItemEditor:"+eventId);await cmd.ExecuteNonQueryAsync();
        }
        using var mirror=new MySqlCommand($"INSERT INTO {AppSettings.Current.BackupDatabase}.changes SELECT * FROM anocore_studio_changes WHERE ChangeId=@id",db,tx);mirror.Parameters.AddWithValue("@id",eventId);await mirror.ExecuteNonQueryAsync();
    }
    internal static async Task<List<ContentOperation>> ListAsync()
    {
        using var db=await DatabaseConnection.OpenAsync(CatalogRepository.Database);if(!await HotfixHistory.HasTable(db,$"{AppSettings.Current.BackupDatabase}","item_editor_content_operations"))return [];
        return ReadOperations(await Query(db,null,$"SELECT * FROM {Operations} ORDER BY RecordedUtc DESC",0));
    }
    private static List<ContentOperation> ReadOperations(List<Dictionary<string,string?>> rows)=>rows.Select(r=>new ContentOperation(r["OperationId"]!,r["Kind"]!,int.Parse(r["EntityId"]!),r["Name"]!,r["IsActive"]=="1",r["IsHidden"]=="1",int.Parse(r["Version"]!),r["BeforeJson"]!,r["AfterJson"]!,r["ExpectedJson"]!,r["ScopeJson"]!,r["Fingerprint"]!,r["RecordedUtc"]!)).ToList();
    internal static async Task<ContentOperation> ReadAsync(MySqlConnection db,MySqlTransaction tx,string id)=>ReadOperations(await Query(db,tx,$"SELECT * FROM {Operations} WHERE OperationId='{Guid.Parse(id)}'",0)).Single();
    internal static async Task SetOperationHiddenAsync(ContentOperation op,bool hidden)=>await Transaction(async(db,tx)=>{await SetOperationHiddenCoreAsync(db,tx,op,hidden);return true;});
    internal static async Task SetOperationHiddenCoreAsync(MySqlConnection db,MySqlTransaction tx,ContentOperation op,bool hidden)
    {
        using var cmd=new MySqlCommand($"UPDATE {Operations} SET IsHidden=@hidden,Version=Version+1 WHERE OperationId=@op AND Version=@version AND IsActive=0",db,tx);cmd.Parameters.AddWithValue("@hidden",hidden);cmd.Parameters.AddWithValue("@op",op.Id);cmd.Parameters.AddWithValue("@version",op.Version);if(await cmd.ExecuteNonQueryAsync()!=1)throw new InvalidOperationException("Active or changed entries cannot be removed. Refresh history.");
        var scope=JsonSerializer.Deserialize<ContentScope>(op.Scope)!;var snapshot=await CaptureAsync(db,tx,scope);await Audit(db,tx,scope,hidden?"Remove content history":"Restore content history",op.Name,op.Id,snapshot,snapshot);
    }
    internal static async Task ToggleAsync(ContentOperation op)=>await Transaction(async(db,tx)=>{await ToggleCoreAsync(db,tx,op);return true;});
    internal static async Task ToggleCoreAsync(MySqlConnection db,MySqlTransaction tx,ContentOperation op)
    {
        var actual=ReadOperations(await Query(db,tx,$"SELECT * FROM {Operations} WHERE OperationId='{Guid.Parse(op.Id)}' FOR UPDATE",0)).Single();
        if(actual.Version!=op.Version||actual.Active!=op.Active||op.Fingerprint!=Fingerprint())throw new InvalidOperationException("Conflict: operation or client definitions changed. Refresh history.");
        var scope=JsonSerializer.Deserialize<ContentScope>(op.Scope)!;var current=await CaptureAsync(db,tx,scope,true);
        if(scope.ItemFingerprint is not null&&scope.ItemFingerprint!=ItemEditing.IdentityFingerprint())throw new InvalidOperationException("The native item identity changed after this edit. Automatic restore is blocked.");
        var expected=JsonSerializer.Deserialize<ItemSnapshot>(op.Expected)!;
        if(HistoryActions.Canonical(current)!=HistoryActions.Canonical(expected))throw new InvalidOperationException("Conflict: newer edits or shared references exist. This archived edit cannot be applied automatically; reopen the editor to review the current configuration.");
        var target=JsonSerializer.Deserialize<ItemSnapshot>(op.Active?op.Before:op.After)!;
        await ItemSaveSafety.ValidateSnapshot(db,tx,target,scope.Kind is "Item" or "ItemDrops"?scope.EntityId:null);
        var before=JsonSerializer.Deserialize<ItemSnapshot>(op.Before)!;var after=JsonSerializer.Deserialize<ItemSnapshot>(op.After)!;
        await RestoreRetained(db,tx,scope,before,after,target);
        await ActiveChangeRecovery.ValidateFinal(db,tx,[scope]);
        var resulting=await CaptureAsync(db,tx,scope,true);
        using(var cmd=new MySqlCommand($"UPDATE {Operations} SET IsActive=@active,IsHidden=0,Version=Version+1,ExpectedJson=@expected WHERE OperationId=@op",db,tx)){cmd.Parameters.AddWithValue("@active",!op.Active);cmd.Parameters.AddWithValue("@expected",HistoryActions.Serialize(resulting));cmd.Parameters.AddWithValue("@op",op.Id);await cmd.ExecuteNonQueryAsync();}
        await Audit(db,tx,scope,op.Active?"Revert content edit":"Reapply content edit",op.Name,op.Id,current,resulting);
    }
    internal static async Task RestoreRetained(MySqlConnection db,MySqlTransaction tx,ContentScope scope,ItemSnapshot before,ItemSnapshot after,ItemSnapshot target)
    {
        foreach(string table in (scope.Kind is "Clone" or "NewSet" or "SetMembers" or "ItemDrops"?Array.Empty<string>():scope.Kind=="Set"?new[]{"item_set_spell"}:scope.Kind=="Item"?new[]{"item_sparse","item"}:new[]{"item_effect","item_x_item_effect"})){
            var ids=before.Tables[table].Concat(after.Tables[table]).Select(r=>int.Parse(r["ID"]!)).Concat(scope.Kind=="Set"||table=="item_x_item_effect"?scope.RowIds:scope.EffectIds).Distinct();
            foreach(int id in ids){
                var oldRows=before.Tables[table].Where(r=>r["ID"]==id.ToString()).ToList();var newRows=after.Tables[table].Where(r=>r["ID"]==id.ToString()).ToList();
                uint hash=TooltipData.Hash(Tables[table]);
                var oldRegs=before.Tables["hotfix_data"].Where(r=>r["TableHash"]==hash.ToString()&&r["RecordId"]==id.ToString()).ToList();var newRegs=after.Tables["hotfix_data"].Where(r=>r["TableHash"]==hash.ToString()&&r["RecordId"]==id.ToString()).ToList();
                if(JsonSerializer.Serialize(oldRows)==JsonSerializer.Serialize(newRows)&&JsonSerializer.Serialize(oldRegs)==JsonSerializer.Serialize(newRegs))continue;
                using(var delete=new MySqlCommand($"DELETE FROM {table} WHERE ID=@id",db,tx)){delete.Parameters.AddWithValue("@id",id);await delete.ExecuteNonQueryAsync();}
                foreach(var row in target.Tables[table].Where(r=>r["ID"]==id.ToString()))await Upsert(db,tx,table,row);
                var reg=target.Tables["hotfix_data"].Where(r=>r["TableHash"]==hash.ToString()&&r["RecordId"]==id.ToString()).OrderByDescending(r=>long.Parse(r["Id"]!)).ThenByDescending(r=>long.Parse(r["UniqueId"]!)).FirstOrDefault();
                // A previously unregistered native row remains valid; a newly allocated row is removed.
                bool native=(table is "item_effect" or "item"?scope.NativeEffectIds:scope.NativeRowIds).Contains(id);
                int status=reg is not null?int.Parse(reg["Status"]!):native?1:target.Tables[table].Any(r=>r["ID"]==id.ToString())?3:2;
                await Register(db,tx,table,id,status);
            }
        }
        if(scope.Creation is not null)await SetCreation.Restore(db,tx,scope.Creation,target);
        if(scope.Drops is not null)await WorldDrops.Restore(db,tx,scope.Drops,target);
        if(scope.CopyScope is not null)await ItemCloning.RestoreAsync(db,tx,scope,target);
        if(scope.Bonus is not null)await BonusSpells.RestoreAsync(db,tx,scope.Bonus,target);
    }

}
