using System.Globalization;
using System.Text.Json;
using MySqlConnector;
namespace WoWItemEditor;
internal sealed record QuestObjectiveDraft(int Id,int Type,int Target,int Amount,string Text);
internal sealed record QuestItemReward(int Item,int Count);
internal sealed record QuestReputationReward(int Faction,int Amount);
internal sealed record QuestOptions
{
    public List<QuestObjectiveDraft> Objectives {get;init;}=[];
    public List<QuestItemReward>? Guaranteed {get;init;}
    public List<QuestItemReward> Choices {get;init;}=[];
    public List<QuestReputationReward> Reputation {get;init;}=[];
    public string CompletionLog {get;init;}="";
    public string IncompleteText {get;init;}="";
    public string AreaText {get;init;}="";
    public int MaxLevel {get;init;}
    public int TimeSeconds {get;init;}
    public int SuggestedPlayers {get;init;}=1;
    public int QuestInfo {get;init;}
    public int QuestSort {get;init;}
    public string Repeat {get;init;}="Once";
    public bool Shareable {get;init;}=true;
    public bool RaidAllowed {get;init;}
    public bool AutoAccept {get;init;}
    public ulong Races {get;init;}=ulong.MaxValue;
    public uint Classes {get;init;}
    public int PreviousQuest {get;init;}
    public int NextQuest {get;init;}
    public int ExclusiveGroup {get;init;}
    public int RequiredSkill {get;init;}
    public int RequiredSkillPoints {get;init;}
    public int RequiredRepFaction {get;init;}
    public int RequiredRepValue {get;init;}
    public int SourceItem {get;init;}
    public int SourceCount {get;init;}=1;
    public int SourceSpell {get;init;}
    public int RewardSpell {get;init;}
    public int EndNpc {get;init;}
    public int AcceptEmote {get;init;}
    public int CompleteEmote {get;init;}
    public int IncompleteEmote {get;init;}
}
internal sealed record QuestEditSession(CreatedQuest Entry,QuestDraft Draft,string Expected,Dictionary<string,string?> Spawn);
internal static class QuestDefinitions
{
    internal static readonly Dictionary<int,string> Types=new(){[0]="Kill creatures",[1]="Collect / deliver items",[2]="Interact with world object",[3]="Talk to NPC",[5]="Learn a spell",[6]="Reach reputation",[7]="Stay below reputation",[8]="Pay money (copper)",[9]="Defeat enemy players",[10]="Explore existing area trigger"};
    internal static List<QuestItemReward> Guaranteed(QuestDraft draft)=>draft.Options.Guaranteed??(draft.RewardItem>0?[new(draft.RewardItem,draft.RewardCount)]:[]);
    private static int N(IReadOnlyDictionary<string,string?> row,string key)=>int.TryParse(row.GetValueOrDefault(key),out int n)?n:0;
    private static string S(object value)=>Convert.ToString(value,CultureInfo.InvariantCulture)??"";
    internal static void Validate(QuestDraft draft,int quest=0)
    {
        var o=draft.Options;var rewards=Guaranteed(draft);if(rewards.Count>4||o.Choices.Count>6||o.Reputation.Count>5||o.Objectives.Count>8)throw new InvalidOperationException("Maximum: 8 objectives, 4 guaranteed items, 6 choice rewards and 5 reputation rewards.");
        if(rewards.Concat(o.Choices).Any(r=>r.Item<=0||r.Count is <1 or >1000)||rewards.GroupBy(r=>r.Item).Any(g=>g.Count()>1)||o.Choices.GroupBy(r=>r.Item).Any(g=>g.Count()>1))throw new InvalidOperationException("Use valid, distinct item rewards with quantities 1–1,000.");
        foreach(var objective in o.Objectives){if(!Types.ContainsKey(objective.Type)||objective.Text.Length>1000||objective.Text.Contains('\0')||objective.Type is not (8 or 9)&&objective.Target<=0)throw new InvalidOperationException("Choose a valid objective type and target; text must be at most 1,000 characters.");bool invalidAmount=objective.Type is 6 or 7?objective.Amount is < -42000 or >42000:objective.Type is 5 or 10?objective.Amount!=1:objective.Type is 0 or 1 or 2 or 3 or 9?objective.Amount is <1 or >1000:objective.Amount is <1 or >10000000;if(invalidAmount)throw new InvalidOperationException("Use counts 1-1,000; spell learning / exploration use 1; reputation is -42,000 to 42,000; money is 1-10,000,000 copper.");}
        if(o.Objectives.Count(r=>r.Type==10)>1||o.Objectives.GroupBy(r=>(r.Type,r.Target)).Any(g=>g.Count()>1))throw new InvalidOperationException("Use distinct objectives and at most one exploration trigger.");
        if(o.MaxLevel<0||o.MaxLevel>60||o.MaxLevel>0&&o.MaxLevel<draft.MinLevel||o.TimeSeconds is <0 or >86400||o.SuggestedPlayers is <1 or >40||o.QuestSort is < -32768 or >32767||o.QuestInfo is <0 or >65535||o.ExclusiveGroup<0)throw new InvalidOperationException("Check maximum level (0 or 1–60), time (0–86,400 seconds), group size (1–40), category and exclusive group.");
        if(!new[]{"Once","Repeatable","Daily","Weekly","Monthly"}.Contains(o.Repeat)||o.Races==0)throw new InvalidOperationException("Choose a repeat schedule and at least one allowed race.");
        if(o.PreviousQuest<0||o.NextQuest<0||quest>0&&(o.PreviousQuest==quest||o.NextQuest==quest)||o.PreviousQuest>0&&o.PreviousQuest==o.NextQuest)throw new InvalidOperationException("A quest cannot link to itself or use the same previous and next quest.");
        if(o.SourceItem<0||o.SourceItem>0&&o.SourceCount is <1 or >20||o.RequiredSkill<0||o.RequiredSkillPoints is <0 or >450||o.RequiredSkill==0&&o.RequiredSkillPoints>0||o.RequiredRepFaction<0||o.RequiredRepValue is < -42000 or >42000||o.EndNpc<0)throw new InvalidOperationException("Check starting item, profession and reputation requirements.");
        if(o.Reputation.Any(r=>r.Faction<=0||r.Amount is < -42000 or >42000)||o.Reputation.GroupBy(r=>r.Faction).Any(g=>g.Count()>1))throw new InvalidOperationException("Use distinct reputation factions and changes from -42,000 to 42,000.");
        if(new[]{o.CompletionLog,o.IncompleteText,o.AreaText}.Any(t=>t.Length>4000||t.Contains('\0')))throw new InvalidOperationException("Quest text must be at most 4,000 characters and cannot contain a null character.");
    }
    private static async Task Exists(MySqlConnection db,MySqlTransaction tx,string table,string key,int id,string label)
    {
        using var cmd=new MySqlCommand($"SELECT COUNT(*) FROM {QuestCreation.World}.{table} WHERE `{key}`=@id",db,tx);cmd.Parameters.AddWithValue("@id",id);if(Convert.ToInt32(await cmd.ExecuteScalarAsync())==0)throw new InvalidOperationException($"{label} {id} does not exist.");
    }
    private static async Task Native(string table,int id,string label){if(id==0)return;if(!(await ItemModelAssets.Shared.Table(table)).ContainsKey(id))throw new InvalidOperationException($"{label} {id} is unavailable in the installed definitions.");}
    internal static async Task Apply(MySqlConnection db,MySqlTransaction tx,int quest,int npc,QuestDraft draft,bool editing=false)
    {
        Validate(draft,quest);var o=draft.Options;var guaranteed=Guaranteed(draft);
        foreach(int item in guaranteed.Concat(o.Choices).Select(r=>r.Item).Concat(o.Objectives.Where(r=>r.Type==1).Select(r=>r.Target)).Concat(o.SourceItem>0?[o.SourceItem]:Array.Empty<int>()).Distinct()){
            var session=await ItemEditing.LoadAsync(item,db,tx);if(session.Calculations.ServerProblems(session.Current).Count>0)throw new InvalidOperationException($"Item {item} has invalid server scaling data.");int unique=session.Current.Number("MaxCount");int count=guaranteed.Where(r=>r.Item==item).Sum(r=>r.Count);count+=o.Choices.Where(r=>r.Item==item).Select(r=>r.Count).DefaultIfEmpty(0).Max();count=Math.Max(count,o.Objectives.Where(r=>r.Type==1&&r.Target==item).Select(r=>r.Amount).DefaultIfEmpty(0).Max());if(o.SourceItem==item)count=Math.Max(count,o.SourceCount);if(unique>0&&count>unique)throw new InvalidOperationException($"Item {item} exceeds its unique limit.");
        }
        foreach(var objective in o.Objectives){switch(objective.Type){case 0:case 3:await Exists(db,tx,"creature_template","entry",objective.Target,"Creature");break;case 2:await Exists(db,tx,"gameobject_template","entry",objective.Target,"World object");break;case 5:await Native("SpellName",objective.Target,"Spell");break;case 6:case 7:await Native("Faction",objective.Target,"Faction");break;case 10:await Native("AreaTrigger",objective.Target,"Area trigger");using(var check=new MySqlCommand($"SELECT quest FROM {QuestCreation.World}.areatrigger_involvedrelation WHERE id=@id FOR UPDATE",db,tx)){check.Parameters.AddWithValue("@id",objective.Target);object? value=await check.ExecuteScalarAsync();if(value is not null&&Convert.ToInt32(value)!=quest)throw new InvalidOperationException("This exploration trigger already belongs to another quest. Choose an unused trigger.");}break;}}
        foreach(var rep in o.Reputation)await Native("Faction",rep.Faction,"Faction");await Native("SkillLine",o.RequiredSkill,"Profession");await Native("Faction",o.RequiredRepFaction,"Reputation faction");await Native("SpellName",o.SourceSpell,"Starting spell");await Native("SpellName",o.RewardSpell,"Reward spell");await Native("Emotes",o.AcceptEmote,"Emote");await Native("Emotes",o.CompleteEmote,"Emote");await Native("Emotes",o.IncompleteEmote,"Emote");await Native("QuestInfo",o.QuestInfo,"Quest type");
        foreach(int link in new[]{o.PreviousQuest,o.NextQuest}.Where(id=>id>0))await Exists(db,tx,"quest_template","ID",link,"Linked quest");
        await ValidateChain(db,tx,quest,o);
        if(o.EndNpc>0){var eligible=await QuestCreation.Source(o.EndNpc,db,tx);if(eligible.Npc.Id==npc)throw new InvalidOperationException("Use Same NPC for the created quest giver.");}
        var before=await WorldDrops.Query(db,tx,$"SELECT * FROM {QuestCreation.World}.quest_objectives WHERE QuestID={quest} ORDER BY `Order`,ID FOR UPDATE");
        var objectiveSignature=o.Objectives.Select(r=>(r.Type,r.Target,r.Amount,r.Text)).ToArray();var beforeTriggers=await WorldDrops.Query(db,tx,$"SELECT * FROM {QuestCreation.World}.areatrigger_involvedrelation WHERE quest={quest} FOR UPDATE");
        bool changed=!before.Select(r=>(N(r,"Type"),N(r,"Type")==10?beforeTriggers.Select(t=>N(t,"id")).FirstOrDefault():N(r,"ObjectID"),N(r,"Type")==10?1:N(r,"Amount"),r.GetValueOrDefault("Description")??"")).SequenceEqual(objectiveSignature);
        if(editing&&changed&&(await WorldDrops.Query(db,tx,$"SELECT guid FROM {AppSettings.Current.CharacterDatabase}.character_queststatus WHERE quest={quest} AND status IN(1,3) LIMIT 1 FOR UPDATE")).Count>0)throw new InvalidOperationException("A character currently has this quest. Finish or abandon it before changing its objectives. Text and reward edits can be saved without changing objectives.");
        uint flags=(o.Shareable?8u:0)|(o.RaidAllowed?64u:0)|(o.AutoAccept?524288u:0)|(o.Repeat=="Daily"?4096u:0)|(o.Repeat=="Weekly"?32768u:0)|(o.Objectives.Any(r=>r.Type==10)?4u:0);
        var xp=await QuestCreation.RewardScale("QuestXP",draft.Level,draft.Experience);var money=await QuestCreation.RewardScale("QuestMoneyReward",draft.Level,draft.MoneyCopper);
        var main=new Dictionary<string,string?>{["LogTitle"]=draft.Title.Trim(),["LogDescription"]=draft.Summary,["QuestDescription"]=draft.Description,["QuestCompletionLog"]=o.CompletionLog,["AreaDescription"]=o.AreaText,["QuestLevel"]=S(draft.Level),["MinLevel"]=S(draft.MinLevel),["TimeAllowed"]=S(o.TimeSeconds),["SuggestedGroupNum"]=S(o.SuggestedPlayers),["QuestInfoID"]=S(o.QuestInfo),["AllowableRaces"]=S(o.Races),["RewardNextQuest"]=S(o.NextQuest),["StartItem"]=S(o.SourceItem),["RewardSpell"]=S(o.RewardSpell),["Flags"]=S(flags),["RewardXPDifficulty"]=S(xp.Index),["RewardXPMultiplier"]=S(xp.Multiplier),["RewardMoneyDifficulty"]=S(money.Index),["RewardMoneyMultiplier"]=S(money.Multiplier)};if(o.QuestSort!=0)main["QuestSortID"]=S(o.QuestSort);
        for(int i=1;i<=4;i++){main["RewardItem"+i]=S(guaranteed.ElementAtOrDefault(i-1)?.Item??0);main["RewardAmount"+i]=S(guaranteed.ElementAtOrDefault(i-1)?.Count??0);}
        for(int i=1;i<=6;i++){main["RewardChoiceItemID"+i]=S(o.Choices.ElementAtOrDefault(i-1)?.Item??0);main["RewardChoiceItemQuantity"+i]=S(o.Choices.ElementAtOrDefault(i-1)?.Count??0);main["RewardChoiceItemDisplayID"+i]="0";}
        for(int i=1;i<=5;i++){var rep=o.Reputation.ElementAtOrDefault(i-1);main["RewardFactionID"+i]=S(rep?.Faction??0);main["RewardFactionOverride"+i]=S((rep?.Amount??0)*100);main["RewardFactionValue"+i]="0";}
        await Update(db,tx,"quest_template","ID",quest,main);
        await Update(db,tx,"quest_template_addon","ID",quest,new(){["MaxLevel"]=S(o.MaxLevel),["AllowableClasses"]=S(o.Classes),["PrevQuestID"]=S(o.PreviousQuest),["NextQuestID"]=S(o.NextQuest),["ExclusiveGroup"]=S(o.ExclusiveGroup),["RequiredSkillID"]=S(o.RequiredSkill),["RequiredSkillPoints"]=S(o.RequiredSkillPoints),["RequiredMinRepFaction"]=S(o.RequiredRepFaction),["RequiredMinRepValue"]=S(o.RequiredRepValue),["SourceSpellID"]=S(o.SourceSpell),["ProvidedItemCount"]=S(o.SourceItem>0?o.SourceCount:0),["SpecialFlags"]=S((o.Repeat=="Once"?0:1)|(o.Repeat=="Monthly"?16:0))});
        await Update(db,tx,"quest_offer_reward","ID",quest,new(){["RewardText"]=draft.RewardText,["Emote1"]=S(o.CompleteEmote)});await Update(db,tx,"quest_request_items","ID",quest,new(){["CompletionText"]=o.IncompleteText,["EmoteOnComplete"]=S(o.CompleteEmote),["EmoteOnIncomplete"]=S(o.IncompleteEmote)});
        await PatchOne(db,tx,"quest_details",quest,new(){["ID"]=S(quest),["Emote1"]=S(o.AcceptEmote)});
        using(var end=new MySqlCommand($"DELETE FROM {QuestCreation.World}.creature_questender WHERE quest={quest}",db,tx))await end.ExecuteNonQueryAsync();await BonusSpells.WriteAsync(db,tx,QuestCreation.World+".creature_questender",new(){["id"]=S(o.EndNpc>0?o.EndNpc:npc),["quest"]=S(quest)});
        if(changed){using var reserve=new MySqlCommand($"SELECT GREATEST(COALESCE((SELECT MAX(ID) FROM {QuestCreation.World}.quest_objectives),0),COALESCE((SELECT MAX(ID) FROM {AppSettings.Current.BackupDatabase}.quest_editor_objective_ids),0))+1",db,tx);int objectiveId=checked(Convert.ToInt32(await reserve.ExecuteScalarAsync()));foreach(string table in new[]{"quest_objectives","areatrigger_involvedrelation"}){using var clear=new MySqlCommand($"DELETE FROM {QuestCreation.World}.{table} WHERE {(table=="quest_objectives"?"QuestID":"quest")}={quest}",db,tx);await clear.ExecuteNonQueryAsync();}
            int order=0;foreach(var objective in o.Objectives){int id=objective.Id>0&&before.Any(r=>N(r,"ID")==objective.Id&&N(r,"Type")==objective.Type&&N(r,"ObjectID")==objective.Target)?objective.Id:objectiveId++;using(var reserved=new MySqlCommand($"INSERT IGNORE INTO {AppSettings.Current.BackupDatabase}.quest_editor_objective_ids(ID) VALUES({id})",db,tx))await reserved.ExecuteNonQueryAsync();int index=objective.Type is 0 or 1 or 2 or 3 or 9 or 10?order:-1;await BonusSpells.WriteAsync(db,tx,QuestCreation.World+".quest_objectives",new(){["ID"]=S(id),["QuestID"]=S(quest),["Type"]=S(objective.Type),["Order"]=S(order++),["StorageIndex"]=S(index),["ObjectID"]=S(objective.Type==10?-1:objective.Type is 8 or 9?0:objective.Target),["Amount"]=S(objective.Type==10?0:objective.Amount),["Description"]=objective.Text});if(objective.Type==10)await BonusSpells.WriteAsync(db,tx,QuestCreation.World+".areatrigger_involvedrelation",new(){["id"]=S(objective.Target),["quest"]=S(quest)});}
        }
    }
    private static async Task ValidateChain(MySqlConnection db,MySqlTransaction tx,int quest,QuestOptions options){var rows=await WorldDrops.Query(db,tx,$"SELECT ID,PrevQuestID,NextQuestID FROM {QuestCreation.World}.quest_template_addon");var edges=new Dictionary<int,List<int>>();foreach(var row in rows.Where(r=>N(r,"ID")!=quest)){int id=N(row,"ID"),prev=N(row,"PrevQuestID"),next=N(row,"NextQuestID");if(prev>0){if(!edges.ContainsKey(prev))edges[prev]=[];edges[prev].Add(id);}if(next>0){if(!edges.ContainsKey(id))edges[id]=[];edges[id].Add(next);}}if(options.PreviousQuest>0){if(!edges.ContainsKey(options.PreviousQuest))edges[options.PreviousQuest]=[];edges[options.PreviousQuest].Add(quest);}if(options.NextQuest>0){if(!edges.ContainsKey(quest))edges[quest]=[];edges[quest].Add(options.NextQuest);}var visited=new HashSet<int>();var pending=new Stack<int>(edges.GetValueOrDefault(quest)??[]);while(pending.TryPop(out int id)){if(id==quest)throw new InvalidOperationException("These links create a quest-chain cycle.");if(!visited.Add(id))continue;foreach(int next in edges.GetValueOrDefault(id)??[])pending.Push(next);}}
    internal static async Task Update(MySqlConnection db,MySqlTransaction tx,string table,string key,long id,Dictionary<string,string?> values){using var cmd=new MySqlCommand($"UPDATE {QuestCreation.World}.{table} SET {string.Join(',',values.Keys.Select((k,i)=>$"`{k}`=@v{i}"))} WHERE `{key}`=@id",db,tx);cmd.Parameters.AddWithValue("@id",id);int n=0;foreach(var value in values.Values)cmd.Parameters.AddWithValue("@v"+n++,(object?)value??DBNull.Value);await cmd.ExecuteNonQueryAsync();}
    private static async Task PatchOne(MySqlConnection db,MySqlTransaction tx,string table,int quest,Dictionary<string,string?> values){if((await WorldDrops.Query(db,tx,$"SELECT ID FROM {QuestCreation.World}.{table} WHERE ID={quest}")).Count>0){var patch=values.Where(p=>p.Key!="ID").ToDictionary(p=>p.Key,p=>p.Value);await Update(db,tx,table,"ID",quest,patch);}else await BonusSpells.WriteAsync(db,tx,QuestCreation.World+"."+table,values);}
}
