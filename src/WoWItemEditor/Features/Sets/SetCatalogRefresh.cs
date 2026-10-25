namespace WoWItemEditor;

// Keep the large installed/reference catalog; replace only definitions changed by this workflow.
internal static class SetCatalogRefresh
{
    internal static CatalogResult Items(CatalogResult catalog,IEnumerable<CatalogItem> changed)
    {
        var replacements=changed.DistinctBy(i=>i.Id).ToDictionary(i=>i.Id);
        foreach(var set in catalog.Tooltips.Sets.Values.ToArray()){
            var members=set.Members.Where(id=>!replacements.TryGetValue(id,out var item)||item.Number("ItemSet")==set.Id)
                .Concat(replacements.Values.Where(i=>i.Number("ItemSet")==set.Id).Select(i=>i.Id)).Distinct().ToArray();
            if(!members.SequenceEqual(set.Members))catalog.Tooltips.Sets[set.Id]=set with{Members=members};
        }
        return catalog with{Items=catalog.Items.Where(i=>!replacements.ContainsKey(i.Id)).Concat(replacements.Values).OrderBy(i=>i.Name,StringComparer.OrdinalIgnoreCase).ToList()};
    }
    internal static CatalogResult CreatedSet(CatalogResult catalog,CreatedSetSession session,CreatedSetSession? previous,ReferenceCatalog refs)
    {
        var changed=session.Members.Values.Select(p=>p.Item.Current).ToList();
        if(previous?.Id==session.Id)foreach(var removed in previous.Members.Where(p=>!session.Members.ContainsKey(p.Key)))
            changed.Add(new(new(removed.Value.Item.Current.Fields,StringComparer.OrdinalIgnoreCase){["ItemSet"]="0"},true));
        var result=Items(catalog,changed);
        var row=session.Expected.Tables["new_set"].First();
        int Number(string key)=>int.TryParse(row.GetValueOrDefault(key),out int n)?n:0;
        var bonuses=session.Expected.Tables["new_set_bonuses"].Select(b=>new SetBonus(int.Parse(b["Threshold"]!),int.Parse(b["SpellID"]!),int.Parse(b["ChrSpecID"]!),int.Parse(b["TraitSubTreeID"]!))).OrderBy(b=>b.Pieces).ToArray();
        result.Tooltips.Sets[session.Id]=new(session.Id,session.Name,session.Members.Keys.ToArray(),bonuses,Number("SetFlags"),Number("RequiredSkill"),Number("RequiredSkillRank"));
        foreach(var piece in session.Members.Values){
            result.Tooltips.ItemSpells[piece.Item.Current.Id]=piece.Rows["item_x_item_effect"].Select(link=>refs.ItemEffects.GetValueOrDefault(TooltipData.Number(link,"ItemEffectID"))).Where(e=>e is not null)
                .Select(e=>new ItemSpellInfo(TooltipData.Number(e!,"SpellID"),TooltipData.Number(e!,"TriggerType"),TooltipData.Number(e!,"CoolDownMSec"))).ToList();
        }
        return result;
    }
}
