using System.Text.Json;
namespace WoWItemEditor;

internal sealed record SetCheck(string Status,string Check,string Subject,string Detail,string Action,int ItemId=0,int SpellId=0);
internal sealed record SetDropCheck(int ItemId,int Rows,string Summary,bool Consistent);
internal static class SetChecklist
{
    internal static async Task<Dictionary<int,SetDropCheck>> LoadDrops(IEnumerable<int> items)
    {
        int[] ids=items.Distinct().ToArray();using var db=await DatabaseConnection.OpenAsync(CatalogRepository.Database);bool hasPolicies=await HotfixHistory.HasTable(db,$"{AppSettings.Current.BackupDatabase}","item_editor_world_drops");using var tx=await db.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var loot=await WorldDrops.Query(db,tx,$"SELECT Item,Entry,Chance,GroupId,QuestRequired,LootMode FROM {AppSettings.Current.WorldDatabase}.creature_loot_template WHERE Item IN({WorldDrops.Ids(ids)}) AND Reference=0 ORDER BY Item,Entry");
        var policies=hasPolicies?await WorldDrops.Query(db,tx,$"SELECT ItemId,PlanJson FROM {WorldDrops.Metadata} WHERE ItemId IN({WorldDrops.Ids(ids)})"):[];
        await tx.CommitAsync();return ids.ToDictionary(id=>id,id=>{
            var rows=loot.Where(r=>r["Item"]==id.ToString()).ToArray();var policy=policies.FirstOrDefault(r=>r["ItemId"]==id.ToString());DropPlan? plan=null;string error="";
            if(policy is not null)try{plan=JsonSerializer.Deserialize<DropPlan>(policy["PlanJson"]!);if(plan is null)error="Stored drop plan is empty. ";}catch(JsonException){error="Stored drop plan cannot be read. ";}
            bool matches=error.Length==0&&(plan is null||plan.Targets.Select(t=>t.Loot).Distinct().Order().SequenceEqual(rows.Select(r=>int.Parse(r["Entry"]!)).Order())&&rows.All(r=>decimal.Parse(r["Chance"]!,System.Globalization.CultureInfo.InvariantCulture)==plan.Chance&&r["GroupId"]=="0"&&r["QuestRequired"] is "0" or "False"&&r["LootMode"]=="1"));
            string summary=error+(plan?.Summary??"Unmanaged direct creature loot")+$" · {rows.Length} saved rows";
            if(plan is not null)summary+="\nDestinations: "+string.Join("; ",plan.Targets.Take(12).Select(t=>$"{t.Name} · {t.Zones}"))+(plan.Targets.Count>12?"; … open drop locations for all":"");
            summary+="\nSaved loot: "+string.Join("; ",rows.Take(16).Select(r=>$"Table {r["Entry"]}: {r["Chance"]}%"+(r["GroupId"]!="0"?$" (group {r["GroupId"]}; not an independent chance)":"")))+(rows.Length>16?"; …":"");
            if(!matches)summary+="\nSaved rows differ from the retained plan. Review before testing.";
            return new SetDropCheck(id,rows.Length,summary,matches);
        });
    }
    internal static List<SetCheck> Build(CatalogResult catalog,ItemSetInfo set,int level,CharacterSnapshot? character,IReadOnlyDictionary<int,SetDropCheck>? drops=null)
    {
        var result=new List<SetCheck>();var lookup=catalog.Items.ToDictionary(i=>i.Id);var pieces=set.Members.Where(lookup.ContainsKey).Select(id=>lookup[id]).ToArray();
        void Add(string state,string check,string subject,string detail,string action,int item=0,int spell=0)=>result.Add(new(state,check,subject,detail,action,item,spell));
        foreach(int id in set.Members.Where(id=>!lookup.ContainsKey(id)))Add("Blocked","Definition",$"Item {id}","Member definition is missing. Totals and reachable thresholds are incomplete.","Membership");
        if(set.Members.Length==0)Add("Blocked","Membership",set.Name,"No members are assigned.","Membership");
        var empty=character??new CharacterSnapshot(new(0,"Checklist",level,1,false,0),[]);var equipped=SetDesignReview.Equip(empty,pieces,lookup,level);int reachable=Math.Max(equipped.Slots.Count,SetDesignReview.Equip(empty,pieces.Where(i=>i.Slot!=17),lookup,level).Slots.Count);
        int classes=-1;foreach(var item in pieces){
            int mask=item.Number("AllowableClass");if(mask>0)classes&=mask;
            var problems=catalog.Tooltips.Calculations.ServerProblems(item);var values=catalog.Tooltips.Calculations.Evaluate(item,level);
            Add(problems.Count>0?"Blocked":values.ItemLevel is null?"Review":"Checked","Scaling & server rows",item.Name,problems.Count>0?string.Join("\n",problems):values.Note,"Item",item.Id);
            var requirements=new List<string>();if(item.RequiredLevel>level)requirements.Add($"Requires level {item.RequiredLevel}; preview level is {level}.");if(character is not null&&mask>0&&(mask&(1<<(character.Character.ClassId-1)))==0)requirements.Add("Selected character's class is excluded.");int skill=item.Number("RequiredSkill");if(character is not null&&skill>0&&(character.Skills?.GetValueOrDefault(skill)??0)<item.Number("RequiredSkillRank"))requirements.Add("Selected character's saved skill rank is insufficient.");
            Add(requirements.Count>0?"Blocked":"Checked","Equip requirements",item.Name,requirements.Count>0?string.Join("\n",requirements):$"Required level {item.RequiredLevel} · {item.SubtypeName}. "+(character is null?"No character selected; class/skill proficiencies need a character check.":"Stored level, class mask and skill requirements passed; class proficiencies/talents still need in-game verification."),"Item",item.Id);
            if(drops is null||!drops.TryGetValue(item.Id,out var drop))Add("Review","Drop assignment",item.Name,"Drop data has not loaded. No conclusion about availability can be drawn.","Drops",item.Id);
            else Add(drop.Rows==0||!drop.Consistent?"Review":"Checked","Drop assignment",item.Name,drop.Summary+"\nChecks cover direct creature loot only; reference/chest/vendor/script sources are outside this check.","Drops",item.Id);
            foreach(var proc in catalog.Tooltips.ItemSpells.GetValueOrDefault(item.Id)??[])Spell(proc.SpellId,$"{item.Name} · {ReferenceCatalog.Trigger(proc.Trigger)}",item.ClassId==2?"Proc":"Spell",item.Id);
        }
        if(classes==0)Add("Blocked","Allowed classes",set.Name,"No class is allowed to wear all selected pieces.","Membership");
        foreach(var p in equipped.Problems.Where(p=>p.Contains("no free compatible")))Add("Blocked","Slot conflict",set.Name,p,"Membership");
        foreach(var bonus in set.Bonuses){Add(bonus.Pieces>reachable||bonus.Pieces<1?"Blocked":"Checked","Bonus threshold",$"{bonus.Pieces}-piece · {catalog.Tooltips.SpellName(bonus.SpellId)}",$"{reachable} distinct compatible set slots can be filled. "+(bonus.Pieces<=reachable?"Threshold is reachable.":"Threshold cannot be reached with these members.")+$"\nSpecialization {bonus.Specialization}, trait {bonus.Trait}; set skill {set.RequiredSkill}/{set.RequiredSkillRank}, flags {set.Flags}. Zero IDs mean unrestricted; nonzero conditions need verification.","Bonus",spell:bonus.SpellId);Spell(bonus.SpellId,$"{bonus.Pieces}-piece bonus","Bonus");}
        if(set.Bonuses.Length==0)Add("Review","Set bonuses",set.Name,"No set bonuses are assigned.","Bonus");
        if(set.Flags!=0||set.RequiredSkill!=0||set.Bonuses.Any(b=>b.Specialization!=0||b.Trait!=0)){bool unmet=character is not null&&set.RequiredSkill>0&&(character.Skills?.GetValueOrDefault(set.RequiredSkill)??0)<set.RequiredSkillRank;Add(unmet?"Blocked":"Review","Bonus conditions",set.Name,(unmet?"The character's saved skill rank does not meet the set requirement. ":"")+"Nonzero set flags, skill, specialization or trait conditions apply. Reachable piece counts alone do not prove these bonuses will activate.","Bonus");}
        foreach(var repeated in set.Bonuses.GroupBy(b=>(b.SpellId,b.Specialization,b.Trait)).Where(g=>g.Count()>1))Add("Review","Repeated bonus",set.Name,$"Spell {repeated.Key.SpellId} is assigned more than once with the same restrictions. Verify that stacking is intended.","Bonus",spell:repeated.Key.SpellId);
        Add("In game","Live testing",set.Name,"After server reload: equip every piece; compare tooltip values and bonus activation at each threshold; test eligible attacks, proc timing and actual loot. Database checks do not execute custom server scripts.","Preview");return result;
        void Spell(int spell,string subject,string action,int item=0){string description=catalog.Tooltips.SpellDescription(spell);bool exists=catalog.Tooltips.Spells.ContainsKey(spell);bool effects=catalog.Tooltips.Effects.Keys.Any(k=>k.Spell==spell);bool unresolved=new[]{"[scales]","[context value]","[value unavailable]","[duration]","[radius]","[description unavailable]"}.Any(description.Contains);Add(!exists?"Blocked":!effects||unresolved?"Review":"Checked","Spell data",subject,$"{catalog.Tooltips.SpellName(spell)} · Spell {spell}\n{description}\n"+(!exists?"Spell definition missing.":!effects?"Effect records unavailable; behavior cannot be fully checked.":unresolved?"Tooltip contains unresolved context/scaling values.":"Referenced description and effects are available; scripts/conditions still require in-game testing."),action,item,spell);}
    }
}
