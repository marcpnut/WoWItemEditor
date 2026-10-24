using System.Globalization;
using System.IO;
using System.Text.Json;
using MySqlConnector;
namespace AnoWowItemEditor;
internal sealed record SetPieceDraft(int Slot,CloneBlueprint Source,CatalogItem Draft,DropPlan Drops)
{
    public string SlotName=>GearDesign.EquipmentSlots[Slot];
    public string Name=>Draft.Name;
    public string SourceName=>Source.Item.Current.Name;
    public int Level=>Draft.ItemLevel;
    public int RequiredLevel=>Draft.RequiredLevel;
    public bool CountsForSet=>Slot is not (3 or 18);
    public string DropSummary=>Drops.Summary;
    public override string ToString()=>$"{SlotName}: {Name}";
}
internal sealed record SetCreationScope(int SetId,Dictionary<int,CloneScope> Copies,int[] Bonuses,WorldDropScope Drops,BonusScope? PrivateBonuses=null,Dictionary<int,int>? Sources=null);
internal sealed record SetCreationResult(int SetId,int[] Items,string Operation);
internal static class SetCreation
{
    internal static void Validate(string name,IReadOnlyList<SetPieceDraft> pieces,IReadOnlyList<ContentRow> bonuses,ReferenceCatalog refs)
    {
        if(string.IsNullOrWhiteSpace(name)||name.Length>200||name.Contains('\0'))throw new InvalidOperationException("Enter a set name of 1–200 characters.");
        if(pieces.Count==0||pieces.Select(p=>p.Slot).Distinct().Count()!=pieces.Count)throw new InvalidOperationException("Choose at most one item per equipment slot.");int count=pieces.Count(p=>p.CountsForSet);if(count is <2 or >17)throw new InvalidOperationException("Choose 2–17 bonus-counting pieces. Shirt and tabard copies do not count toward bonuses.");
        if(pieces.Any(p=>p.Slot==15&&p.Draft.Slot==17)&&pieces.Any(p=>p.Slot==16))throw new InvalidOperationException("A two-handed main hand occupies both hand slots. Remove the off-hand selection.");
        foreach(var piece in pieces){ItemEditing.Validate(piece.Source.Item,piece.Draft);if(!GearDesign.Targets(piece.Draft).Contains(piece.Slot))throw new InvalidOperationException($"{piece.Name} cannot occupy {piece.SlotName}.");if(piece.Drops.Targets.Count==0)throw new InvalidOperationException($"{piece.Name} has no verified world-drop targets. Check its level.");}
        int mask=-1;foreach(var piece in pieces){int allowed=piece.Draft.Number("AllowableClass");if(allowed>0)mask&=allowed;}if(mask==0)throw new InvalidOperationException("The selected pieces have no allowed class in common.");
        var session=new ContentSession(new("Set",0,[],[],[],[]),[],new(new()),"",new(),new(),new(),count);ContentEditing.Validate(session,bonuses,refs);
    }
    internal static Task<SetCreationResult> SaveAsync(string name,SetPieceDraft[] pieces,ContentRow[] bonuses,ReferenceCatalog refs){Validate(name,pieces,bonuses,refs);return ContentEditing.Transaction((db,tx)=>SaveCore(db,tx,name,pieces,bonuses,refs));}
    private static async Task<int> Next(MySqlConnection db,MySqlTransaction tx,string table,string dbc)
    {
        using var f=File.OpenRead(Path.Combine(CatalogRepository.Db2Folder,dbc+".db2"));var h=new byte[168];f.ReadExactly(h);int native=checked((int)BitConverter.ToUInt32(h,164));using var cmd=new MySqlCommand($"SELECT GREATEST(COALESCE((SELECT MAX(ID) FROM {table}),0),COALESCE((SELECT MAX(RecordId) FROM hotfix_data WHERE TableHash={TooltipData.Hash(dbc)}),0))",db,tx);return checked((int)Math.Max(native,Convert.ToInt64(await cmd.ExecuteScalarAsync()))+1);
    }
    internal static async Task<SetCreationResult> SaveCore(MySqlConnection db,MySqlTransaction tx,string name,SetPieceDraft[] pieces,ContentRow[] bonuses,ReferenceCatalog refs)
    {
        Validate(name,pieces,bonuses,refs);var world=await WorldDropCatalog.LoadAsync(db,tx);if(pieces.Any(p=>p.Drops.InputFingerprint!=world.Fingerprint))throw new InvalidOperationException("World creatures or map definitions changed after preview. Refresh world drops and review again.");
        int setId=await Next(db,tx,"item_set","ItemSet");if(setId>ushort.MaxValue)throw new InvalidOperationException("Set IDs exceed this client's item-set field capacity.");
        var plans=new List<(ContentRow Row,BonusScope Scope,int Root)>();var reservations=new Dictionary<string,int>();foreach(var bonus in bonuses.Where(b=>b.Bonus is not null)){var plan=await BonusSpells.AllocateAsync(db,tx,bonus.Bonus!,reservations);plans.Add((bonus,plan.Scope,plan.Root));}
        var clones=new List<CloneResult>();foreach(var piece in pieces){var fields=new Dictionary<string,string>(piece.Draft.Fields,StringComparer.OrdinalIgnoreCase){["ItemSet"]=piece.CountsForSet?setId.ToString():"0"};clones.Add(await ItemCloning.SaveCoreAsync(db,tx,piece.Source,new(fields),false));}
        int nextBonus=await Next(db,tx,"item_set_spell","ItemSetSpell");var bonusRows=bonuses.Select((b,n)=>b with{Id=nextBonus+n,SpellId=plans.FirstOrDefault(p=>ReferenceEquals(p.Row,b)).Root is >0 and var root?root:b.SpellId,Bonus=null}).ToArray();
        var creation=new SetCreationScope(setId,clones.ToDictionary(c=>c.Id,c=>c.Scope!),bonusRows.Select(b=>b.Id).ToArray(),new(clones.Select(c=>c.Id).ToArray()),plans.Count>0?BonusSpells.Merge(plans.Select(p=>p.Scope)):null,clones.Select((c,n)=>(c.Id,Source:pieces[n].Source.Item.Current.Id)).ToDictionary(p=>p.Id,p=>p.Source));var scope=new ContentScope("NewSet",setId,[],[],[],[],Creation:creation);
        var before=await Capture(db,tx,creation,true);foreach(var clone in clones)foreach(var row in clone.Before!.Tables)before.Tables[$"piece_{clone.Id}_{row.Key}"]=row.Value;
        var setRow=new Dictionary<string,string?>{["ID"]=setId.ToString(),["Name"]=name,["SetFlags"]="0",["RequiredSkill"]="0",["RequiredSkillRank"]="0",["VerifiedBuild"]="0"};var members=pieces.Select((p,n)=>(Piece:p,Id:clones[n].Id)).Where(p=>p.Piece.CountsForSet).Select(p=>p.Id).ToArray();for(int n=1;n<=17;n++)setRow["ItemID"+n]=n<=members.Length?members[n-1].ToString():"0";
        await ContentEditing.Upsert(db,tx,"item_set",setRow);await ContentEditing.Register(db,tx,"item_set",setId);
        foreach(var plan in plans)await BonusSpells.ApplyAsync(db,tx,plan.Row.Bonus!,plan.Scope);
        foreach(var bonus in bonusRows){var row=new Dictionary<string,string?>{["ID"]=bonus.Id.ToString(),["ChrSpecID"]=bonus.Specialization.ToString(),["SpellID"]=bonus.SpellId.ToString(),["TraitSubTreeID"]=bonus.Trait.ToString(),["Threshold"]=bonus.Pieces.ToString(),["ItemSetID"]=setId.ToString(),["VerifiedBuild"]="0"};await ContentEditing.Upsert(db,tx,"item_set_spell",row);await ContentEditing.Register(db,tx,"item_set_spell",bonus.Id);}
        for(int n=0;n<pieces.Length;n++)await WorldDrops.Apply(db,tx,clones[n].Id,world.Plan(pieces[n].Draft,pieces[n].Drops.Chance),pieces[n].Name);
        before=before with{OriginalItem=new(){["Display"]="No set or duplicated items existed before creation"},ContentRows=[]};var after=(await Capture(db,tx,creation,true)) with{OriginalItem=new(){["Display"]=name,["ID"]=setId.ToString(),["Items"]=string.Join(", ",clones.Select(c=>c.Id)),["Copied from"]=string.Join("; ",clones.Select((c,n)=>$"{c.Id} ← base {pieces[n].Source.Item.Current.Id} ({pieces[n].SlotName})"))},ContentRows=bonusRows};string op=Guid.NewGuid().ToString();
        using(var cmd=new MySqlCommand($"INSERT INTO {ContentEditing.Operations}(OperationId,Kind,EntityId,Name,IsActive,IsHidden,Version,BeforeJson,AfterJson,ExpectedJson,ScopeJson,Fingerprint,RecordedUtc) VALUES(@op,'NewSet',@id,@name,1,0,1,@before,@after,@after,@scope,@fp,UTC_TIMESTAMP(6))",db,tx)){cmd.Parameters.AddWithValue("@op",op);cmd.Parameters.AddWithValue("@id",setId);cmd.Parameters.AddWithValue("@name",name);cmd.Parameters.AddWithValue("@before",HistoryActions.Serialize(before));cmd.Parameters.AddWithValue("@after",HistoryActions.Serialize(after));cmd.Parameters.AddWithValue("@scope",JsonSerializer.Serialize(scope));cmd.Parameters.AddWithValue("@fp",ContentEditing.Fingerprint());await cmd.ExecuteNonQueryAsync();}
        await ContentEditing.Audit(db,tx,scope,"Create set and world drops",name,op,before,after);return new(setId,clones.Select(c=>c.Id).ToArray(),op);
    }
    internal static async Task<ItemSnapshot> Capture(MySqlConnection db,MySqlTransaction? tx,SetCreationScope scope,bool locked=false)
    {
        string suffix=locked?" FOR UPDATE":"";var rows=new Dictionary<string,List<Dictionary<string,string?>>>{["new_set"]=await WorldDrops.Query(db,tx,$"SELECT * FROM item_set WHERE ID={scope.SetId} ORDER BY ID,VerifiedBuild"+suffix),["new_set_bonuses"]=await WorldDrops.Query(db,tx,$"SELECT * FROM item_set_spell WHERE ItemSetID={scope.SetId} OR ID IN({WorldDrops.Ids(scope.Bonuses)}) ORDER BY ID,VerifiedBuild"+suffix),["new_set_registrations"]=await WorldDrops.Query(db,tx,$"SELECT * FROM hotfix_data WHERE (TableHash={TooltipData.Hash("ItemSet")} AND RecordId={scope.SetId}) OR (TableHash={TooltipData.Hash("ItemSetSpell")} AND RecordId IN({WorldDrops.Ids(scope.Bonuses)})) ORDER BY Id,UniqueId"+suffix)};
        foreach(var (id,copy) in scope.Copies)foreach(var p in (await ItemCloning.CaptureAsync(db,tx,id,copy,locked)).Tables)rows[$"piece_{id}_{p.Key}"]=p.Value;
        foreach(var p in await WorldDrops.Capture(db,tx,scope.Drops,locked))rows[p.Key]=p.Value;if(scope.PrivateBonuses is not null)foreach(var p in (await BonusSpells.CaptureAsync(db,tx,scope.PrivateBonuses,locked)).Tables)rows[p.Key]=p.Value;return new(rows);
    }
    internal static async Task Restore(MySqlConnection db,MySqlTransaction tx,SetCreationScope scope,ItemSnapshot target)
    {
        foreach(var (id,copy) in scope.Copies){var item=new ItemSnapshot(target.Tables.Where(p=>p.Key.StartsWith($"piece_{id}_")).ToDictionary(p=>p.Key[$"piece_{id}_".Length..],p=>p.Value));await ItemCloning.RestoreAsync(db,tx,new("Clone",id,[],[],[],[],CopyScope:copy),item);}
        using(var delete=new MySqlCommand($"DELETE FROM item_set WHERE ID={scope.SetId}",db,tx))await delete.ExecuteNonQueryAsync();using(var delete=new MySqlCommand($"DELETE FROM item_set_spell WHERE ItemSetID={scope.SetId} OR ID IN({WorldDrops.Ids(scope.Bonuses)})",db,tx))await delete.ExecuteNonQueryAsync();foreach(var row in target.Tables["new_set"])await ContentEditing.Upsert(db,tx,"item_set",row);foreach(var row in target.Tables["new_set_bonuses"])await ContentEditing.Upsert(db,tx,"item_set_spell",row);await ContentEditing.Register(db,tx,"item_set",scope.SetId,target.Tables["new_set"].Count>0?1:2);foreach(int id in scope.Bonuses)await ContentEditing.Register(db,tx,"item_set_spell",id,target.Tables["new_set_bonuses"].Any(r=>r["ID"]==id.ToString())?1:2);if(scope.PrivateBonuses is not null)await BonusSpells.RestoreAsync(db,tx,scope.PrivateBonuses,target);await WorldDrops.Restore(db,tx,scope.Drops,target);
    }
}
