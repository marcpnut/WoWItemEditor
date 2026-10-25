using System.IO;
using DBCD.Providers;
using TACTSharp;
namespace WoWItemEditor;

internal sealed record ItemModelPart(int Model,int[] Textures,int Component=0);
internal sealed record ItemModelAppearance(int Appearance,int Display,ItemModelPart[] Parts,string Detail)
{
    internal int[] Models=>Parts.Select(p=>p.Model).ToArray();
    internal int[] AdditionalTextures {get;init;}=[];
    internal int[] Textures=>Parts.SelectMany(p=>p.Textures).Concat(AdditionalTextures).Distinct().ToArray();
}
internal sealed class ItemModelAssets
{
    internal const string DefaultClient=@"C:\Program Files (x86)\World of Warcraft";
    private BuildInstance build=new();
    private string buildStamp="";
    private readonly SemaphoreSlim gate=new(1);
    private bool loaded;
    private Dictionary<int,Dictionary<string,string>> modified=[],appearances=[],displays=[],models=[],textures=[];
    private readonly Dictionary<string,Dictionary<int,Dictionary<string,string>>> extraTables=[];
    internal string ClientFolder {get;private set;}="";
    internal string Version {get;private set;}="";
    internal string BuildIdentity=>ClientFolder+"|"+Version;
    internal static ItemModelAssets Shared {get;}=new();
    internal async Task<ItemModelAppearance> Resolve(int item,string? client=null,bool refresh=false,bool standaloneRequired=true)
    {
        await gate.WaitAsync();try{
            await Task.Run(()=>LoadClient(client));
            if(refresh){loaded=false;extraTables.Clear();}
            if(!loaded){using var db=await DatabaseConnection.OpenAsync(CatalogRepository.Database);var reader=new DBCD.DBCD(new FilesystemDBCProvider(CatalogRepository.Db2Folder),new GithubDBDProvider(true));
                async Task<Dictionary<int,Dictionary<string,string>>> Table(string dbc,string sql){var rows=await Task.Run(()=>TooltipData.LoadTable(reader,dbc));if(await HotfixHistory.HasTable(db,CatalogRepository.Database,sql))await TooltipData.Overlay(db,sql,dbc,rows);return rows;}
                modified=await Table("ItemModifiedAppearance","item_modified_appearance");appearances=await Table("ItemAppearance","item_appearance");displays=await Table("ItemDisplayInfo","item_display_info");models=await Table("ModelFileData","model_file_data");textures=await Table("TextureFileData","texture_file_data");loaded=true;
            }
            // Query this item's mappings on each selection so newly duplicated/edited appearances are visible.
            using(var db=await DatabaseConnection.OpenAsync(CatalogRepository.Database)){
                var current=await WorldDrops.Query(db,null,$"SELECT * FROM item_modified_appearance WHERE ItemID={item} AND VerifiedBuild>=0 ORDER BY ID,VerifiedBuild DESC");
                var data=await WorldDrops.Query(db,null,$"SELECT RecordId,Status FROM hotfix_data WHERE TableHash={TooltipData.Hash("ItemModifiedAppearance")} AND RecordId IN({WorldDrops.Ids(current.Select(r=>int.Parse(r["ID"]!)).Concat(modified.Where(p=>TooltipData.Number(p.Value,"ItemID")==item).Select(p=>p.Key)))}) ORDER BY Id,UniqueId");
                var status=data.GroupBy(r=>int.Parse(r["RecordId"]!)).ToDictionary(g=>g.Key,g=>int.Parse(g.Last()["Status"]!));
                foreach(var id in modified.Where(p=>TooltipData.Number(p.Value,"ItemID")==item&&status.GetValueOrDefault(p.Key)==2).Select(p=>p.Key).ToArray())modified.Remove(id);
                foreach(var rows in current.GroupBy(r=>int.Parse(r["ID"]!)))if(status.GetValueOrDefault(rows.Key)==1)modified[rows.Key]=rows.First().ToDictionary(p=>p.Key,p=>p.Value??"",StringComparer.OrdinalIgnoreCase);
            }
            var mapping=modified.Values.Where(r=>TooltipData.Number(r,"ItemID")==item).OrderBy(r=>TooltipData.Number(r,"ItemAppearanceModifierID")).ThenBy(r=>TooltipData.Number(r,"OrderIndex")).FirstOrDefault()??throw new InvalidOperationException("This item has no active appearance mapping. No substitute model is shown.");
            int appearance=TooltipData.Number(mapping,"ItemAppearanceID");if(!appearances.TryGetValue(appearance,out var app))throw new InvalidOperationException($"Appearance {appearance} is unavailable.");int display=TooltipData.Number(app,"ItemDisplayInfoID");if(!displays.TryGetValue(display,out var row))throw new InvalidOperationException($"Display {display} is unavailable.");
            var parts=new List<ItemModelPart>();for(int n=1;n<=2;n++){int resource=TooltipData.Number(row,"ModelResourcesID"+n),material=TooltipData.Number(row,"ModelMaterialResourcesID"+n);if(resource==0)continue;int[] skins=textures.Values.Where(t=>TooltipData.Number(t,"MaterialResourcesID")==material&&material!=0).OrderBy(t=>TooltipData.Number(t,"UsageType")).Select(t=>TooltipData.Number(t,"FileDataID")).Where(id=>id>0).Distinct().ToArray();foreach(int mesh in models.Values.Where(m=>TooltipData.Number(m,"ModelResourcesID")==resource).Select(m=>TooltipData.Number(m,"FileDataID")).Where(id=>id>0).Distinct().Order())parts.Add(new(mesh,skins,n-1));}
            if(parts.Count==0&&standaloneRequired)throw new InvalidOperationException("This item has no standalone 3D mesh. Use On character to see its body textures/geosets.");
            var materialIds=new[]{TooltipData.Number(row,"ModelMaterialResourcesID1"),TooltipData.Number(row,"ModelMaterialResourcesID2")}.Where(id=>id>0).ToHashSet();return new(appearance,display,parts.DistinctBy(p=>p.Model).ToArray(),$"Appearance {appearance} · Display {display} · Client {Version}"){AdditionalTextures=textures.Values.Where(t=>materialIds.Contains(TooltipData.Number(t,"MaterialResourcesID"))).Select(t=>TooltipData.Number(t,"FileDataID")).Where(id=>id>0).Distinct().ToArray()};
        }finally{gate.Release();}
    }
    internal async Task<Dictionary<int,Dictionary<string,string>>> Table(string name)
    {
        await gate.WaitAsync();try{await Task.Run(()=>LoadClient(null));var existing=name switch{"ItemDisplayInfo"=>displays,"ModelFileData"=>models,"TextureFileData"=>textures,_=>null};if(existing is {Count:>0})return existing;if(extraTables.TryGetValue(name,out var cached))return cached;
            var reader=new DBCD.DBCD(new FilesystemDBCProvider(CatalogRepository.Db2Folder),new GithubDBDProvider(true));var rows=await Task.Run(()=>TooltipData.LoadTable(reader,name));string sql=System.Text.RegularExpressions.Regex.Replace(name,"(?<=[a-z0-9])([A-Z])","_$1").ToLowerInvariant();using var db=await DatabaseConnection.OpenAsync(CatalogRepository.Database);if(await HotfixHistory.HasTable(db,CatalogRepository.Database,sql))await TooltipData.Overlay(db,sql,name,rows);extraTables[name]=rows;return rows;
        }finally{gate.Release();}
    }
    private void LoadClient(string? folder)
    {
        if(folder is null&&ClientFolder.Length==0&&AppSettings.Current.ClientFolder.Length>0)folder=AppSettings.Current.ClientFolder;
        if(folder is null&&ClientFolder.Length==0){string preferences=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WoWItemEditor","model-viewer.json");if(File.Exists(preferences))try{folder=System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(preferences))?.GetValueOrDefault("ClientFolder");}catch(System.Text.Json.JsonException){}}folder=Path.GetFullPath(folder??(ClientFolder.Length>0?ClientFolder:DefaultClient));var buildFile=new FileInfo(Path.Combine(folder,".build.info"));string stamp=buildFile.Exists?$"{buildFile.Length}:{buildFile.LastWriteTimeUtc.Ticks}":"missing";if(ClientFolder==folder&&buildStamp==stamp)return;
        if(!File.Exists(Path.Combine(folder,".build.info")))throw new InvalidOperationException("Choose the WoW installation root containing .build.info and Data.");
        var lines=File.ReadAllLines(Path.Combine(folder,".build.info"));var names=lines[0].Split('|').Select(s=>s.Split('!')[0]).ToArray();string Field(string[] row,string name)=>row[Array.IndexOf(names,name)];var rows=lines.Skip(1).Where(s=>!string.IsNullOrWhiteSpace(s)).Select(s=>s.Split('|')).ToArray();var selected=rows.FirstOrDefault(r=>Field(r,"Product")=="wow_classic_beta"&&Field(r,"Active")=="1")??throw new InvalidOperationException("The selected client has no active Classic Beta build. Choose the client matching this server.");
        string version=Field(selected,"Version");if(!version.StartsWith("1.60.1.",StringComparison.Ordinal))throw new InvalidOperationException($"Client {version} does not match the server's 1.60.1 appearance layout.");
        Settings.LogLevel=TSLogLevel.Off;var next=new BuildInstance();next.Settings.BaseDir=folder;next.Settings.Product="wow_classic_beta";next.Settings.CacheDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WoWItemEditor","ModelCache");Directory.CreateDirectory(next.Settings.CacheDir);next.Settings.TryCDN=AppSettings.Current.AllowAssetDownloads;next.Settings.ListfileFallback=false;next.cdn.ProductDirectory=Field(selected,"CDN Path");next.LoadConfigs(Field(selected,"Build Key"),Field(selected,"CDN Key"));next.Load();build=next;ClientFolder=folder;Version=version;buildStamp=stamp;
    }
    internal async Task<byte[]> Read(int id,string? identity=null)
    {
        await gate.WaitAsync();try{if(identity is not null&&identity!=BuildIdentity)throw new InvalidOperationException("The client changed while loading. Reload this model.");return await Task.Run(()=>{byte[] bytes=build.OpenFileByFDID(checked((uint)id));if(bytes.Length>64*1024*1024)throw new InvalidDataException("Model asset exceeds the supported 64 MiB limit.");return bytes;});}finally{gate.Release();}
    }
}
