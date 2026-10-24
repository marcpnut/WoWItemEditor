using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using DBCD.Providers;
using MySqlConnector;
namespace AnoWowItemEditor;

internal sealed record BonusTable(string Sql,string Dbc,bool ParentSpell);
internal sealed record BonusScope(int[] SpellIds,Dictionary<string,int[]> Records,int[] OwnedSpells,string Fingerprint,List<BonusTable> Tables,int[]? EditedEffects=null);
internal sealed record BonusBlueprint(int Root,BonusScope Scope,ItemSnapshot Expected,Dictionary<string,List<Dictionary<string,string>>> Rows,Dictionary<string,int> MaxIds,Dictionary<int,Dictionary<string,string>> Texts);
internal sealed record BonusDraft(BonusBlueprint Source,Dictionary<int,string> Values)
{
    internal string Description(TooltipData data){var copy=ContentEditorWindow.PreviewData(data,data);int id=BonusSpells.Preview(this,copy,2000000000);return copy.SpellDescription(id);}
}
internal static class BonusSpells
{
    private static int N(Dictionary<string,string> row,string name)=>TooltipData.Number(row,name);
    internal static string Fingerprint(IEnumerable<BonusTable> tables)=>string.Join("/",tables.Select(t=>t.Dbc).Append("Spell").Distinct().Order().Select(n=>{var f=new FileInfo(Path.Combine(CatalogRepository.Db2Folder,n+".db2"));return $"{n}:{f.Length}:{f.LastWriteTimeUtc.Ticks}";}));
    private static string Ids(IEnumerable<int> ids)=>string.Join(',',ids.Distinct().DefaultIfEmpty(-1));
    private static string DbcName(string sql)=>string.Concat(sql.Split('_').Select(s=>char.ToUpperInvariant(s[0])+s[1..]));
    private static int NativeMax(string dbc){using var f=File.OpenRead(Path.Combine(CatalogRepository.Db2Folder,dbc+".db2"));var h=new byte[168];f.ReadExactly(h);return checked((int)BitConverter.ToUInt32(h,164));}
    private static async Task<List<Dictionary<string,string?>>> Query(MySqlConnection db,MySqlTransaction? tx,string sql)
    {
        using var cmd=new MySqlCommand(sql,db,tx);using var r=await cmd.ExecuteReaderAsync();var rows=new List<Dictionary<string,string?>>();while(await r.ReadAsync()){var row=new Dictionary<string,string?>(StringComparer.OrdinalIgnoreCase);for(int n=0;n<r.FieldCount;n++)row[r.GetName(n)]=r.IsDBNull(n)?null:r.GetValue(n) is byte[] bytes?Convert.ToHexString(bytes):Convert.ToString(r.GetValue(n),CultureInfo.InvariantCulture);rows.Add(row);}return rows;
    }
    internal static async Task<BonusBlueprint> LoadAsync(int root)
    {
        using var db=await DatabaseConnection.OpenAsync(CatalogRepository.Database);
        var schemas=await Query(db,null,"SELECT DISTINCT TABLE_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA='foreverclassic_hotfixes' AND COLUMN_NAME='SpellID' ORDER BY TABLE_NAME");
        var tables=schemas.Select(r=>r["TABLE_NAME"]!).Where(n=>n.StartsWith("spell_")).Select(n=>new BonusTable(n,DbcName(n),true)).Where(t=>File.Exists(Path.Combine(CatalogRepository.Db2Folder,t.Dbc+".db2"))).Prepend(new("spell_name","SpellName",false)).ToList();
        var allNames=schemas.Select(r=>r["TABLE_NAME"]!).Where(n=>n.StartsWith("spell_")).ToHashSet();if(allNames.Except(tables.Select(t=>t.Sql)).Any())throw new InvalidOperationException("Some spell dependencies have no installed definition; isolated value editing is unavailable for this client layout.");
        string fingerprint=Fingerprint(tables);var reader=new DBCD.DBCD(new FilesystemDBCProvider(CatalogRepository.Db2Folder),new GithubDBDProvider(true));
        var effects=await Task.Run(()=>TooltipData.LoadTable(reader,"SpellEffect"));
        var texts=await Task.Run(()=>TooltipData.LoadTable(reader,"Spell"));
        using var tx=await db.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        await TooltipData.Overlay(db,"spell_effect","SpellEffect",effects,tx);await SpellBlobs.OverlayAsync(db,texts,tx);
        var ids=new HashSet<int>{root};
        for(int pass=0;pass<16;pass++){
            int count=ids.Count;foreach(var row in effects.Values.Where(r=>ids.Contains(N(r,"SpellID"))).ToArray())if(N(row,"EffectTriggerSpell")>0)ids.Add(N(row,"EffectTriggerSpell"));
            foreach(int id in ids.ToArray())if(texts.TryGetValue(id,out var row))foreach(Match m in Regex.Matches(TooltipData.Read(row,"Description")+TooltipData.Read(row,"AuraDescription"),@"\$(\d+)[a-zA-Z]"))ids.Add(int.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture));
            if(ids.Count>16)throw new InvalidOperationException("This bonus references more than 16 spells. Its scripted chain needs dedicated support before isolated editing.");if(ids.Count==count)break;
        }
        if(ids.Any(id=>!texts.ContainsKey(id)))throw new InvalidOperationException("The full spell text for this bonus chain is unavailable; cloning is blocked.");
        foreach(var (table,column) in new[]{("spell_script_names","spell_id"),("spell_linked_spell","spell_trigger"),("spell_area","spell"),("spell_custom_attr","spell_id"),("spell_target_position","ID"),("spell_threat","entry")}){
            if(!await HasColumn(db,tx,"foreverclassic_world",table,column))continue;var special=await Query(db,tx,$"SELECT * FROM foreverclassic_world.{table} WHERE ABS(`{column}`) IN({Ids(ids)})");if(special.Count>0)throw new InvalidOperationException($"This bonus uses {table}. A set-specific copy needs dedicated support for those server rules. Its original spell was preserved.");
        }
        var rows=new Dictionary<string,List<Dictionary<string,string>>>();var max=new Dictionary<string,int>();
        foreach(var table in tables){
            var native=table.Sql=="spell_effect"?effects:await Task.Run(()=>TooltipData.LoadTable(reader,table.Dbc,r=>ids.Contains(N(r,table.ParentSpell?"SpellID":"ID"))));
            if(table.Sql!="spell_effect")await TooltipData.Overlay(db,table.Sql,table.Dbc,native,tx);
            rows[table.Sql]=native.Values.Where(r=>ids.Contains(N(r,table.ParentSpell?"SpellID":"ID"))).Select(r=>new Dictionary<string,string>(r,StringComparer.OrdinalIgnoreCase)).ToList();
            max[table.Sql]=Math.Max(NativeMax(table.Dbc),native.Keys.DefaultIfEmpty().Max());
        }
        if(rows["spell_name"].All(r=>N(r,"ID")!=root)||rows["spell_effect"].All(r=>N(r,"SpellID")!=root))throw new InvalidOperationException("The bonus has no complete spell/effect definition.");
        var records=rows.ToDictionary(t=>t.Key,t=>t.Value.Select(r=>N(r,"ID")).ToArray());var scope=new BonusScope(ids.ToArray(),records,[],fingerprint,tables);
        var expected=await CaptureAsync(db,tx,scope);await tx.CommitAsync();if(fingerprint!=Fingerprint(tables))throw new InvalidOperationException("Client spell definitions changed while loading.");
        return new(root,scope,expected,rows,max,texts.Where(p=>ids.Contains(p.Key)).ToDictionary(p=>p.Key,p=>p.Value));
    }
    private static async Task<bool> HasColumn(MySqlConnection db,MySqlTransaction tx,string schema,string table,string column){using var cmd=new MySqlCommand("SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=@s AND TABLE_NAME=@t AND COLUMN_NAME=@c",db,tx);cmd.Parameters.AddWithValue("@s",schema);cmd.Parameters.AddWithValue("@t",table);cmd.Parameters.AddWithValue("@c",column);return Convert.ToInt32(await cmd.ExecuteScalarAsync())>0;}
    internal static async Task<ItemSnapshot> CaptureAsync(MySqlConnection db,MySqlTransaction? tx,BonusScope scope,bool locked=false)
    {
        string suffix=locked?" FOR UPDATE":"";var rows=new Dictionary<string,List<Dictionary<string,string?>>>();var clauses=new List<string>();
        foreach(var t in scope.Tables){
            var ids=scope.Records.GetValueOrDefault(t.Sql)??[];
            rows["bonus_"+t.Sql]=await Query(db,tx,$"SELECT * FROM {t.Sql} WHERE {(t.ParentSpell?"SpellID":"ID")} IN({Ids(scope.SpellIds)}) OR ID IN({Ids(ids)}) ORDER BY ID,VerifiedBuild"+suffix);
            clauses.Add($"(TableHash={TooltipData.Hash(t.Dbc)} AND RecordId IN({Ids(ids.Concat(rows["bonus_"+t.Sql].Select(r=>int.Parse(r["ID"]!))))}))");
        }
        rows["bonus_blob"]=await Query(db,tx,$"SELECT * FROM hotfix_blob WHERE TableHash={TooltipData.Hash("Spell")} AND RecordId IN({Ids(scope.SpellIds)}) ORDER BY RecordId,locale,VerifiedBuild"+suffix);
        rows["bonus_proc"]=await Query(db,tx,$"SELECT * FROM foreverclassic_world.spell_proc WHERE ABS(SpellId) IN({Ids(scope.SpellIds)}) ORDER BY SpellId"+suffix);
        clauses.Add($"(TableHash={TooltipData.Hash("Spell")} AND RecordId IN({Ids(scope.SpellIds)}))");
        rows["bonus_registrations"]=await Query(db,tx,$"SELECT * FROM hotfix_data WHERE {string.Join(" OR ",clauses)} ORDER BY Id,UniqueId"+suffix);
        rows["bonus_external_sets"]=await Query(db,tx,$"SELECT * FROM item_set_spell WHERE SpellID IN({Ids(scope.OwnedSpells)}) ORDER BY ID,VerifiedBuild"+suffix);
        rows["bonus_external_items"]=await Query(db,tx,$"SELECT * FROM item_effect WHERE SpellID IN({Ids(scope.OwnedSpells)}) ORDER BY ID,VerifiedBuild"+suffix);
        rows["bonus_external_triggers"]=await Query(db,tx,$"SELECT * FROM spell_effect WHERE EffectTriggerSpell IN({Ids(scope.OwnedSpells)}) AND SpellID NOT IN({Ids(scope.OwnedSpells)}) ORDER BY ID,VerifiedBuild"+suffix);
        return new(rows);
    }
    internal static bool FixedAmount(Dictionary<string,string> row)=>new[]{"ScalingClass","Coefficient","Variance","EffectRealPointsPerLevel","EffectBonusCoefficient","BonusCoefficientFromAP","EffectPointsPerResource","ResourceCoefficient"}.All(k=>TooltipData.Amount(row,k)==0);
    internal static bool Editable(Dictionary<string,string> row)=>BonusValueRules.For(row).Supported;
    internal static void Validate(BonusDraft draft)
    {
        foreach(var (id,value) in draft.Values){var row=draft.Source.Rows["spell_effect"].SingleOrDefault(r=>N(r,"ID")==id);if(row is null)throw new InvalidOperationException("The edited effect does not belong to this bonus.");BonusValueRules.Validate(row,value);}
    }
    private static Dictionary<string,string> RemapText(Dictionary<string,string> source,Dictionary<int,int> map){var copy=new Dictionary<string,string>(source,StringComparer.OrdinalIgnoreCase);foreach(string key in new[]{"Description","AuraDescription"})if(copy.TryGetValue(key,out var text))copy[key]=Regex.Replace(text,@"\$(\d+)(?=[a-zA-Z])",m=>map.TryGetValue(int.Parse(m.Groups[1].Value),out int id)?"$"+id:m.Value);return copy;}
    internal static int Preview(BonusDraft draft,TooltipData data,int seed)
    {
        var map=draft.Source.Scope.SpellIds.Select((id,n)=>(id,target:seed+n)).ToDictionary(p=>p.id,p=>p.target);
        foreach(var (old,id) in map){var text=RemapText(draft.Source.Texts[old],map);text["Name"]=data.SpellName(old);data.Spells[id]=text;}
        foreach(var e in draft.Source.Rows["spell_effect"].Where(r=>N(r,"DifficultyID")==0)){var row=new Dictionary<string,string>(e,StringComparer.OrdinalIgnoreCase);if(draft.Values.TryGetValue(N(row,"ID"),out var value))row["EffectBasePointsF"]=value;data.Effects[(map[N(row,"SpellID")],N(row,"EffectIndex"))]=row;}
        foreach(var m in draft.Source.Rows.GetValueOrDefault("spell_misc")??[])if(N(m,"DifficultyID")==0)data.Misc[map[N(m,"SpellID")]]=m;
        return map[draft.Source.Root];
    }
    private static async Task<int> NextId(MySqlConnection db,MySqlTransaction tx,string table,string dbc,int nativeMax)
    {
        using var cmd=new MySqlCommand($"SELECT GREATEST(COALESCE((SELECT MAX(ID) FROM {table}),0),COALESCE((SELECT MAX(RecordId) FROM hotfix_data WHERE TableHash=@hash),0))",db,tx);cmd.Parameters.AddWithValue("@hash",TooltipData.Hash(dbc));return checked((int)Math.Max(nativeMax,Convert.ToInt64(await cmd.ExecuteScalarAsync()))+1);
    }
    internal static async Task<(int Root,BonusScope Scope)> AllocateAsync(MySqlConnection db,MySqlTransaction tx,BonusDraft draft,Dictionary<string,int> reserved)
    {
        Validate(draft);if(Fingerprint(draft.Source.Scope.Tables)!=draft.Source.Scope.Fingerprint||HistoryActions.Canonical(await CaptureAsync(db,tx,draft.Source.Scope,true))!=HistoryActions.Canonical(draft.Source.Expected))throw new InvalidOperationException("The source bonus spell changed after loading. Reopen its value editor.");
        foreach(var table in draft.Source.Scope.Tables.Select(t=>"foreverclassic_hotfixes."+t.Sql).Concat(new[]{"foreverclassic_hotfixes.hotfix_blob","foreverclassic_world.spell_proc"})){
            var parts=table.Split('.');using var engine=new MySqlCommand("SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA=@s AND TABLE_NAME=@t",db,tx);engine.Parameters.AddWithValue("@s",parts[0]);engine.Parameters.AddWithValue("@t",parts[1]);if(!string.Equals(Convert.ToString(await engine.ExecuteScalarAsync()),"InnoDB",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException(table+" must support transactions before isolated bonus editing.");
        }
        int next=await NextId(db,tx,"spell_name","SpellName",draft.Source.MaxIds["spell_name"]);
        using(var max=new MySqlCommand($"SELECT GREATEST(COALESCE((SELECT MAX(Id) FROM foreverclassic_world.serverside_spell),0),COALESCE((SELECT MAX(RecordId) FROM hotfix_blob WHERE TableHash={TooltipData.Hash("Spell")}),0))",db,tx))next=Math.Max(next,checked(Convert.ToInt32(await max.ExecuteScalarAsync())+1));
        next=Math.Max(next,reserved.GetValueOrDefault("spell_name"));
        var map=draft.Source.Scope.SpellIds.Select((id,n)=>(id,target:checked(next+n))).ToDictionary(p=>p.id,p=>p.target);
        var records=draft.Source.Scope.Records.ToDictionary(p=>p.Key,p=>p.Value.ToList());
        reserved["spell_name"]=checked(next+map.Count);
        foreach(var t in draft.Source.Scope.Tables){int rowId=Math.Max(await NextId(db,tx,t.Sql,t.Dbc,draft.Source.MaxIds[t.Sql]),reserved.GetValueOrDefault(t.Sql));foreach(var row in draft.Source.Rows[t.Sql])records[t.Sql].Add(t.ParentSpell?rowId++:map[N(row,"ID")]);if(t.ParentSpell)reserved[t.Sql]=rowId;}
        var scope=draft.Source.Scope with{SpellIds=draft.Source.Scope.SpellIds.Concat(map.Values).ToArray(),Records=records.ToDictionary(p=>p.Key,p=>p.Value.ToArray()),OwnedSpells=map.Values.ToArray(),EditedEffects=draft.Source.Rows["spell_effect"].Select((r,n)=>(Id:N(r,"ID"),Target:records["spell_effect"].Skip(draft.Source.Scope.Records["spell_effect"].Length).ElementAt(n))).Where(p=>draft.Values.ContainsKey(p.Id)).Select(p=>p.Target).ToArray()};
        return (map[draft.Source.Root],scope);
    }
    internal static async Task ApplyAsync(MySqlConnection db,MySqlTransaction tx,BonusDraft draft,BonusScope scope)
    {
        Validate(draft);
        var map=draft.Source.Scope.SpellIds.Zip(scope.OwnedSpells).ToDictionary(p=>p.First,p=>p.Second);
        foreach(var t in scope.Tables){
            var added=scope.Records[t.Sql].Except(draft.Source.Scope.Records[t.Sql]).ToArray();int index=0;
            foreach(var original in draft.Source.Rows[t.Sql]){
                var row=new Dictionary<string,string>(original,StringComparer.OrdinalIgnoreCase);int oldId=N(row,"ID");row["ID"]=(t.ParentSpell?added[index++]:map[oldId]).ToString();row["VerifiedBuild"]="0";if(t.ParentSpell)row["SpellID"]=map[N(row,"SpellID")].ToString();
                if(t.Sql=="spell_effect"){if(draft.Values.TryGetValue(oldId,out var value))row["EffectBasePointsF"]=value;row["EffectBasePoints"]=row.GetValueOrDefault("EffectBasePointsF")??"0";if(map.TryGetValue(N(row,"EffectTriggerSpell"),out int trigger))row["EffectTriggerSpell"]=trigger.ToString();}
                await WriteAsync(db,tx,t.Sql,row.ToDictionary(p=>p.Key,p=>(string?)p.Value));await RegisterAsync(db,tx,t.Dbc,N(row,"ID"),1);
            }
        }
        foreach(var (old,id) in map){var text=RemapText(draft.Source.Texts[old],map);var bytes=SpellBlobs.Encode(text);using(var cmd=new MySqlCommand("INSERT INTO hotfix_blob(TableHash,RecordId,locale,`Blob`,LayoutHash,VerifiedBuild) VALUES(@hash,@id,'enUS',@bytes,@layout,0)",db,tx)){cmd.Parameters.AddWithValue("@hash",TooltipData.Hash("Spell"));cmd.Parameters.AddWithValue("@id",id);cmd.Parameters.AddWithValue("@bytes",bytes);cmd.Parameters.AddWithValue("@layout",SpellBlobs.LayoutHash());await cmd.ExecuteNonQueryAsync();}await RegisterAsync(db,tx,"Spell",id,1);}
        var procs=draft.Source.Expected.Tables["bonus_proc"];
        foreach(var original in procs){var row=new Dictionary<string,string?>(original,StringComparer.OrdinalIgnoreCase);row["SpellId"]=map[Math.Abs(int.Parse(row["SpellId"]!))].ToString();await WriteAsync(db,tx,"foreverclassic_world.spell_proc",row);}
    }
    internal static async Task WriteAsync(MySqlConnection db,MySqlTransaction tx,string table,Dictionary<string,string?> values)
    {
        string schema=table.StartsWith("foreverclassic_world.")?"foreverclassic_world":CatalogRepository.Database,name=table.Split('.').Last();
        using var columns=new MySqlCommand("SELECT COLUMN_NAME,COLUMN_DEFAULT,IS_NULLABLE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=@s AND TABLE_NAME=@t ORDER BY ORDINAL_POSITION",db,tx);columns.Parameters.AddWithValue("@s",schema);columns.Parameters.AddWithValue("@t",name);var fields=new List<(string Name,string? Value)>();using(var r=await columns.ExecuteReaderAsync())while(await r.ReadAsync()){string key=r.GetString(0);string? value=values.GetValueOrDefault(key);if(value is null&&!values.ContainsKey(key)){value=r.IsDBNull(1)?null:r.GetString(1);if(value=="NULL")value=null;else value=value?.Trim('\'');if(value is null&&r.GetString(2)!="YES")throw new InvalidOperationException($"Unmapped spell field {name}.{key}; cloning blocked.");}fields.Add((key,value));}
        using var insert=new MySqlCommand($"INSERT INTO {table} ({string.Join(',',fields.Select(f=>$"`{f.Name}`"))}) VALUES({string.Join(',',fields.Select((_,n)=>"@v"+n))})",db,tx);for(int n=0;n<fields.Count;n++)insert.Parameters.AddWithValue("@v"+n,(object?)fields[n].Value??DBNull.Value);await insert.ExecuteNonQueryAsync();
    }
    internal static async Task RegisterAsync(MySqlConnection db,MySqlTransaction tx,string dbc,int id,int status)
    {
        using var next=new MySqlCommand("SELECT COALESCE(MAX(Id),0)+1 FROM hotfix_data FOR UPDATE",db,tx);long push=Convert.ToInt64(await next.ExecuteScalarAsync());if(push>int.MaxValue)throw new InvalidOperationException("Hotfix push IDs exhausted.");using var cmd=new MySqlCommand("INSERT INTO hotfix_data(Id,UniqueId,TableHash,RecordId,Status,VerifiedBuild) VALUES(@push,@push,@hash,@id,@status,0)",db,tx);cmd.Parameters.AddWithValue("@push",push);cmd.Parameters.AddWithValue("@hash",TooltipData.Hash(dbc));cmd.Parameters.AddWithValue("@id",id);cmd.Parameters.AddWithValue("@status",status);await cmd.ExecuteNonQueryAsync();
    }
    internal static BonusScope Merge(IEnumerable<BonusScope> scopes)
    {
        var all=scopes.ToArray();return new(all.SelectMany(s=>s.SpellIds).Distinct().ToArray(),all.SelectMany(s=>s.Records).GroupBy(p=>p.Key).ToDictionary(g=>g.Key,g=>g.SelectMany(p=>p.Value).Distinct().ToArray()),all.SelectMany(s=>s.OwnedSpells).Distinct().ToArray(),all[0].Fingerprint,all[0].Tables,all.SelectMany(s=>s.EditedEffects??[]).Distinct().ToArray());
    }
    internal static async Task RestoreAsync(MySqlConnection db,MySqlTransaction tx,BonusScope scope,ItemSnapshot target)
    {
        if(Fingerprint(scope.Tables)!=scope.Fingerprint)throw new InvalidOperationException("Client spell definitions changed; automatic restoration is blocked.");
        foreach(var row in target.Tables["bonus_spell_effect"].Where(r=>scope.OwnedSpells.Contains(int.Parse(r["SpellID"]!)))){
            var fields=row.ToDictionary(p=>p.Key,p=>p.Value??"",StringComparer.OrdinalIgnoreCase);
            if(scope.EditedEffects?.Contains(N(fields,"ID"))==true||(scope.EditedEffects is null&&Editable(fields)))BonusValueRules.Validate(fields,fields.GetValueOrDefault("EffectBasePointsF")??fields.GetValueOrDefault("EffectBasePoints")??"0");
        }
        foreach(var t in scope.Tables){
            var allCurrent=await Query(db,tx,$"SELECT * FROM {t.Sql} WHERE {(t.ParentSpell?"SpellID":"ID")} IN({Ids(scope.OwnedSpells)})");
            var wanted=target.Tables["bonus_"+t.Sql].Where(r=>scope.OwnedSpells.Contains(int.Parse(r[t.ParentSpell?"SpellID":"ID"]!))).ToList();
            var ids=allCurrent.Concat(wanted).Select(r=>int.Parse(r["ID"]!)).Distinct().ToArray();
            using(var delete=new MySqlCommand($"DELETE FROM {t.Sql} WHERE {(t.ParentSpell?"SpellID":"ID")} IN({Ids(scope.OwnedSpells)})",db,tx))await delete.ExecuteNonQueryAsync();
            foreach(var row in wanted)await WriteAsync(db,tx,t.Sql,row);
            foreach(int id in ids)await RegisterAsync(db,tx,t.Dbc,id,wanted.Any(r=>r["ID"]==id.ToString())?1:2);
        }
        using(var delete=new MySqlCommand($"DELETE FROM hotfix_blob WHERE TableHash={TooltipData.Hash("Spell")} AND RecordId IN({Ids(scope.OwnedSpells)})",db,tx))await delete.ExecuteNonQueryAsync();
        var blobs=target.Tables["bonus_blob"].Where(r=>scope.OwnedSpells.Contains(int.Parse(r["RecordId"]!))).ToList();
        foreach(var row in blobs){using var insert=new MySqlCommand("INSERT INTO hotfix_blob(TableHash,RecordId,locale,`Blob`,LayoutHash,VerifiedBuild) VALUES(@hash,@id,@locale,@blob,@layout,@build)",db,tx);insert.Parameters.AddWithValue("@hash",row["TableHash"]);insert.Parameters.AddWithValue("@id",row["RecordId"]);insert.Parameters.AddWithValue("@locale",row["locale"]);insert.Parameters.AddWithValue("@blob",row["Blob"] is {} hex?Convert.FromHexString(hex):DBNull.Value);insert.Parameters.AddWithValue("@layout",row["LayoutHash"]);insert.Parameters.AddWithValue("@build",row["VerifiedBuild"]);await insert.ExecuteNonQueryAsync();}
        foreach(int id in scope.OwnedSpells)await RegisterAsync(db,tx,"Spell",id,blobs.Any(r=>r["RecordId"]==id.ToString())?1:2);
        using(var delete=new MySqlCommand($"DELETE FROM foreverclassic_world.spell_proc WHERE ABS(SpellId) IN({Ids(scope.OwnedSpells)})",db,tx))await delete.ExecuteNonQueryAsync();
        foreach(var row in target.Tables["bonus_proc"].Where(r=>scope.OwnedSpells.Contains(Math.Abs(int.Parse(r["SpellId"]!)))))await WriteAsync(db,tx,"foreverclassic_world.spell_proc",row);
    }
}
