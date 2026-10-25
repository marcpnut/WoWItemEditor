namespace WoWItemEditor;
internal static class QuestNpcBrowser
{

    internal static async Task NameFactions(IEnumerable<QuestNpc> npcs)
    {
        var templates=await ItemModelAssets.Shared.Table("FactionTemplate");var factions=await ItemModelAssets.Shared.Table("Faction");
        foreach(var npc in npcs){int id=templates.TryGetValue(npc.Faction,out var row)?TooltipData.Number(row,"Faction"):0;string name=factions.TryGetValue(id,out var faction)?faction.GetValueOrDefault("Name_lang")??faction.GetValueOrDefault("Name")??"":"";npc.FactionName=string.IsNullOrWhiteSpace(name)?"Unaffiliated / unnamed faction":name;}
    }
}
