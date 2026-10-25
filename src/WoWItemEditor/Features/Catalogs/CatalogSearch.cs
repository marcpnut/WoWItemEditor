using System.Runtime.CompilerServices;
namespace WoWItemEditor;

internal static class CatalogSearch
{
    private static ConditionalWeakTable<CatalogItem,Text> itemText=new();
    private static readonly ConditionalWeakTable<ReferenceEntry,Text> referenceText=new();
    private sealed record Text(string Value);
    private static TooltipData? data;
    internal static void Configure(TooltipData source){data=source;itemText=new();}
    internal static bool Matches(string query,string text)=>string.IsNullOrWhiteSpace(query)||query.Trim().Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).All(word=>text.Contains(word,StringComparison.OrdinalIgnoreCase));
    internal static bool Reference(ReferenceEntry entry,string query)
    {
        if(string.IsNullOrWhiteSpace(query))return true;
        string extra=referenceText.GetValue(entry,e=>new(string.Join(" ",e.SpellId,e.EffectId,string.Join(" ",e.EffectTypes.Select(SpellTypeNames.Effect)),string.Join(" ",e.AuraTypes.Select(SpellTypeNames.Aura)),string.Join(" ",e.Triggers),string.Join(" ",e.SetIds),string.Join(" ",new[]{(1,"Physical"),(2,"Holy"),(4,"Fire"),(8,"Nature"),(16,"Frost"),(32,"Shadow"),(64,"Arcane")}.Where(s=>(e.SchoolMask&s.Item1)!=0).Select(s=>s.Item2)),e.UsedBySets?"Used by sets":"",e.UsedByItems?"Used by items":"",e.HasProcRules?"Server proc rules":""))).Value;
        // Keep the already-loaded raw details in place instead of copying them on every keystroke.
        return query.Trim().Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).All(word=>entry.Name.Contains(word,StringComparison.OrdinalIgnoreCase)||entry.Description.Contains(word,StringComparison.OrdinalIgnoreCase)||entry.Trigger.Contains(word,StringComparison.OrdinalIgnoreCase)||entry.Kind.Contains(word,StringComparison.OrdinalIgnoreCase)||entry.Details.Contains(word,StringComparison.OrdinalIgnoreCase)||extra.Contains(word,StringComparison.OrdinalIgnoreCase));
    }
    internal static bool Item(CatalogItem item,string query)
    {
        if(string.IsNullOrWhiteSpace(query))return true;return Matches(query,itemText.GetValue(item,Build).Value);
    }
    private static Text Build(CatalogItem item)
    {
        var fields=item.Fields.Select(p=>p.Key+": "+p.Value).ToList();fields.AddRange([item.Name,item.Id.ToString(),item.SlotName,item.SubtypeName,item.QualityName,item.TypeName,item.Source]);
        fields.AddRange(Enumerable.Range(1,10).Where(n=>item.Number("StatPercentEditor"+n)>0).Select(n=>ItemTooltip.StatNames.GetValueOrDefault(item.Number("StatModifierBonusStat"+n),"")));
        if(data is not null){if(data.ItemSpells.TryGetValue(item.Id,out var spells))foreach(var spell in spells)fields.Add($"{spell.SpellId} {data.SpellName(spell.SpellId)} {data.SpellDescription(spell.SpellId)} {ReferenceCatalog.Trigger(spell.Trigger)}");if(data.Sets.TryGetValue(item.Number("ItemSet"),out var set)){fields.Add(set.Name);foreach(var bonus in set.Bonuses)fields.Add($"{bonus.Pieces} pieces {bonus.SpellId} {data.SpellName(bonus.SpellId)} {data.SpellDescription(bonus.SpellId)}");}fields.AddRange(data.Classes.Where(c=>item.Number("AllowableClass")==-1||(item.Number("AllowableClass")&(1<<(c.Key-1)))!=0).Select(c=>c.Value));if(data.Skills.TryGetValue(item.Number("RequiredSkill"),out var skill))fields.Add(skill);if(data.Factions.TryGetValue(item.Number("RequiredReputationFaction"),out var faction))fields.Add(faction);}
        return new(string.Join("\n",fields));
    }
}
