using System.Globalization;
using System.Text.Json;
using MySqlConnector;
using DBCD.Providers;
namespace WoWItemEditor;
internal sealed record QuestNpc(int Id,string Name,int MinLevel,int MaxLevel,int Faction)
{
    public string FactionName {get;set;}="Loading faction…";
    public string Level=>$"{MinLevel}–{MaxLevel}";
}
internal sealed record QuestNpcSource(QuestNpc Npc,Dictionary<string,string?> Template,List<Dictionary<string,string?>> Models,List<Dictionary<string,string?>> Equipment)
{
    internal string Fingerprint=>HistoryActions.Canonical(new(new(){["template"]=[Template],["models"]=Models,["equipment"]=Equipment}));
}
internal sealed record QuestPlacement(ulong CharacterId,string CharacterName,int Map,int Zone,double X,double Y,double Z,double Facing,long LogoutTime,DateTime CapturedUtc)
{
    internal string PositionKey=>FormattableString.Invariant($"{CharacterId}/{Map}/{Zone}/{X:R}/{Y:R}/{Z:R}/{Facing:R}/{LogoutTime}");
    public string Summary=>FormattableString.Invariant($"{CharacterName} · map {Map}, zone {Zone}\nX {X:F3} · Y {Y:F3} · Z {Z:F3} · facing {Facing:F5} radians ({Facing*180/Math.PI:F1}°)");
}
internal sealed record QuestDraft(string NpcName,string Title,string Summary,string Description,string RewardText,int Level,int MinLevel,int RewardItem,int RewardCount,bool GroundConfirmed,int Experience=0,int MoneyCopper=0) { public QuestOptions Options {get;init;}=new(); }
internal sealed record QuestCreationResult(int NpcId,ulong SpawnId,int QuestId,string OperationId);
internal static class QuestCreation
{
    internal static string World=>AppSettings.Current.WorldDatabase;
    internal static string History=>AppSettings.Current.BackupDatabase+".quest_editor_creations";
    // Only ordinary, non-scripted existing quest starters. Encounter and difficulty variants are excluded independently of rank.
    internal const string EligibleSql="(c.npcflag & 2)<>0 AND c.rank=0 AND c.minlevel>=1 AND c.maxlevel BETWEEN c.minlevel AND 60 AND (c.type_flags & 2147483652)=0 AND (c.flags_extra & 268435584)=0 AND c.RacialLeader=0 AND c.VehicleId=0 AND COALESCE(c.ScriptName,'')='' AND c.HealthModifier BETWEEN 0.01 AND 100 AND c.DamageModifier BETWEEN 0.01 AND 10 AND EXISTS(SELECT 1 FROM {0}.creature_queststarter qs WHERE qs.id=c.entry) AND EXISTS(SELECT 1 FROM {0}.creature_template_model m WHERE m.CreatureID=c.entry AND m.CreatureDisplayID>0) AND NOT EXISTS(SELECT 1 FROM {0}.instance_encounters e WHERE e.creditType=0 AND e.creditEntry=c.entry) AND NOT EXISTS(SELECT 1 FROM {0}.creature_template v WHERE c.entry IN(v.difficulty_entry_1,v.difficulty_entry_2,v.difficulty_entry_3))";
    private static int N(IReadOnlyDictionary<string,string?> row,string name)=>int.TryParse(row.GetValueOrDefault(name),out int n)?n:0;
    private static double D(IReadOnlyDictionary<string,string?> row,string name)=>double.Parse(row[name]!,CultureInfo.InvariantCulture);
    private static QuestNpc Npc(Dictionary<string,string?> row)=>new(N(row,"entry"),row["name"]??"",N(row,"minlevel"),N(row,"maxlevel"),N(row,"faction"));
    internal static async Task<List<QuestNpc>> Browse()
    {
        using var db=await DatabaseConnection.OpenAsync(World);
        return (await WorldDrops.Query(db,null,$"SELECT c.entry,c.name,c.minlevel,c.maxlevel,c.faction FROM {World}.creature_template c WHERE {string.Format(CultureInfo.InvariantCulture,EligibleSql,World)} ORDER BY c.name,c.entry")).Select(Npc).ToList();
    }
    internal static async Task<QuestNpcSource> Source(int id,MySqlConnection? connection=null,MySqlTransaction? tx=null,bool locked=false)
    {
        using var owned=connection is null?await DatabaseConnection.OpenAsync(World):null;var db=connection??owned!;string suffix=locked?" FOR UPDATE":"";
        var row=(await WorldDrops.Query(db,tx,$"SELECT c.* FROM {World}.creature_template c WHERE c.entry={id} AND {string.Format(CultureInfo.InvariantCulture,EligibleSql,World)}"+suffix)).SingleOrDefault()??throw new InvalidOperationException("This template is no longer an eligible normal quest giver. Bosses, encounter variants and unsafe/scripted templates cannot be copied.");
        var addons=await WorldDrops.Query(db,tx,$"SELECT * FROM {World}.creature_template_addon WHERE entry={id}"+suffix);
        if(addons.Any(a=>N(a,"mount")!=0||!string.IsNullOrWhiteSpace(a.GetValueOrDefault("auras"))))throw new InvalidOperationException("This NPC uses a mount or aura appearance that cannot be copied faithfully yet. Choose another quest giver.");
        var models=await WorldDrops.Query(db,tx,$"SELECT * FROM {World}.creature_template_model WHERE CreatureID={id} ORDER BY Idx"+suffix);
        foreach(var model in models)if((await WorldDrops.Query(db,tx,$"SELECT DisplayID FROM {World}.creature_model_info WHERE DisplayID={N(model,"CreatureDisplayID")}")).Count==0)throw new InvalidOperationException("The source NPC has a missing creature model definition. Choose another quest giver.");
        return new(Npc(row),row,models,await WorldDrops.Query(db,tx,$"SELECT * FROM {World}.creature_equip_template WHERE CreatureID={id} ORDER BY ID"+suffix));
    }
    internal static async Task<QuestPlacement> Capture(ulong guid,MySqlConnection? connection=null,MySqlTransaction? tx=null,bool locked=false)
    {
        using var owned=connection is null?await DatabaseConnection.OpenAsync(CharacterRepository.Database):null;var db=connection??owned!;
        var row=(await WorldDrops.Query(db,tx,$"SELECT guid,name,online,map,zone,instance_id,position_x,position_y,position_z,orientation,logout_time,transguid,taxi_path FROM {AppSettings.Current.CharacterDatabase}.characters WHERE guid={guid}"+(locked?" FOR UPDATE":""))).SingleOrDefault()??throw new InvalidOperationException("Character not found.");
        if(N(row,"online")!=0)throw new InvalidOperationException("Log out to the WoW character-selection screen after positioning and facing your character, then capture again. Online database positions can be stale.");
        if(N(row,"instance_id")!=0||row["transguid"]!="0"||!string.IsNullOrWhiteSpace(row["taxi_path"]))throw new InvalidOperationException("Use a character standing on outdoor ground, not in an instance, on a transport or on a taxi.");
        var result=new QuestPlacement(guid,row["name"]??"",N(row,"map"),N(row,"zone"),D(row,"position_x"),D(row,"position_y"),D(row,"position_z"),D(row,"orientation"),long.Parse(row["logout_time"]!,CultureInfo.InvariantCulture),DateTime.UtcNow);ValidatePosition(result);
        var reader=new DBCD.DBCD(new FilesystemDBCProvider(CatalogRepository.Db2Folder),new GithubDBDProvider(true));var maps=await Task.Run(()=>TooltipData.LoadTable(reader,"Map"));
        if(!maps.TryGetValue(result.Map,out var map)||TooltipData.Number(map,"InstanceType")!=0)throw new InvalidOperationException("This first version supports outdoor world maps only. Choose a location outside instances, battlegrounds and arenas.");
        return result;
    }
    internal static void ValidatePosition(QuestPlacement p)
    {
        if(new[]{p.X,p.Y,p.Z,p.Facing}.Any(n=>!double.IsFinite(n))||Math.Abs(p.X)>32768||Math.Abs(p.Y)>32768||p.Z is < -2000 or > 10000||p.Facing<0||p.Facing>Math.Tau)throw new InvalidOperationException("The saved position or orientation is outside supported world-coordinate bounds.");
    }
    internal static void Validate(QuestDraft draft,QuestPlacement p)
    {
        ValidatePosition(p);
        if(draft.Experience is <0 or >100000||draft.MoneyCopper is <0 or >10000000)throw new InvalidOperationException("XP must be 0–100,000; gold must be 0–1,000.");
        if(!draft.GroundConfirmed)throw new InvalidOperationException("Confirm that the character was standing on solid ground at the intended location and facing before logging out.");
        if(DateTime.UtcNow-p.CapturedUtc>TimeSpan.FromMinutes(30)||p.CapturedUtc>DateTime.UtcNow.AddMinutes(1))throw new InvalidOperationException("Capture the character location again; the captured position has expired.");
        ValidateContent(draft);
    }
    internal static void ValidateContent(QuestDraft draft)
    {
        if(draft.Experience is <0 or >100000||draft.MoneyCopper is <0 or >10000000)throw new InvalidOperationException("XP must be 0-100,000; gold must be 0-1,000.");
        QuestDefinitions.Validate(draft);
        if(string.IsNullOrWhiteSpace(draft.NpcName)||draft.NpcName.Length>100||draft.NpcName.Any(char.IsControl)||string.IsNullOrWhiteSpace(draft.Title)||draft.Title.Length>120||draft.Title.Any(char.IsControl))throw new InvalidOperationException("Enter an NPC name (1–100 characters) and quest title (1–120 characters), without control characters.");
        if(new[]{draft.Summary,draft.Description,draft.RewardText}.Any(t=>string.IsNullOrWhiteSpace(t)||t.Length>4000||t.Contains('\0')))throw new InvalidOperationException("Enter quest summary, description and reward dialogue (1–4000 characters each).");
        if(draft.Level is <1 or >60||draft.MinLevel<1||draft.MinLevel>draft.Level||draft.RewardItem<0||draft.RewardItem>0&&draft.RewardCount is <1 or >1000)throw new InvalidOperationException("Use quest levels 1–60, minimum level no higher than quest level, and valid reward quantities.");
    }
    internal static async Task Initialize(MySqlConnection db)
    {
        using var cmd=new MySqlCommand($"CREATE TABLE IF NOT EXISTS {History} (OperationId CHAR(36) NOT NULL PRIMARY KEY,NpcId INT NOT NULL,SpawnId BIGINT UNSIGNED NOT NULL,QuestId INT NOT NULL,Title VARCHAR(120) NOT NULL,SourceNpcId INT NOT NULL,SnapshotJson LONGTEXT NOT NULL,CreatedUtc DATETIME(6) NOT NULL) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",db);await cmd.ExecuteNonQueryAsync();
        using(var ids=new MySqlCommand($"CREATE TABLE IF NOT EXISTS {AppSettings.Current.BackupDatabase}.quest_editor_objective_ids(ID INT NOT NULL PRIMARY KEY) ENGINE=InnoDB",db))await ids.ExecuteNonQueryAsync();
        foreach(string table in new[]{World+".creature_template",World+".creature_template_model",World+".creature_equip_template",World+".creature",World+".quest_template",World+".quest_template_addon",World+".quest_offer_reward",World+".quest_request_items",World+".quest_details",World+".quest_objectives",World+".areatrigger_involvedrelation",World+".creature_queststarter",World+".creature_questender",History})
        {
            string[] parts=table.Split('.');using var check=new MySqlCommand("SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA=@s AND TABLE_NAME=@t",db);check.Parameters.AddWithValue("@s",parts[0]);check.Parameters.AddWithValue("@t",parts[1]);if(Convert.ToString(await check.ExecuteScalarAsync())!="InnoDB")throw new InvalidOperationException(table+" must exist and use InnoDB before quest creation.");
        }
    }
    internal static async Task<QuestCreationResult> Save(QuestNpcSource source,QuestDraft draft,QuestPlacement placement)
    {
        using(var db=await DatabaseConnection.OpenAsync(CatalogRepository.Database)){await ContentEditing.InitializeAsync(db);await Initialize(db);}
        return await ContentEditing.Transaction((db,tx)=>SaveCore(db,tx,source,draft,placement));
    }
    internal static async Task<(int Index,float Multiplier)> RewardScale(string table,int level,int amount)
    {
        if(amount==0)return (0,0);var rows=await ItemModelAssets.Shared.Table(table);if(!rows.TryGetValue(level,out var row))throw new InvalidOperationException("Reward level is unavailable in "+table);
        var candidates=Enumerable.Range(1,10).Select(n=>(Index:n-1,Value:TooltipData.Number(row,"Difficulty"+n))).Where(v=>v.Value>0).OrderBy(v=>Math.Abs(v.Value-amount)).ToArray();if(candidates.Length==0)throw new InvalidOperationException("Reward scaling data is unavailable.");var chosen=candidates[0];float multiplier=(float)amount/chosen.Value;if(!float.IsFinite(multiplier)||multiplier>10000000)throw new InvalidOperationException("Reward multiplier exceeds the supported range.");return(chosen.Index,multiplier);
    }
    private static string S(object n)=>Convert.ToString(n,CultureInfo.InvariantCulture)??"";
    private static async Task<int> Next(MySqlConnection db,MySqlTransaction tx,string table,string key)
    {
        using var cmd=new MySqlCommand($"SELECT GREATEST(COALESCE((SELECT MAX({key}) FROM {World}.{table}),0),COALESCE((SELECT MAX({(table=="quest_template"?"QuestId":"NpcId")}) FROM {History}),0))+1",db,tx);return checked(Convert.ToInt32(await cmd.ExecuteScalarAsync()));
    }
    internal static async Task<QuestCreationResult> SaveCore(MySqlConnection db,MySqlTransaction tx,QuestNpcSource source,QuestDraft draft,QuestPlacement placement)
    {
        Validate(draft,placement);var current=await Source(source.Npc.Id,db,tx,true);if(current.Fingerprint!=source.Fingerprint)throw new InvalidOperationException("The source NPC changed. Select it again and review a new copy.");
        var livePosition=await Capture(placement.CharacterId,db,tx,true);if(livePosition.PositionKey!=placement.PositionKey)throw new InvalidOperationException("The character location changed. Capture and review it again.");
        int npc=await Next(db,tx,"creature_template","entry"),quest=await Next(db,tx,"quest_template","ID");using var spawnMax=new MySqlCommand($"SELECT GREATEST(COALESCE((SELECT MAX(guid) FROM {World}.creature),0),COALESCE((SELECT MAX(SpawnId) FROM {History}),0))+1",db,tx);ulong spawn=Convert.ToUInt64(await spawnMax.ExecuteScalarAsync());
        var template=new Dictionary<string,string?>(source.Template,StringComparer.OrdinalIgnoreCase){["entry"]=S(npc),["name"]=draft.NpcName.Trim(),["femaleName"]=draft.NpcName.Trim(),["subname"]="",["TitleAlt"]="",["IconName"]="",["gossip_menu_id"]="0",["npcflag"]="2",["AIName"]="",["ScriptName"]="",["StringId"]=null,["rank"]="0",["RacialLeader"]="0",["VehicleId"]="0",["MovementType"]="0",["movementId"]="0",["lootid"]="0",["pickpocketloot"]="0",["skinloot"]="0",["mingold"]="0",["maxgold"]="0",["KillCredit1"]="0",["KillCredit2"]="0",["difficulty_entry_1"]="0",["difficulty_entry_2"]="0",["difficulty_entry_3"]="0",["dynamicflags"]="0",["VerifiedBuild"]="0"};
        await BonusSpells.WriteAsync(db,tx,World+".creature_template",template);
        foreach(var original in source.Models){var copy=new Dictionary<string,string?>(original){["CreatureID"]=S(npc)};await BonusSpells.WriteAsync(db,tx,World+".creature_template_model",copy);}
        foreach(var original in source.Equipment){var copy=new Dictionary<string,string?>(original){["CreatureID"]=S(npc)};await BonusSpells.WriteAsync(db,tx,World+".creature_equip_template",copy);}
        await BonusSpells.WriteAsync(db,tx,World+".creature",new(){["guid"]=S(spawn),["id"]=S(npc),["map"]=S(placement.Map),["zoneId"]=S(placement.Zone),["spawnDifficulties"]="0",["position_x"]=S(placement.X),["position_y"]=S(placement.Y),["position_z"]=S(placement.Z),["orientation"]=S(placement.Facing),["spawntimesecs"]="120",["wander_distance"]="0",["MovementType"]="0",["curhealth"]="1",["equipment_id"]=source.Equipment.Count>0?"-1":"0"});
        var xp=await RewardScale("QuestXP",draft.Level,draft.Experience);var money=await RewardScale("QuestMoneyReward",draft.Level,draft.MoneyCopper);
        await BonusSpells.WriteAsync(db,tx,World+".quest_template",new(){["ID"]=S(quest),["QuestType"]="2",["QuestLevel"]=S(draft.Level),["MinLevel"]=S(draft.MinLevel),["QuestSortID"]=placement.Zone is >0 and <=32767?S(placement.Zone):"0",["LogTitle"]=draft.Title.Trim(),["LogDescription"]=draft.Summary,["QuestDescription"]=draft.Description,["QuestCompletionLog"]="Return to "+draft.NpcName.Trim()+".",["RewardItem1"]=S(draft.RewardItem),["RewardAmount1"]=S(draft.RewardCount),["RewardXPDifficulty"]=S(xp.Index),["RewardXPMultiplier"]=S(xp.Multiplier),["RewardMoneyDifficulty"]=S(money.Index),["RewardMoneyMultiplier"]=S(money.Multiplier),["AllowableRaces"]=ulong.MaxValue.ToString(CultureInfo.InvariantCulture),["Flags"]="0",["FlagsEx"]="0",["FlagsEx2"]="0"});
        await BonusSpells.WriteAsync(db,tx,World+".quest_template_addon",new(){["ID"]=S(quest),["SpecialFlags"]="0",["ScriptName"]=""});
        await BonusSpells.WriteAsync(db,tx,World+".quest_offer_reward",new(){["ID"]=S(quest),["RewardText"]=draft.RewardText});
        await BonusSpells.WriteAsync(db,tx,World+".quest_request_items",new(){["ID"]=S(quest),["CompletionText"]="Speak to "+draft.NpcName.Trim()+" to complete this quest."});
        foreach(string table in new[]{"creature_queststarter","creature_questender"})await BonusSpells.WriteAsync(db,tx,World+"."+table,new(){["id"]=S(npc),["quest"]=S(quest)});
        await QuestDefinitions.Apply(db,tx,quest,npc,draft);
        string operation=Guid.NewGuid().ToString();using var audit=new MySqlCommand($"INSERT INTO {History}(OperationId,NpcId,SpawnId,QuestId,Title,SourceNpcId,SnapshotJson,CreatedUtc) VALUES(@op,@npc,@spawn,@quest,@title,@source,@json,UTC_TIMESTAMP(6))",db,tx);audit.Parameters.AddWithValue("@op",operation);audit.Parameters.AddWithValue("@npc",npc);audit.Parameters.AddWithValue("@spawn",spawn);audit.Parameters.AddWithValue("@quest",quest);audit.Parameters.AddWithValue("@title",draft.Title.Trim());audit.Parameters.AddWithValue("@source",source.Npc.Id);audit.Parameters.AddWithValue("@json",JsonSerializer.Serialize(new{source,draft,placement,npc,spawn,quest}));await audit.ExecuteNonQueryAsync();
        return new(npc,spawn,quest,operation);
    }
}
