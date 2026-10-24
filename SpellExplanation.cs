using System.Globalization;
namespace AnoWowItemEditor;

internal static class SpellExplanation
{
    internal static string Time(int ms)=>ms<0?"Spell default":ms==0?"None / instant":(ms/1000m).ToString("0.###",CultureInfo.InvariantCulture)+" seconds";
    // Reference selector names: TrinityCore SharedDefines.h. Unknown/custom selectors stay explicit.
    internal static string Target(int id)=>id switch{0=>"No additional selector",1=>"Caster / wearer",2=>"Nearby enemy",3=>"Nearby ally",4=>"Nearby party member",5=>"Pet",6=>"Selected enemy",15=>"Enemies around source",16=>"Enemies at destination",18=>"Caster position",20=>"Party around caster",21=>"Selected ally",24=>"Enemies in cone",25=>"Selected unit",27=>"Owner / master",30=>"Allies around source",31=>"Allies at destination",35=>"Selected party member",56=>"Raid around caster",57=>"Selected raid member",_=>$"Selector {id} (server-defined)"};
    internal static string Duration(TooltipData data,int spell)
    {
        if(!data.Misc.TryGetValue(spell,out var misc))return "Unavailable (spell metadata missing)";
        int index=TooltipData.Number(misc,"DurationIndex");if(index==0)return "No duration entry; instantaneous or passive behavior depends on the spell";
        if(!data.Durations.TryGetValue(index,out var row))return $"Unavailable (duration {index})";
        int ms=TooltipData.Number(row,"Duration");if(ms<0)return "Indefinite while conditions hold";
        return Time(ms)+(TooltipData.Amount(row,"DurationPerResource")!=0||TooltipData.Amount(row,"MaxDuration")>ms?" [varies with spell context]":"");
    }
    internal static string Effect(TooltipData data,Dictionary<string,string> row,string? draftValue=null)
    {
        int spell=TooltipData.Number(row,"SpellID"),effect=TooltipData.Number(row,"Effect"),aura=TooltipData.Number(row,"EffectAura");
        var rule=BonusValueRules.For(row);string value=draftValue??row.GetValueOrDefault("EffectBasePointsF")??row.GetValueOrDefault("EffectBasePoints")??"[unavailable]";
        var lines=new List<string>{$"{SpellTypeNames.Effect(effect)}"+(aura!=0?" · "+SpellTypeNames.Aura(aura):"")};
        if(rule.Supported)lines.Add($"{rule.Meaning}.\nStored effect amount: {value} {rule.Unit}.");
        else if(effect==6&&aura is 3 or 8&&BonusSpells.FixedAmount(row))lines.Add($"{(aura==3?"Periodic damage":"Periodic healing")}: {value} points per tick; value editing is read-only.");
        else lines.Add($"Stored amount: {value}. {rule.Reason} This is not a calculated final damage/healing value.");
        if(effect==6&&aura==29)lines.Add("Attribute: "+(TooltipData.Number(row,"EffectMiscValue1") switch{0=>"Strength",1=>"Agility",2=>"Stamina",3=>"Intellect",4=>"Spirit",-1=>"All attributes",var n=>$"Attribute selector {n}"}));
        if(aura is 107 or 108)lines.Add("Affected ability: the spell's family/class mask selects abilities. The bonus name/description identifies intended abilities; this is not a global bonus to every attack.");
        lines.Add($"Targets: {Target(TooltipData.Number(row,"ImplicitTarget1"))}; {Target(TooltipData.Number(row,"ImplicitTarget2"))}. Target selectors do not define a maximum target count.");
        int period=TooltipData.Number(row,"EffectAuraPeriod");if(period>0)lines.Add($"Tick interval: {Time(period)}. The amount applies per tick; no total is inferred for variable durations.");
        lines.Add("Duration: "+Duration(data,spell));
        int triggered=TooltipData.Number(row,"EffectTriggerSpell");if(triggered>0){int displayId=data.Spells.TryGetValue(triggered,out var triggeredText)&&TooltipData.Number(triggeredText,"PreviewOriginalId")>0?TooltipData.Number(triggeredText,"PreviewOriginalId"):triggered;lines.Add($"Triggers {data.SpellName(triggered)} · Spell {displayId}. Its effects are separate from this stored amount.");}
        int chain=TooltipData.Number(row,"EffectChainTargets");if(chain>0)lines.Add($"Chain target setting: {chain}. Other target caps/conditions may also apply.");
        lines.Add(rule.Limits);return string.Join("\n",lines);
    }
    internal static string Describe(TooltipData data,int spell,ContentRow? assignment=null,BonusDraft? draft=null,bool proc=false)
    {
        int original=spell;if(draft is not null){data=ContentEditorWindow.PreviewData(data,data);spell=BonusSpells.Preview(draft,data,2000000000);var map=draft.Source.Scope.SpellIds.Select((id,n)=>(id,target:2000000000+n)).ToDictionary(p=>p.id,p=>p.target);foreach(var (old,id) in map){data.Spells[id]["PreviewOriginalId"]=old.ToString(CultureInfo.InvariantCulture);foreach(var key in data.Effects.Keys.Where(k=>k.Spell==id).ToArray()){var effect=new Dictionary<string,string>(data.Effects[key],StringComparer.OrdinalIgnoreCase){["SpellID"]=id.ToString(CultureInfo.InvariantCulture)};if(map.TryGetValue(TooltipData.Number(effect,"EffectTriggerSpell"),out int child))effect["EffectTriggerSpell"]=child.ToString(CultureInfo.InvariantCulture);data.Effects[key]=effect;}}}
        var lines=new List<string>{$"{data.SpellName(spell)} · Spell {original}",data.SpellDescription(spell)};
        if(assignment is {} row){
            if(!proc)lines.Add($"Activation: at least {row.Pieces} equipped set pieces; specialization {row.Specialization}, trait {row.Trait} (0 = unrestricted).");
            else lines.Add($"Activation: {ReferenceCatalog.Trigger(row.Trigger)}. "+(row.Trigger==2?"Chance on hit is an event trigger, not a 100% proc chance. Chance/PPM and eligible attacks depend on spell/server rules.":"The spell's own conditions still apply."));
            if(proc)lines.Add($"Item cooldown: {Time(row.Cooldown)} · Category cooldown: {Time(row.CategoryCooldown)}\nCharges: {(row.Charges==0?"0 (no item charge count)":row.Charges.ToString(CultureInfo.InvariantCulture))} · Category {row.Category} · Specialization {row.Specialization} · Condition {row.Condition}. Zero restriction IDs mean no additional item restriction.");
        }
        var pending=new Queue<int>();var visited=new HashSet<int>();pending.Enqueue(spell);
        while(pending.Count>0&&visited.Count<32){int id=pending.Dequeue();if(!visited.Add(id))continue;if(data.Spells.TryGetValue(id,out var text))foreach(System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(TooltipData.Read(text,"Description")+TooltipData.Read(text,"AuraDescription"),@"\$(\d+)[a-zA-Z]"))pending.Enqueue(int.Parse(match.Groups[1].Value));var effects=data.Effects.Where(p=>p.Key.Spell==id).OrderBy(p=>p.Key.Index).ToArray();if(effects.Length==0)lines.Add($"{data.SpellName(id)}: effect data unavailable; behavior cannot be explained fully.");foreach(var pair in effects){lines.Add($"{data.SpellName(id)} · Effect {pair.Key.Index+1}\n{Effect(data,pair.Value)}");int child=TooltipData.Number(pair.Value,"EffectTriggerSpell");if(child>0&&!visited.Contains(child))pending.Enqueue(child);}}
        if(pending.Count>0)lines.Add("Further triggered effects omitted after 32 spells.");
        lines.Add("Read from installed spell data. Scripts, talents, conditions and combat formulas may change the actual result. These explanations do not simulate proc DPS.");return string.Join("\n\n",lines);
    }
}
