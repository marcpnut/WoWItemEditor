using DBCD.Providers;
using System.Globalization;
using MySqlConnector;
namespace WoWItemEditor;

internal sealed record ReferenceEntry(int SpellId,int? EffectId,string Name,string Kind,string Trigger,string Description,string Details)
{
    public string[] TriggerTypes {get;init;}=[];
    public IEnumerable<string> Triggers=>Kind=="Spell"&&TriggerTypes.Length>0?TriggerTypes:[Trigger];

    public int SchoolMask {get;init;}
    public int[] EffectTypes {get;init;}=[];
    public int[] AuraTypes {get;init;}=[];
    public bool UsedBySets {get;init;}
    public bool UsedByItems {get;init;}
    public bool HasProcRules {get;init;}
    public bool HasDescription {get;init;}
    public int[] SetIds {get;init;}=[];
    public string EffectLabel=>EffectId?.ToString()??"—";
    public bool Matches(string query)=>CatalogSearch.Reference(this,query);
}
internal sealed record ReferenceCatalog(List<ReferenceEntry> Spells,List<ReferenceEntry> Procs)
{
    internal TooltipData Data {get;init;}=new();
    internal Dictionary<int,Dictionary<string,string>> ItemEffects {get;init;}=[];
    internal Dictionary<int,string> ProcExplanations {get;init;}=[];
    internal string Explain(int spell,ContentRow? row=null,bool proc=false)=>SpellExplanation.Describe(Data,spell,row,row?.Bonus,proc)+(proc?"\n\n"+ProcExplanations.GetValueOrDefault(spell,"No proc chance/PPM data available. This does not imply a guaranteed proc."):"");
    internal static async Task<ReferenceCatalog> LoadAsync()
    {
        string[] names=["Spell","SpellName","SpellEffect","SpellMisc","SpellDuration","SpellAuraOptions","ItemEffect","ItemXItemEffect","ItemSetSpell","SpellProcsPerMinute"];
        string[] sql=["spell","spell_name","spell_effect","spell_misc","spell_duration","spell_aura_options","item_effect","item_x_item_effect","item_set_spell","spell_procs_per_minute"];
        var tables=await Task.Run(()=>{
            var reader=new DBCD.DBCD(new FilesystemDBCProvider(CatalogRepository.Db2Folder),new GithubDBDProvider(true));
            return names.ToDictionary(n=>n,n=>TooltipData.LoadTable(reader,n));
        });
        using var db=await DatabaseConnection.OpenAsync(CatalogRepository.Database);
        for(int n=0;n<names.Length;n++)if(await HotfixHistory.HasTable(db,CatalogRepository.Database,sql[n]))await TooltipData.Overlay(db,sql[n],names[n],tables[names[n]]);
        await SpellBlobs.OverlayAsync(db,tables["Spell"]);
        tables["ServerProcRules"]=[];
        if(await HotfixHistory.HasTable(db,$"{AppSettings.Current.WorldDatabase}","spell_proc")){
            using var cmd=new MySqlCommand($"SELECT * FROM {AppSettings.Current.WorldDatabase}.spell_proc",db);using var r=await cmd.ExecuteReaderAsync();int index=0;
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
            var entry=new ReferenceEntry(id,null,data.SpellName(id),"Spell","—",description,details){TriggerTypes=itemEffects[id].Select(r=>Trigger(N(r,"TriggerType"))).Concat(procRules[id].Any()||aura[id].Any(r=>TooltipData.Amount(r,"ProcTypeMask1")!=0||TooltipData.Amount(r,"ProcTypeMask2")!=0)?new[]{"Spell proc rules"}:Array.Empty<string>()).Distinct().ToArray(),SetIds=bonuses[id].Select(r=>N(r,"ItemSetID")).Distinct().ToArray(),SchoolMask=misc[id].Aggregate(0,(mask,r)=>mask|N(r,"SchoolMask")),EffectTypes=effects[id].Select(r=>N(r,"Effect")).Distinct().ToArray(),AuraTypes=effects[id].Select(r=>N(r,"EffectAura")).Where(n=>n>0).Distinct().ToArray(),UsedBySets=bonuses[id].Any(),UsedByItems=itemEffects[id].Any(),HasProcRules=procRules[id].Any(),HasDescription=data.Spells.TryGetValue(id,out var s)&&!string.IsNullOrWhiteSpace(TooltipData.Read(s,"Description"))};entry=entry with{Trigger=entry.TriggerTypes.Length>0?string.Join(", ",entry.TriggerTypes):"—"};spells.Add(entry);
            foreach(var effect in itemEffects[id])procs.Add(entry with{EffectId=N(effect,"ID"),Kind="Item effect",Trigger=Trigger(N(effect,"TriggerType")),Details=$"ITEM EFFECT {N(effect,"ID")}\n"+Section("Item effect fields",[effect])+details});
            if(!itemEffects[id].Any()&&(procRules[id].Any()||aura[id].Any(r=>TooltipData.Amount(r,"ProcTypeMask1")!=0||TooltipData.Amount(r,"ProcTypeMask2")!=0)))procs.Add(entry with{Kind="Proc spell",Trigger="Spell proc rules",Details="PROC SPELL · Proc rules are configured; no item effect is assigned.\n\n"+details});
        }
        var explanations=ids.ToDictionary(id=>id,id=>ProcExplanation(aura[id],procRules[id],tables["SpellProcsPerMinute"]));
        return new(spells.OrderBy(s=>s.Name).ThenBy(s=>s.SpellId).ToList(),procs.OrderBy(s=>s.Name).ThenBy(s=>s.SpellId).ThenBy(s=>s.EffectId).ToList()){Data=data,ItemEffects=tables["ItemEffect"],ProcExplanations=explanations};
    }
    internal static string ProcExplanation(IEnumerable<Dictionary<string,string>> options,IEnumerable<Dictionary<string,string>> server,Dictionary<int,Dictionary<string,string>> ppm)
    {
        string Chance(string value)=>decimal.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out decimal chance)?chance is >=0 and <=100?value+"%":value+" (outside 0–100; server-defined, not a literal probability)":"Unavailable";var lines=new List<string>();foreach(var row in options){int rate=N(row,"SpellProcsPerMinuteID");string rateText=rate==0?"No PPM record":ppm.TryGetValue(rate,out var p)?$"{TooltipData.Read(p,"BaseProcRate")} procs/minute base rate (context modifiers may apply)":$"PPM record {rate} unavailable";lines.Add($"Spell proc defaults · difficulty {N(row,"DifficultyID")}: stored chance {Chance(TooltipData.Read(row,"ProcChance"))} · {rateText}\nSpell recovery: {SpellExplanation.Time(N(row,"ProcRecovery"))} · Proc charges: {N(row,"ProcCharges")}. These are spell defaults, not the final effective chance.");}
        foreach(var row in server)lines.Add($"Server proc rule · spell {TooltipData.Read(row,"SpellId")}: stored chance {Chance(TooltipData.Read(row,"Chance"))} · PPM {TooltipData.Read(row,"ProcsPerMinute")} · cooldown {TooltipData.Read(row,"Cooldown")} (raw server units; custom semantics unverified). Flags, conditions and fallback rules determine which defaults apply.");
        if(lines.Count==0)lines.Add("No proc chance/PPM records found. This does not mean 100% chance.");return string.Join("\n\n",lines);
    }
    private static int N(Dictionary<string,string> row,string key)=>TooltipData.Number(row,key);
    private static string Section(string title,IEnumerable<Dictionary<string,string>> rows)=>title+"\n"+(rows.Any()?string.Join("\n\n",rows.Select(r=>string.Join("\n",r.OrderBy(p=>p.Key).Select(p=>$"{p.Key}: {p.Value}")))):"No records")+"\n\n";
    internal static string Trigger(int trigger)=>trigger switch{0=>"Use",1=>"Equip",2=>"Chance on hit",5=>"Use (no delay)",6=>"Learn spell",_=>$"Trigger {trigger}"};
}
