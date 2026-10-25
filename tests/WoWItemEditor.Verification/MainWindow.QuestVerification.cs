using System.IO;
using System.Text.Json;
namespace WoWItemEditor;
public partial class MainWindow
{
    private async Task VerifyQuests(CatalogResult catalog)
    {
        var list=await QuestCreation.Browse();if(list.Count==0)throw new InvalidOperationException("No eligible quest givers.");var results=new List<object>();
        foreach(int id in new[]{167,196,197,239}){var source=await QuestCreation.Source(id);var scenes=await NpcModelBuilder.Build(source,catalog);if(scenes.Count==0||scenes.Any(s=>s.Mesh.Triangles==0||s.Mesh.Textures==0))throw new InvalidOperationException("NPC scene missing geometry/textures.");var view=new ItemModelView();var window=new System.Windows.Window{Title=source.Npc.Name,Content=view,Width=950,Height=950,Owner=this};window.Show();view.ShowScene(scenes[0].Mesh,scenes[0].Name);await RenderEditorWindow(window,"npc-"+id);window.Close();results.Add(new{id,variants=scenes.Count,triangles=scenes[0].Mesh.Triangles,textures=scenes[0].Mesh.Textures});}
        QuestPlacement? placement=null;foreach(var hero in (await CharacterRepository.ListAsync()).Where(c=>!c.Online)){try{placement=await QuestCreation.Capture(hero.Guid);break;}catch(InvalidOperationException){}}
        if(placement is null)throw new InvalidOperationException("An offline outdoor character is required for the rollback save test.");
        using(var db=await DatabaseConnection.OpenAsync(CatalogRepository.Database)){
            await ContentEditing.InitializeAsync(db);await QuestCreation.Initialize(db);using var tx=await db.BeginTransactionAsync();
            var source=await QuestCreation.Source(167,db,tx);var draft=new QuestDraft("Local rollback test","Local rollback quest","Talk to this NPC.","A local rollback verification.","Here is your item.",1,1,22418,1,true);
            var saved=await QuestCreation.SaveCore(db,tx,source,draft,placement);
            var spawn=(await WorldDrops.Query(db,tx,$"SELECT * FROM {QuestCreation.World}.creature WHERE guid={saved.SpawnId}")).Single();
            var quest=(await WorldDrops.Query(db,tx,$"SELECT * FROM {QuestCreation.World}.quest_template WHERE ID={saved.QuestId}")).Single();
            if(spawn["id"]!=saved.NpcId.ToString()||quest["RewardItem1"]!="22418"||quest["RewardAmount1"]!="1"||(await QuestCreation.Source(167,db,tx)).Fingerprint!=source.Fingerprint)throw new InvalidOperationException("Quest copy transaction verification failed.");
            await tx.RollbackAsync();if((await WorldDrops.Query(db,null,$"SELECT guid FROM {QuestCreation.World}.creature WHERE guid={saved.SpawnId}")).Count!=0||(await WorldDrops.Query(db,null,$"SELECT ID FROM {QuestCreation.World}.quest_template WHERE ID={saved.QuestId}")).Count!=0)throw new InvalidOperationException("Rollback failed.");
        }
        var creator=new QuestCreatorWindow(catalog){Owner=this};creator.Show();await creator.Ready;await RenderEditorWindow(creator,"quest-creator-local");creator.Close();
        await File.WriteAllTextAsync("artifacts/quest-verification.json",JsonSerializer.Serialize(new{eligible=list.Count,previews=results,databaseWrites="Save transaction verified and rolled back; no NPC or quest left in world"},new JsonSerializerOptions{WriteIndented=true}));
    }
    private async Task InspectQuestAssets()
    {
        var assets=ItemModelAssets.Shared;var displays=await assets.Table("CreatureDisplayInfo");var extra=await assets.Table("CreatureDisplayInfoExtra");var models=await assets.Table("CreatureModelData");var result=new List<object>();
        foreach(int id in new[]{1844,3251,1859,3265}){var row=displays[id];int model=TooltipData.Number(row,"ModelID");var definition=models[model];result.Add(new{id,display=row,extra=extra.GetValueOrDefault(TooltipData.Number(row,"ExtendedDisplayInfoID")),model=definition,mesh=await StaticItemModel.Describe(assets,TooltipData.Number(definition,"FileDataID"))});}
        var linked=new Dictionary<string,object>();foreach(string table in new[]{"CreatureDisplayInfoOption","CreatureDisplayInfoGeosetData","NPCModelItemSlotDisplayInfo","ChrCustomizationDisplayInfo","CharHairGeosets","TextureFileData"}){var rows=await assets.Table(table);linked[table]=new{example=rows.Values.Take(2),matches=rows.Values.Where(r=>r.Any(p=>(p.Key.Contains("Display")||p.Key.Contains("Material")||p.Key.Contains("Npc"))&&new[]{1844,3251,1859,3265,515,548,584,1557,116887,116888}.Contains(int.TryParse(p.Value,out int n)?n:0))).Take(100).ToArray()};}await File.WriteAllTextAsync("artifacts/quest-linked-assets.json",JsonSerializer.Serialize(linked,new JsonSerializerOptions{WriteIndented=true}));
        await File.WriteAllTextAsync("artifacts/quest-assets.json",JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
    }
}
