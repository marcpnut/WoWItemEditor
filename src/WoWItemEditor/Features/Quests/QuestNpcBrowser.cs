using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace WoWItemEditor;
internal static class QuestNpcBrowser
{
    private static readonly SemaphoreSlim gate=new(1);

    internal static async Task NameFactions(IEnumerable<QuestNpc> npcs)
    {
        var templates=await ItemModelAssets.Shared.Table("FactionTemplate");var factions=await ItemModelAssets.Shared.Table("Faction");
        foreach(var npc in npcs){int id=templates.TryGetValue(npc.Faction,out var row)?TooltipData.Number(row,"Faction"):0;string name=factions.TryGetValue(id,out var faction)?faction.GetValueOrDefault("Name_lang")??faction.GetValueOrDefault("Name")??"":"";npc.FactionName=string.IsNullOrWhiteSpace(name)?"Unaffiliated / unnamed faction":name;}
    }
    internal static async Task Scan(CatalogResult catalog,Action<QuestNpc?,int,int> progress,CancellationToken cancellation,int limit=int.MaxValue)
    {
        await gate.WaitAsync(cancellation);try{
            var all=await QuestCreation.Browse();await NameFactions(all);string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WoWItemEditor");Directory.CreateDirectory(folder);string file=Path.Combine(folder,"npc-preview-capabilities-v2.json");var known=new Dictionary<string,bool>();if(File.Exists(file))try{known=JsonSerializer.Deserialize<Dictionary<string,bool>>(await File.ReadAllTextAsync(file,cancellation))??[];}catch(JsonException){}
            int done=0;foreach(var npc in all.Take(limit)){cancellation.ThrowIfCancellationRequested();bool supported=false;try{var source=await QuestCreation.Source(npc.Id);string key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ItemModelAssets.Shared.BuildIdentity+"/"+AppSettings.Current.Db2Folder+"/"+source.Fingerprint)));if(!known.TryGetValue(key,out supported)){try{supported=(await Task.Run(()=>NpcModelBuilder.Build(source,catalog),cancellation)).Count>0;}catch(OperationCanceledException){throw;}catch{supported=false;}if(supported)known[key]=true;}}
                catch(OperationCanceledException){throw;}catch{supported=false;}
                progress(supported?npc:null,++done,all.Count);if(done%20==0)await File.WriteAllTextAsync(file,JsonSerializer.Serialize(known),cancellation);
            }
            await File.WriteAllTextAsync(file,JsonSerializer.Serialize(known),cancellation);
        }finally{gate.Release();}
    }
}
