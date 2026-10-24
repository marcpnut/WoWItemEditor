using DBCD.Providers;
using System.Globalization;
using MySqlConnector;
namespace AnoWowItemEditor;

internal sealed record ReferenceEntry(int SpellId,int? EffectId,string Name,string Kind,string Trigger,string Description,string Details)
{
    public int SchoolMask {get;init;}
    public int[] EffectTypes {get;init;}=[];
    public int[] AuraTypes {get;init;}=[];
    public bool UsedBySets {get;init;}
    public bool UsedByItems {get;init;}
    public bool HasProcRules {get;init;}
    public bool HasDescription {get;init;}
    public string EffectLabel=>EffectId?.ToString()??"—";
    public bool Matches(string query)=>query.Length==0||SpellId.ToString()==query||EffectId?.ToString()==query||Name.Contains(query,StringComparison.OrdinalIgnoreCase)||Description.Contains(query,StringComparison.OrdinalIgnoreCase)||Trigger.Contains(query,StringComparison.OrdinalIgnoreCase)||Kind.Contains(query,StringComparison.OrdinalIgnoreCase);
}
internal sealed record ReferenceCatalog(List<ReferenceEntry> Spells,List<ReferenceEntry> Procs)
{
    internal TooltipData Data {get;init;}=new();
    internal Dictionary<int,Dictionary<string,string>> ItemEffects {get;init;}=[];
    internal static async Task<ReferenceCatalog> LoadAsync()
    {
        string[] names=["Spell","SpellName","SpellEffect","SpellMisc","SpellDuration","SpellAuraOptions","ItemEffect","ItemXItemEffect","ItemSetSpell"];
        string[] sql=["spell","spell_name","spell_effect","spell_misc","spell_duration","spell_aura_options","item_effect","item_x_item_effect","item_set_spell"];
        var tables=await Task.Run(()=>{
            var reader=new DBCD.DBCD(new FilesystemDBCProvider(CatalogRepository.Db2Folder),new GithubDBDProvider());
            return names.ToDictionary(n=>n,n=>TooltipData.LoadTable(reader,n));
        });
        using var db=await DatabaseConnection.OpenAsync(CatalogRepository.Database);
        for(int n=0;n<names.Length;n++)if(await HotfixHistory.HasTable(db,CatalogRepository.Database,sql[n]))await TooltipData.Overlay(db,sql[n],names[n],tables[names[n]]);
        tables["ServerProcRules"]=[];
        if(await HotfixHistory.HasTable(db,"foreverclassic_world","spell_proc")){
            using var cmd=new MySqlCommand("SELECT * FROM foreverclassic_world.spell_proc",db);using var r=await cmd.ExecuteReaderAsync();int index=0;
            while(await r.ReadAsync()){var row=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);for(int n=0;n<r.FieldCount;n++)row[r.GetName(n)]=r.IsDBNull(n)?"":Convert.ToString(r.GetValue(n),CultureInfo.InvariantCulture)??"";tables["ServerProcRules"][index++]=row;}
        }
        return await Task.Run(()=>Build(tables));
    }
    private static ReferenceCatalog Build(Dictionary<string,Dictionary<int,Dictionary<string,string>>> tables)
    {
        var data=new TooltipData();
        foreach(var (id,row) in tables["Spell"])data.Spells[id]=new(row,StringComparer.OrdinalIgnoreCase);
        foreach(var (id,row) in tables["SpellName"]){if(!data.Spells.TryGetValue(id,out var spell))data.Spells[id]=spell=new(StringComparer.OrdinalIgnoreCase);spell["Name"]=TooltipData.Read(row,"Name");}
        foreach(var row in tables["SpellEffect"].Values.Where(r=>N(r,"DifficultyID")==0))data.Effects[(N(row,"SpellID"),N(row,"EffectIndex"))]=row;
        foreach(var row in tables["SpellMisc"].Values.Where(r=>N(r,"DifficultyID")==0))data.Misc[N(row,"SpellID")]=row;
        foreach(var (id,row) in tables["SpellDuration"])data.Durations[id]=row;
        var effects=tables["SpellEffect"].Values.ToLookup(r=>N(r,"SpellID"));
        var aura=tables["SpellAuraOptions"].Values.ToLookup(r=>N(r,"SpellID"));
        var misc=tables["SpellMisc"].Values.ToLookup(r=>N(r,"SpellID"));
        var itemEffects=tables["ItemEffect"].Values.ToLookup(r=>N(r,"SpellID"));
        var links=tables["ItemXItemEffect"].Values.ToLookup(r=>N(r,"ItemEffectID"));
        var bonuses=tables["ItemSetSpell"].Values.ToLookup(r=>N(r,"SpellID"));
        var procRules=tables["ServerProcRules"].Values.ToLookup(r=>Math.Abs(N(r,"SpellId")));
        var ids=data.Spells.Keys.Concat(effects.Select(g=>g.Key)).Concat(itemEffects.Select(g=>g.Key)).Concat(bonuses.Select(g=>g.Key)).Concat(procRules.Select(g=>g.Key)).Where(id=>id>0).Distinct().Order().ToArray();
        var spells=new List<ReferenceEntry>();var procs=new List<ReferenceEntry>();
        foreach(int id in ids){
            string description=data.SpellDescription(id);
            string details=$"Spell ID: {id}\n\n{description}\n\n";
            details+="Existing set bonuses\n"+(bonuses[id].Any()?string.Join("\n",bonuses[id].Select(r=>$"Set {N(r,"ItemSetID")} · {N(r,"Threshold")} pieces · specialization {N(r,"ChrSpecID")}")):"None recorded")+"\n\n";
            details+="Existing item effects\n"+(itemEffects[id].Any()?string.Join("\n",itemEffects[id].Select(r=>$"Effect {N(r,"ID")} · {Trigger(N(r,"TriggerType"))} · items: "+string.Join(", ",links[N(r,"ID")].Select(l=>N(l,"ItemID"))))):"None configured")+"\n\n";
            details+=Section("Server proc rules (negative SpellId applies to all ranks)",procRules[id])+Section("Spell fields",data.Spells.TryGetValue(id,out var fields)?[fields]:[])+Section("Spell effects (all difficulties)",effects[id])+Section("Proc/aura options",aura[id])+Section("Spell metadata",misc[id]);
            var entry=new ReferenceEntry(id,null,data.SpellName(id),"Spell","—",description,details){SchoolMask=misc[id].Aggregate(0,(mask,r)=>mask|N(r,"SchoolMask")),EffectTypes=effects[id].Select(r=>N(r,"Effect")).Distinct().ToArray(),AuraTypes=effects[id].Select(r=>N(r,"EffectAura")).Where(n=>n>0).Distinct().ToArray(),UsedBySets=bonuses[id].Any(),UsedByItems=itemEffects[id].Any(),HasProcRules=procRules[id].Any(),HasDescription=data.Spells.TryGetValue(id,out var s)&&!string.IsNullOrWhiteSpace(TooltipData.Read(s,"Description"))};spells.Add(entry);
            foreach(var effect in itemEffects[id])procs.Add(entry with{EffectId=N(effect,"ID"),Kind="Item effect",Trigger=Trigger(N(effect,"TriggerType")),Details=$"ITEM EFFECT {N(effect,"ID")}\n"+Section("Item effect fields",[effect])+details});
            if(!itemEffects[id].Any())procs.Add(entry with{Kind="Spell candidate",Trigger="Not configured",Details="SPELL CANDIDATE · No item effect configured. Suitability depends on the spell and server rules.\n\n"+details});
        }
        return new(spells.OrderBy(s=>s.Name).ThenBy(s=>s.SpellId).ToList(),procs.OrderBy(s=>s.Name).ThenBy(s=>s.SpellId).ThenBy(s=>s.EffectId).ToList()){Data=data,ItemEffects=tables["ItemEffect"]};
    }
    private static int N(Dictionary<string,string> row,string key)=>TooltipData.Number(row,key);
    private static string Section(string title,IEnumerable<Dictionary<string,string>> rows)=>title+"\n"+(rows.Any()?string.Join("\n\n",rows.Select(r=>string.Join("\n",r.OrderBy(p=>p.Key).Select(p=>$"{p.Key}: {p.Value}")))):"No records")+"\n\n";
    internal static string Trigger(int trigger)=>trigger switch{0=>"Use",1=>"Equip",2=>"Chance on hit",5=>"Use (no delay)",6=>"Learn spell",_=>$"Trigger {trigger}"};
}
