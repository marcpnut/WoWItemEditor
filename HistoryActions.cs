using System.Globalization;
using System.IO;
using System.Text.Json;
using MySqlConnector;
namespace AnoWowItemEditor;

internal sealed record ItemSnapshot(Dictionary<string,List<Dictionary<string,string?>>> Tables,Dictionary<string,string>? OriginalItem=null);
internal sealed record OverrideState(int Version,bool Active,string BaselineJson,string ModifiedJson,string ExpectedJson,string LastEvent);
internal sealed record HistoryPlan(int Id,int Build,bool Reapply,string Name,ItemSnapshot Expected,ItemSnapshot Target,ItemSnapshot Modified,int Version,string? Parent,string Fingerprint,string Review,Dictionary<string,string>? Native);
internal sealed record HistoryActionResult(string EventId,ItemSnapshot Before,ItemSnapshot After,int Version,bool Active);

internal static class HistoryActions
{
    internal const string Archive="anocore_studio_backup.item_editor_override_states";
    private static readonly string[] DependencyTables=["item_sparse","item","item_x_item_effect","item_effect","hotfix_data"];
    internal static string Serialize(ItemSnapshot snapshot)=>JsonSerializer.Serialize(snapshot);
    internal static string Canonical(ItemSnapshot snapshot)=>JsonSerializer.Serialize(snapshot.Tables.OrderBy(t=>t.Key,StringComparer.Ordinal).Select(t=>new{Table=t.Key,Rows=t.Value.Select(r=>JsonSerializer.Serialize(r.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new{p.Key,p.Value}))).OrderBy(r=>r,StringComparer.Ordinal)}));
    private static ItemSnapshot Decode(string json)=>JsonSerializer.Deserialize<ItemSnapshot>(json)??throw new InvalidOperationException("Saved override snapshot is invalid.");
    internal static List<FieldComparison> ReviewFields(HistoryPlan plan)
    {
        CatalogItem? Item(ItemSnapshot snapshot,bool active)=>!active?plan.Native is not null?new CatalogItem(plan.Native):null:HotfixHistory.SnapshotItem(Serialize(snapshot),plan.Id,true);
        var current=Item(plan.Expected,!plan.Reapply);var target=Item(plan.Target,plan.Reapply);
        var fields=target is null?new List<FieldComparison>():OverrideEntry.Compare(current,target).Where(f=>f.Changed||!f.Known).ToList();
        fields.Insert(0,new("Override state",plan.Reapply?"Inactive":"Active",plan.Reapply?"Active":"Inactive",true,true));return fields;
    }
    private static string NativeFingerprint()=>string.Join("/",new[]{"ItemSparse","Item","ItemXItemEffect","ItemEffect"}.Select(n=>{var f=new FileInfo(Path.Combine(CatalogRepository.Db2Folder,n+".db2"));return $"{f.Length}:{f.LastWriteTimeUtc.Ticks}";}));
    internal static async Task InitializeAsync(MySqlConnection db)
    {
        using(var create=new MySqlCommand("CREATE DATABASE IF NOT EXISTS anocore_studio_backup CHARACTER SET utf8mb4",db))await create.ExecuteNonQueryAsync();
        const string audit="CREATE TABLE IF NOT EXISTS foreverclassic_hotfixes.anocore_studio_changes (ChangeId CHAR(36) NOT NULL PRIMARY KEY,ChangedUtc DATETIME(6) NOT NULL,Actor VARCHAR(255) NOT NULL,EntityType VARCHAR(32) NOT NULL,EntityId INT NOT NULL,Action VARCHAR(64) NOT NULL,Summary TEXT NOT NULL,BeforeJson LONGTEXT NOT NULL,AfterJson LONGTEXT NOT NULL,Provenance VARCHAR(255) NOT NULL,SourceKey VARCHAR(255) NULL,UNIQUE KEY UX_StudioSource (SourceKey),KEY IX_StudioEntity(EntityType,EntityId),KEY IX_StudioTime(ChangedUtc)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4";
        using(var create=new MySqlCommand(audit,db))await create.ExecuteNonQueryAsync();
        using(var create=new MySqlCommand("CREATE TABLE IF NOT EXISTS anocore_studio_backup.changes LIKE foreverclassic_hotfixes.anocore_studio_changes",db))await create.ExecuteNonQueryAsync();
        using(var create=new MySqlCommand($"CREATE TABLE IF NOT EXISTS {Archive} (ItemId INT NOT NULL,PayloadBuild INT NOT NULL,Version INT NOT NULL,IsActive TINYINT NOT NULL,BaselineJson LONGTEXT NOT NULL,ModifiedJson LONGTEXT NOT NULL,ExpectedJson LONGTEXT NOT NULL,LastEventId CHAR(36) NOT NULL,UpdatedUtc DATETIME(6) NOT NULL,PRIMARY KEY(ItemId,PayloadBuild)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",db))await create.ExecuteNonQueryAsync();
        using(var create=new MySqlCommand("CREATE TABLE IF NOT EXISTS anocore_studio_backup.item_editor_hidden_history (ItemId INT NOT NULL,PayloadBuild INT NOT NULL,RemovedUtc DATETIME(6) NOT NULL,Actor VARCHAR(255) NOT NULL,PRIMARY KEY(ItemId,PayloadBuild)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",db))await create.ExecuteNonQueryAsync();
        foreach(string name in DependencyTables.Select(t=>"foreverclassic_hotfixes."+t).Concat(new[]{Archive,"foreverclassic_hotfixes.anocore_studio_changes","anocore_studio_backup.changes","anocore_studio_backup.item_editor_hidden_history"})) {
            var parts=name.Split('.');using var check=new MySqlCommand("SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA=@schema AND TABLE_NAME=@table",db);check.Parameters.AddWithValue("@schema",parts[0]);check.Parameters.AddWithValue("@table",parts[1]);
            if(!string.Equals(Convert.ToString(await check.ExecuteScalarAsync()),"InnoDB",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException($"{name} must support transactions before revert/reapply can run.");
        }
    }
    private static async Task<List<Dictionary<string,string?>>> Rows(MySqlConnection db,MySqlTransaction? tx,string sql,int id)
    {
        using var cmd=new MySqlCommand(sql,db,tx);cmd.Parameters.AddWithValue("@id",id);using var r=await cmd.ExecuteReaderAsync();var rows=new List<Dictionary<string,string?>>();
        while(await r.ReadAsync()){var row=new Dictionary<string,string?>(StringComparer.OrdinalIgnoreCase);for(int n=0;n<r.FieldCount;n++)row[r.GetName(n)]=r.IsDBNull(n)?null:Convert.ToString(r.GetValue(n),CultureInfo.InvariantCulture);rows.Add(row);}return rows;
    }
    internal static async Task<ItemSnapshot> CaptureAsync(MySqlConnection db,MySqlTransaction? tx,int id,bool locked=false)
    {
        string suffix=locked?" FOR UPDATE":"";
        var tables=new Dictionary<string,List<Dictionary<string,string?>>>();
        tables["item_sparse"]=await Rows(db,tx,"SELECT * FROM item_sparse WHERE ID=@id ORDER BY VerifiedBuild"+suffix,id);
        tables["item"]=await Rows(db,tx,"SELECT * FROM item WHERE ID=@id ORDER BY VerifiedBuild"+suffix,id);
        tables["item_x_item_effect"]=await Rows(db,tx,"SELECT * FROM item_x_item_effect WHERE ItemID=@id ORDER BY ID,VerifiedBuild"+suffix,id);
        tables["item_effect"]=await Rows(db,tx,"SELECT * FROM item_effect WHERE ID IN(SELECT ItemEffectID FROM item_x_item_effect WHERE ItemID=@id) ORDER BY ID,VerifiedBuild"+suffix,id);
        tables["hotfix_data"]=await Rows(db,tx,$"SELECT * FROM hotfix_data WHERE (TableHash={TooltipData.Hash("ItemSparse")} AND RecordId=@id) OR (TableHash={TooltipData.Hash("Item")} AND RecordId=@id) OR (TableHash={TooltipData.Hash("ItemXItemEffect")} AND RecordId IN(SELECT ID FROM item_x_item_effect WHERE ItemID=@id)) OR (TableHash={TooltipData.Hash("ItemEffect")} AND RecordId IN(SELECT ItemEffectID FROM item_x_item_effect WHERE ItemID=@id)) ORDER BY Id,UniqueId"+suffix,id);
        return new(tables);
    }
    internal static async Task<OverrideState?> StateAsync(MySqlConnection db,MySqlTransaction? tx,int id,int build,bool locked=false)
    {
        using var cmd=new MySqlCommand($"SELECT Version,IsActive,BaselineJson,ModifiedJson,ExpectedJson,LastEventId FROM {Archive} WHERE ItemId=@id AND PayloadBuild=@build"+(locked?" FOR UPDATE":""),db,tx);cmd.Parameters.AddWithValue("@id",id);cmd.Parameters.AddWithValue("@build",build);using var r=await cmd.ExecuteReaderAsync();
        return await r.ReadAsync()?new(Convert.ToInt32(r[0]),Convert.ToBoolean(r[1]),r.GetString(2),r.GetString(3),r.GetString(4),Convert.ToString(r[5],CultureInfo.InvariantCulture)!):null;
    }
    private static int LatestStatus(ItemSnapshot snapshot,int id)
    {
        var row=snapshot.Tables["hotfix_data"].Where(r=>r.GetValueOrDefault("TableHash")==TooltipData.Hash("ItemSparse").ToString(CultureInfo.InvariantCulture)&&r.GetValueOrDefault("RecordId")==id.ToString(CultureInfo.InvariantCulture)).OrderByDescending(r=>long.Parse(r["Id"]??r["ID"]!,CultureInfo.InvariantCulture)).FirstOrDefault();
        return row is null?0:int.Parse(row["Status"]!,CultureInfo.InvariantCulture);
    }
    internal static async Task<HistoryPlan> PrepareAsync(OverrideEntry entry,Dictionary<int,CatalogItem> originals)
    {
        using var db=await DatabaseConnection.OpenAsync(CatalogRepository.Database);await InitializeAsync(db);
        using var tx=await db.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var plan=await PrepareCoreAsync(db,tx,entry,originals);await tx.CommitAsync();return plan;
    }
    internal static async Task<HistoryPlan> PrepareCoreAsync(MySqlConnection db,MySqlTransaction tx,OverrideEntry entry,Dictionary<int,CatalogItem> originals)
    {
        var state=await StateAsync(db,tx,entry.Id,entry.Build);
        var current=await CaptureAsync(db,tx,entry.Id);
        bool reapply=!entry.Active;
        if(state is not null) {
            if(state.Active!=entry.Active||Canonical(current)!=Canonical(Decode(state.ExpectedJson)))throw new InvalidOperationException("This item changed after its saved action. Refresh history; newer changes were preserved.");
        } else {
            var selected=current.Tables["item_sparse"].SingleOrDefault(r=>r["VerifiedBuild"]==entry.Build.ToString(CultureInfo.InvariantCulture));
            if(selected is null||selected.Any(p=>!entry.ModifiedItem.Fields.TryGetValue(p.Key,out var value)||!OverrideEntry.Equivalent(p.Value,value)))throw new InvalidOperationException("The stored override differs from the displayed version. Refresh history before applying an action.");
            if(entry.Active&&LatestStatus(current,entry.Id)!=1)throw new InvalidOperationException("The override is no longer active. Refresh history.");
            if(!entry.Active&&LatestStatus(current,entry.Id)==1)throw new InvalidOperationException("Another version is active. Refresh history before reapplying.");
        }
        var native=originals.GetValueOrDefault(entry.Id);bool hasNative=native?.Fields.ContainsKey("Display")==true;
        ItemSnapshot target,modified;
        if(state is not null){target=reapply?Decode(state.ModifiedJson):Decode(state.BaselineJson);modified=Decode(state.ModifiedJson);}
        else if(reapply) {
            if(current.Tables["item_sparse"].Count!=1)throw new InvalidOperationException("Multiple database payload versions require an explicit version conflict review before reapply.");
            if(LatestStatus(current,entry.Id)==0&&!hasNative)throw new InvalidOperationException("The original client definition and registration are unavailable, so a reversible activation cannot be guaranteed.");
            target=current;modified=current;
        } else {
            if(!hasNative)throw new InvalidOperationException("No original client definition is available. Revert is blocked because removing this server-supplied definition could remove the item entirely.");
            if(current.Tables["item_sparse"].Count!=1)throw new InvalidOperationException("Other database payload versions exist. Revert cannot safely select the intended original automatically.");
            var tables=current.Tables.ToDictionary(p=>p.Key,p=>p.Value);tables["item_sparse"]=[];target=new(tables,native!.Fields);modified=current;
        }
        string review=(reapply?"Activate the retained item-field override.":"Restore the previous item-field state.")+"\n\n"+(reapply?"The retained modified version will become active.":target.Tables["item_sparse"].Count==0?"The installed original client definition will be used.":"The saved inactive payload and registration state will be restored.")+"\n\nItem identity, item-effect links and shared spell/effect definitions are retained. Before/after snapshots and a new activity event are saved together. Server reload and client verification are still needed after applying.";
        if(hasNative&&(current.Tables["item_sparse"].Count==0||reapply))current=current with{OriginalItem=native!.Fields};
        return new(entry.Id,entry.Build,reapply,entry.Name,current,target,modified,state?.Version??0,state?.LastEvent,NativeFingerprint(),review,hasNative?native!.Fields:null);
    }
    internal static async Task<HistoryActionResult> ExecuteAsync(HistoryPlan plan)
    {
        using var db=await DatabaseConnection.OpenAsync(CatalogRepository.Database);await InitializeAsync(db);
        using(var gate=new MySqlCommand("SELECT GET_LOCK('anocore_studio_forge',8)",db))if(Convert.ToInt32(await gate.ExecuteScalarAsync())!=1)throw new InvalidOperationException("Another editor is saving. Retry after it finishes.");
        try {
            using var tx=await db.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            try{var result=await ApplyCoreAsync(db,tx,plan);await tx.CommitAsync();return result;}catch{await tx.RollbackAsync();throw;}
        }finally{using var release=new MySqlCommand("SELECT RELEASE_LOCK('anocore_studio_forge')",db);await release.ExecuteScalarAsync();}
    }
    internal static async Task SetHiddenAsync(OverrideEntry entry,bool hidden)
    {
        using var db=await DatabaseConnection.OpenAsync(CatalogRepository.Database);await InitializeAsync(db);
        using(var gate=new MySqlCommand("SELECT GET_LOCK('anocore_studio_forge',8)",db))if(Convert.ToInt32(await gate.ExecuteScalarAsync())!=1)throw new InvalidOperationException("Another editor is saving. Retry after it finishes.");
        try{using var tx=await db.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);try{await SetHiddenCoreAsync(db,tx,entry,hidden);await tx.CommitAsync();}catch{await tx.RollbackAsync();throw;}}
        finally{using var release=new MySqlCommand("SELECT RELEASE_LOCK('anocore_studio_forge')",db);await release.ExecuteScalarAsync();}
    }
    internal static async Task SetHiddenCoreAsync(MySqlConnection db,MySqlTransaction tx,OverrideEntry entry,bool hidden)
    {
        var current=await CaptureAsync(db,tx,entry.Id,true);
        var selected=current.Tables["item_sparse"].OrderBy(r=>r["VerifiedBuild"]=="0"?0:1).ThenByDescending(r=>int.Parse(r["VerifiedBuild"]!,CultureInfo.InvariantCulture)).FirstOrDefault();
        bool active=LatestStatus(current,entry.Id)==1&&selected?.GetValueOrDefault("VerifiedBuild")==entry.Build.ToString(CultureInfo.InvariantCulture);
        if(hidden&&(entry.Active||active))throw new InvalidOperationException("Active overrides cannot be removed from history. Refresh and revert the override first.");
        using(var cmd=new MySqlCommand(hidden?"INSERT INTO anocore_studio_backup.item_editor_hidden_history (ItemId,PayloadBuild,RemovedUtc,Actor) VALUES(@id,@build,UTC_TIMESTAMP(6),@actor) ON DUPLICATE KEY UPDATE RemovedUtc=UTC_TIMESTAMP(6),Actor=@actor":"DELETE FROM anocore_studio_backup.item_editor_hidden_history WHERE ItemId=@id AND PayloadBuild=@build",db,tx)){
            cmd.Parameters.AddWithValue("@id",entry.Id);cmd.Parameters.AddWithValue("@build",entry.Build);cmd.Parameters.AddWithValue("@actor",Environment.UserDomainName+"\\"+Environment.UserName);await cmd.ExecuteNonQueryAsync();
        }
        string eventId=Guid.NewGuid().ToString();
        using(var audit=new MySqlCommand("INSERT INTO anocore_studio_changes (ChangeId,ChangedUtc,Actor,EntityType,EntityId,Action,Summary,BeforeJson,AfterJson,Provenance,SourceKey) VALUES(@event,UTC_TIMESTAMP(6),@actor,'Item',@id,@action,@summary,@snapshot,@snapshot,'AnoWoW Item Editor history visibility',@key)",db,tx)){
            audit.Parameters.AddWithValue("@event",eventId);audit.Parameters.AddWithValue("@actor",Environment.UserDomainName+"\\"+Environment.UserName);audit.Parameters.AddWithValue("@id",entry.Id);audit.Parameters.AddWithValue("@action",hidden?"Remove from history":"Restore to history");audit.Parameters.AddWithValue("@summary",$"{entry.Name}; payload build {entry.Build}. History visibility changed; game data retained.");audit.Parameters.AddWithValue("@snapshot",Serialize(current));audit.Parameters.AddWithValue("@key","ItemEditor:"+eventId);await audit.ExecuteNonQueryAsync();
        }
        using(var mirror=new MySqlCommand("INSERT INTO anocore_studio_backup.changes SELECT * FROM anocore_studio_changes WHERE ChangeId=@event",db,tx)){mirror.Parameters.AddWithValue("@event",eventId);await mirror.ExecuteNonQueryAsync();}
    }
    internal static async Task<HistoryActionResult> ApplyCoreAsync(MySqlConnection db,MySqlTransaction tx,HistoryPlan plan)
    {
        if(NativeFingerprint()!=plan.Fingerprint)throw new InvalidOperationException("The installed client definitions changed since review. Refresh before applying.");
        var before=await CaptureAsync(db,tx,plan.Id,true);
        var state=await StateAsync(db,tx,plan.Id,plan.Build,true);
        if((state?.Version??0)!=plan.Version||Canonical(before)!=Canonical(plan.Expected))throw new InvalidOperationException("Conflict: the item changed after review. No changes were applied.");
        var target=plan.Reapply?plan.Modified:plan.Target;
        var targetRows=target.Tables["item_sparse"];
        // Only item_sparse is restored. Dependency snapshots protect references but are never written.
        if(JsonSerializer.Serialize(before.Tables["item_sparse"])!=JsonSerializer.Serialize(targetRows)) {
            using(var remove=new MySqlCommand("DELETE FROM item_sparse WHERE ID=@id",db,tx)){remove.Parameters.AddWithValue("@id",plan.Id);await remove.ExecuteNonQueryAsync();}
            var columns=await Rows(db,tx,"SELECT COLUMN_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='item_sparse'",plan.Id);
            var allowed=columns.Select(r=>r["COLUMN_NAME"]!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach(var row in targetRows) {
                if(!allowed.SetEquals(row.Keys)||row.GetValueOrDefault("ID")!=plan.Id.ToString(CultureInfo.InvariantCulture))throw new InvalidOperationException("Saved payload does not match the current item schema.");
                var keys=row.Keys.ToArray();using var insert=new MySqlCommand($"INSERT INTO item_sparse ({string.Join(',',keys.Select(k=>$"`{k}`"))}) VALUES ({string.Join(',',keys.Select((_,n)=>"@v"+n))})",db,tx);
                for(int n=0;n<keys.Length;n++)insert.Parameters.AddWithValue("@v"+n,(object?)row[keys[n]]??DBNull.Value);await insert.ExecuteNonQueryAsync();
            }
        }
        int status=plan.Reapply?1:targetRows.Count==0?1:LatestStatus(target,plan.Id);
        if(status==0)status=3;
        long next;
        using(var max=new MySqlCommand("SELECT COALESCE(MAX(Id),0)+1 FROM hotfix_data FOR UPDATE",db,tx))next=Convert.ToInt64(await max.ExecuteScalarAsync());
        if(next>int.MaxValue)throw new InvalidOperationException("Hotfix push IDs are exhausted; no action was applied.");
        using(var push=new MySqlCommand("INSERT INTO hotfix_data (Id,UniqueId,TableHash,RecordId,Status,VerifiedBuild) VALUES(@push,@push,@hash,@id,@status,70450)",db,tx)) {
            push.Parameters.AddWithValue("@push",next);push.Parameters.AddWithValue("@hash",TooltipData.Hash("ItemSparse"));push.Parameters.AddWithValue("@id",plan.Id);push.Parameters.AddWithValue("@status",status);await push.ExecuteNonQueryAsync();
        }
        var after=await CaptureAsync(db,tx,plan.Id,true);int version=plan.Version+1;string eventId=Guid.NewGuid().ToString();
        using(var visible=new MySqlCommand("DELETE FROM anocore_studio_backup.item_editor_hidden_history WHERE ItemId=@id AND PayloadBuild=@build",db,tx)){visible.Parameters.AddWithValue("@id",plan.Id);visible.Parameters.AddWithValue("@build",plan.Build);await visible.ExecuteNonQueryAsync();}
        string baseline=state?.BaselineJson??Serialize(plan.Reapply?before:plan.Target),modified=state?.ModifiedJson??Serialize(plan.Modified);
        using(var save=new MySqlCommand($"INSERT INTO {Archive} (ItemId,PayloadBuild,Version,IsActive,BaselineJson,ModifiedJson,ExpectedJson,LastEventId,UpdatedUtc) VALUES(@id,@build,@version,@active,@baseline,@modified,@expected,@event,UTC_TIMESTAMP(6)) ON DUPLICATE KEY UPDATE Version=@version,IsActive=@active,ExpectedJson=@expected,LastEventId=@event,UpdatedUtc=UTC_TIMESTAMP(6)",db,tx)) {
            save.Parameters.AddWithValue("@id",plan.Id);save.Parameters.AddWithValue("@build",plan.Build);save.Parameters.AddWithValue("@version",version);save.Parameters.AddWithValue("@active",plan.Reapply);save.Parameters.AddWithValue("@baseline",baseline);save.Parameters.AddWithValue("@modified",modified);save.Parameters.AddWithValue("@expected",Serialize(after));save.Parameters.AddWithValue("@event",eventId);await save.ExecuteNonQueryAsync();
        }
        string action=plan.Reapply?"Reapply item override":"Revert item override";
        using(var audit=new MySqlCommand("INSERT INTO foreverclassic_hotfixes.anocore_studio_changes (ChangeId,ChangedUtc,Actor,EntityType,EntityId,Action,Summary,BeforeJson,AfterJson,Provenance,SourceKey) VALUES(@event,UTC_TIMESTAMP(6),@actor,'Item',@id,@action,@summary,@before,@after,@source,@key)",db,tx)) {
            audit.Parameters.AddWithValue("@event",eventId);audit.Parameters.AddWithValue("@actor",Environment.UserDomainName+"\\"+Environment.UserName);audit.Parameters.AddWithValue("@id",plan.Id);audit.Parameters.AddWithValue("@action",action);audit.Parameters.AddWithValue("@summary",$"{action}: {plan.Name}; payload build {plan.Build}; saved version {version}. Server reload pending.");audit.Parameters.AddWithValue("@before",Serialize(before));audit.Parameters.AddWithValue("@after",Serialize(after));audit.Parameters.AddWithValue("@source","AnoWoW Item Editor; parent "+(plan.Parent??"initial database record"));audit.Parameters.AddWithValue("@key","ItemEditor:"+eventId);await audit.ExecuteNonQueryAsync();
        }
        using(var mirror=new MySqlCommand("INSERT INTO anocore_studio_backup.changes SELECT * FROM foreverclassic_hotfixes.anocore_studio_changes WHERE ChangeId=@event",db,tx)){mirror.Parameters.AddWithValue("@event",eventId);await mirror.ExecuteNonQueryAsync();}
        return new(eventId,before,after,version,plan.Reapply);
    }
}
