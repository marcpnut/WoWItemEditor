using System.Windows.Media.Imaging;
namespace WoWItemEditor;
internal sealed record NpcPreviewScene(string Name,StaticItemMesh Mesh);
internal static class NpcModelBuilder
{
    private static int N(IReadOnlyDictionary<string,string> row,string key)=>TooltipData.Number(row as Dictionary<string,string>??row.ToDictionary(p=>p.Key,p=>p.Value),key);
    internal static async Task<IReadOnlyList<NpcPreviewScene>> Build(QuestNpcSource source,CatalogResult catalog)
    {
        var assets=ItemModelAssets.Shared;var displays=await assets.Table("CreatureDisplayInfo");var extras=await assets.Table("CreatureDisplayInfoExtra");var models=await assets.Table("CreatureModelData");var options=await assets.Table("CreatureDisplayInfoOption");var slots=await assets.Table("NPCModelItemSlotDisplayInfo");var textures=await assets.Table("TextureFileData");var geosets=await assets.Table("CreatureDisplayInfoGeosetData");var result=new List<NpcPreviewScene>();
        int[] slotMap=[0,2,3,4,5,6,7,8,9,18,14];int[] inventory=[1,3,4,5,6,7,8,9,10,19,16];
        foreach(var model in source.Models)
        {
            int displayId=int.Parse(model["CreatureDisplayID"]!);if(!displays.TryGetValue(displayId,out var display)||!models.TryGetValue(N(display,"ModelID"),out var definition))throw new InvalidOperationException("This NPC model is missing from the configured client.");
            if(geosets.Values.Any(r=>N(r,"CreatureDisplayInfoID")==displayId))throw new InvalidOperationException("This NPC uses special display geosets that are not supported yet. Choose another NPC.");
            int body=N(definition,"FileDataID"),extraId=N(display,"ExtendedDisplayInfoID");
            var equipmentVariants=source.Equipment.Count>0?source.Equipment:new List<Dictionary<string,string?>>{new()};
            if(source.Models.Count*equipmentVariants.Count>32)throw new InvalidOperationException("This NPC has too many appearance variants to preview safely.");
            foreach(var equipmentRow in equipmentVariants)
            {
                StaticItemMesh mesh;
                if(extraId>0)
                {
                    if(!extras.TryGetValue(extraId,out var extra))throw new InvalidOperationException("NPC customization data is unavailable.");
                    var choices=options.Values.Where(r=>N(r,"CreatureDisplayInfoExtraID")==extraId).ToDictionary(r=>N(r,"ChrCustomizationOptionID"),r=>N(r,"ChrCustomizationChoiceID"));
                    if(choices.Count==0)throw new InvalidOperationException("This NPC has no supported face and hair customization mapping.");
                    int material=N(extra,"HDBakeMaterialResourcesID");if(material==0)material=N(extra,"BakeMaterialResourcesID");int skin=textures.Values.Where(r=>N(r,"MaterialResourcesID")==material).OrderBy(r=>N(r,"UsageType")).Select(r=>N(r,"FileDataID")).FirstOrDefault();if(skin==0)throw new InvalidOperationException("The NPC's baked skin and clothing texture is missing.");
                    BitmapSource image=ModelTexture.Decode(await assets.Read(skin));var direct=new Dictionary<int,int>();var equipment=new Dictionary<int,CatalogItem>();
                    foreach(var row in slots.Values.Where(r=>N(r,"NpcModelID")==extraId)){int index=N(row,"ItemSlot");if(index<0||index>=slotMap.Length)throw new InvalidOperationException("Unsupported NPC clothing slot.");int slot=slotMap[index];direct[slot]=N(row,"ItemDisplayInfoID");equipment[slot]=new(new(){["ID"]="0",["Display"]=$"NPC clothing {direct[slot]}",["InventoryType"]=inventory[index].ToString(),["ClassID"]="4"});}
                    for(int i=1;i<=3;i++){int id=int.TryParse(equipmentRow.GetValueOrDefault("ItemID"+i),out int item)?item:0;if(id==0)continue;if(equipmentRow.GetValueOrDefault("AppearanceModID"+i) is string mod&&mod!="0"||equipmentRow.GetValueOrDefault("ItemVisual"+i) is string visual&&visual!="0")throw new InvalidOperationException("This NPC uses special weapon appearance effects that are not supported yet.");equipment[14+i]=catalog.Items.FirstOrDefault(item=>item.Id==id)??throw new InvalidOperationException($"NPC equipment item {id} is unavailable.");}
                    var scene=await CharacterModelBuilder.Build(new(N(extra,"DisplayRaceID"),N(extra,"DisplaySexID"),source.Npc.Name,choices,Math.Max(1,N(extra,"DisplayClassID"))),equipment,rangedWeapons:!equipment.ContainsKey(15)&&!equipment.ContainsKey(16),npc:new(body,image,direct));mesh=scene.Mesh;
                }
                else
                {
                    if(equipmentRow.Any(p=>p.Key.StartsWith("ItemID")&&p.Value!="0"))throw new InvalidOperationException("Non-character NPC weapon attachments are not supported yet.");
                    var replacements=new Dictionary<int,BitmapSource>();for(int i=1;i<=4;i++){int file=N(display,"TextureVariationFileDataID"+i);if(file>0)replacements[10+i]=ModelTexture.Decode(await assets.Read(file));}
                    mesh=await StaticItemModel.Load(assets,new(body,[]),typeTextures:replacements,pose:await CharacterRig.Stand(assets,body));if(mesh.Textures==0)throw new InvalidOperationException("This NPC's complete textured appearance is unavailable: "+mesh.Note);
                }
                result.Add(new($"{source.Npc.Name} · display {displayId} · equipment {equipmentRow.GetValueOrDefault("ID")??"none"}",mesh));
            }
        }
        return result;
    }
}
