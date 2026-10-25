namespace WoWItemEditor;
internal static class ModelCharacterRepository
{
    internal static async Task<ModelCharacter> Load(CharacterInfo character)
    {
        using var db=await DatabaseConnection.OpenAsync(CharacterRepository.Database);var row=(await WorldDrops.Query(db,null,$"SELECT race,gender FROM characters WHERE guid={character.Guid}")).FirstOrDefault()??throw new InvalidOperationException("Character no longer exists.");var choices=new Dictionary<int,int>();
        if(await HotfixHistory.HasTable(db,CharacterRepository.Database,"character_customizations")){var columns=(await WorldDrops.Query(db,null,"SHOW COLUMNS FROM character_customizations")).Select(c=>c["Field"]!).ToHashSet(StringComparer.OrdinalIgnoreCase);string? Find(params string[] names)=>names.FirstOrDefault(columns.Contains);string? option=Find("chrCustomizationOptionID","optionId","option"),choice=Find("chrCustomizationChoiceID","choiceId","choice"),guid=Find("guid","characterGuid");if(option is not null&&choice is not null&&guid is not null)foreach(var c in await WorldDrops.Query(db,null,$"SELECT `{option}` AS OptionId,`{choice}` AS ChoiceId FROM character_customizations WHERE `{guid}`={character.Guid}"))choices[int.Parse(c["OptionId"]!)]=int.Parse(c["ChoiceId"]!);}
        return new(int.Parse(row["race"]!),int.Parse(row["gender"]!),character.Name,choices,character.ClassId);
    }
}
