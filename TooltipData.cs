using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DBCD.Providers;
using MySqlConnector;
namespace AnoWowItemEditor;

public sealed record SetBonus(int Pieces, int SpellId, int Specialization);
public sealed record ItemSetInfo(int Id, string Name, int[] Members, SetBonus[] Bonuses,int Flags=0,int RequiredSkill=0,int RequiredSkillRank=0);
public sealed record ItemSpellInfo(int SpellId, int Trigger, int Cooldown);

// A read-only snapshot of the same installed build and active hotfixes as the item catalog.
public sealed class TooltipData
{
    public Dictionary<int,ItemSetInfo> Sets { get; } = [];
    public Dictionary<int,List<ItemSpellInfo>> ItemSpells { get; } = [];
    public Dictionary<(int Class,int Subclass),string> Subclasses { get; } = [];
    public Dictionary<int,string> Classes { get; } = [];
    public Dictionary<int,string> Skills { get; } = [];
    public Dictionary<int,string> Factions { get; } = [];
    public Dictionary<int,string> Enchantments { get; } = [];
    public ItemCalculations Calculations { get; internal set; } = new();
    internal Dictionary<int,Dictionary<string,string>> Spells { get; } = [];
    internal Dictionary<(int Spell,int Index),Dictionary<string,string>> Effects { get; } = [];
    internal Dictionary<int,Dictionary<string,string>> Durations { get; } = [];
    internal Dictionary<int,Dictionary<string,string>> Misc { get; } = [];
    public string SpellName(int id) => Spells.TryGetValue(id,out var spell)&&!string.IsNullOrWhiteSpace(Read(spell,"Name")) ? Read(spell,"Name") : $"Spell {id}";
    internal static string Read(Dictionary<string,string> row,string key) => row.GetValueOrDefault(key)??"";
    internal static int Number(Dictionary<string,string> row,string key) => int.TryParse(Read(row,key),out int n)?n:0;
    internal static decimal Amount(Dictionary<string,string> row,string key) => decimal.TryParse(Read(row,key),NumberStyles.Float,CultureInfo.InvariantCulture,out var n)?n:0;
    internal static Dictionary<int,Dictionary<string,string>> LoadTable(DBCD.DBCD reader,string table,Func<Dictionary<string,string>,bool>? filter=null)
    {
        var data=reader.Load(table,"1.60.1.70450");
        var rows=new Dictionary<int,Dictionary<string,string>>();
        foreach(var row in data.Values)
        {
            var fields=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(string column in data.AvailableColumns)
            {
                object? value=row[column];
                string target=column.EndsWith("_lang",StringComparison.Ordinal)?column[..^5]:column;
                if(value is Array array)
                    for(int i=0;i<array.Length;i++) fields[target+(i+1)]=Convert.ToString(array.GetValue(i),CultureInfo.InvariantCulture)??"";
                else fields[target]=Convert.ToString(value,CultureInfo.InvariantCulture)??"";
            }
            int id=Convert.ToInt32(row.ID); fields["ID"]=id.ToString(CultureInfo.InvariantCulture);
            if(filter is null || filter(fields)) rows[id]=fields;
        }
        return rows;
    }
    internal static uint Hash(string table)
    {
        using var file=File.OpenRead(Path.Combine(CatalogRepository.Db2Folder,table+".db2"));
        var header=new byte[160]; file.ReadExactly(header);
        if(Encoding.ASCII.GetString(header,0,4)!="WDC5") throw new InvalidOperationException($"Unsupported DB2 header: {table}");
        return BitConverter.ToUInt32(header,152);
    }
    internal static async Task Overlay(MySqlConnection db,string table,string db2,Dictionary<int,Dictionary<string,string>> rows)
    {
        // Only these internally named tables are passed here; no user text enters identifiers.
        using var cmd=new MySqlCommand($"SELECT s.* FROM `{table}` s WHERE s.VerifiedBuild IN (70450,0) AND EXISTS (SELECT 1 FROM hotfix_data h WHERE h.TableHash=@hash AND h.RecordId=s.ID AND h.VerifiedBuild=s.VerifiedBuild AND h.Status=1) ORDER BY s.ID,CASE WHEN s.VerifiedBuild=70450 THEN 0 ELSE 1 END",db);
        cmd.Parameters.AddWithValue("@hash",Hash(db2));
        var applied=new HashSet<int>();
        using(var reader=await cmd.ExecuteReaderAsync())
        while(await reader.ReadAsync())
        {
            int id=Convert.ToInt32(reader["ID"]); if(!applied.Add(id))continue;
            var fields=rows.TryGetValue(id,out var old)?new Dictionary<string,string>(old,StringComparer.OrdinalIgnoreCase):new(StringComparer.OrdinalIgnoreCase);
            for(int i=0;i<reader.FieldCount;i++) fields[reader.GetName(i)]=reader.IsDBNull(i)?"":Convert.ToString(reader.GetValue(i),CultureInfo.InvariantCulture)??"";
            // This installed DB2 exposes float base points as EffectBasePointsF; SQL calls them EffectBasePoints.
            if(table=="spell_effect") fields["EffectBasePointsF"]=fields["EffectBasePoints"];
            rows[id]=fields;
        }
        using var deleted=new MySqlCommand("SELECT RecordId FROM hotfix_data WHERE TableHash=@hash AND Status=2 AND VerifiedBuild IN (0,70450)",db);
        deleted.Parameters.AddWithValue("@hash",Hash(db2));
        using var removed=await deleted.ExecuteReaderAsync();
        while(await removed.ReadAsync()) if(!applied.Contains(Convert.ToInt32(removed[0])))rows.Remove(Convert.ToInt32(removed[0]));
    }
    public static async Task<TooltipData> LoadAsync(MySqlConnection db,List<CatalogItem> catalog)
    {
        var result=new TooltipData();
        var reader=new DBCD.DBCD(new FilesystemDBCProvider(CatalogRepository.Db2Folder),new GithubDBDProvider());
        var baseline=await Task.Run(()=>new {
            Sets=LoadTable(reader,"ItemSet"),Bonuses=LoadTable(reader,"ItemSetSpell"),
            Links=LoadTable(reader,"ItemXItemEffect"),ItemEffects=LoadTable(reader,"ItemEffect"),
            Subclasses=LoadTable(reader,"ItemSubClass"),Classes=LoadTable(reader,"ChrClasses"),
            Skills=LoadTable(reader,"SkillLine"),Factions=LoadTable(reader,"Faction"),Enchantments=LoadTable(reader,"SpellItemEnchantment")
        });
        await Overlay(db,"item_set","ItemSet",baseline.Sets);
        await Overlay(db,"item_set_spell","ItemSetSpell",baseline.Bonuses);
        await Overlay(db,"item_effect","ItemEffect",baseline.ItemEffects);
        await Overlay(db,"item_x_item_effect","ItemXItemEffect",baseline.Links);
        await Overlay(db,"spell_item_enchantment","SpellItemEnchantment",baseline.Enchantments);
        foreach(var row in baseline.Subclasses.Values) result.Subclasses[(Number(row,"ClassID"),Number(row,"SubClassID"))]=Read(row,"DisplayName");
        foreach(var row in baseline.Classes.Values) result.Classes[Number(row,"ID")]=Read(row,"Name");
        foreach(var row in baseline.Skills.Values) result.Skills[Number(row,"ID")]=Read(row,"DisplayName");
        foreach(var row in baseline.Factions.Values) result.Factions[Number(row,"ID")]=Read(row,"Name");
        foreach(var row in baseline.Enchantments.Values) result.Enchantments[Number(row,"ID")]=Read(row,"Name");
        var catalogById=catalog.ToDictionary(i=>i.Id);
        foreach(var row in baseline.Sets.Values)
        {
            int id=Number(row,"ID");
            var members=Enumerable.Range(1,17).Select(n=>Number(row,"ItemID"+n)).Where(n=>n>0).Distinct().ToList();
            members.RemoveAll(n=>catalogById.TryGetValue(n,out var item)&&item.Number("ItemSet")!=id);
            members.AddRange(catalog.Where(i=>i.Number("ItemSet")==id&&!members.Contains(i.Id)).Select(i=>i.Id));
            var bonuses=baseline.Bonuses.Values.Where(b=>Number(b,"ItemSetID")==id).Select(b=>new SetBonus(Number(b,"Threshold"),Number(b,"SpellID"),Number(b,"ChrSpecID"))).OrderBy(b=>b.Pieces).ThenBy(b=>b.Specialization).ToArray();
            result.Sets[id]=new(id,Read(row,"Name"),members.ToArray(),bonuses,Number(row,"SetFlags"),Number(row,"RequiredSkill"),Number(row,"RequiredSkillRank"));
        }
        foreach(var row in baseline.Links.Values)
        {
            int item=Number(row,"ItemID");
            if(!baseline.ItemEffects.TryGetValue(Number(row,"ItemEffectID"),out var effect)) continue;
            if(!result.ItemSpells.TryGetValue(item,out var list)) result.ItemSpells[item]=list=[];
            list.Add(new(Number(effect,"SpellID"),Number(effect,"TriggerType"),Number(effect,"CoolDownMSec")));
        }
        var wanted=baseline.ItemEffects.Values.Select(e=>Number(e,"SpellID")).Concat(baseline.Bonuses.Values.Select(b=>Number(b,"SpellID"))).ToHashSet();
        await Task.Run(()=> {
            var spells=LoadTable(reader,"Spell");
            // Referenced spell values such as Dreadnaught's $28846s1 are in another spell's effects.
            foreach(int id in wanted.ToArray()) if(spells.TryGetValue(id,out var spell))
                foreach(Match match in Regex.Matches(Read(spell,"Description"),@"\$(\d+)[a-zA-Z]"))wanted.Add(int.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture));
            foreach(int id in wanted) if(spells.TryGetValue(id,out var spell)) result.Spells[id]=spell;
        });
        var names=await Task.Run(()=>LoadTable(reader,"SpellName",r=>wanted.Contains(Number(r,"ID"))));
        await Overlay(db,"spell_name","SpellName",names);
        foreach(var (id,name) in names) { if(!result.Spells.TryGetValue(id,out var spell)) result.Spells[id]=spell=[]; spell["Name"]=Read(name,"Name"); }
        var effects=await Task.Run(()=>LoadTable(reader,"SpellEffect",r=>wanted.Contains(Number(r,"SpellID"))));
        await Overlay(db,"spell_effect","SpellEffect",effects);
        foreach(var row in effects.Values.Where(r=>Number(r,"DifficultyID")==0)) result.Effects[(Number(row,"SpellID"),Number(row,"EffectIndex"))]=row;
        var durations=await Task.Run(()=>LoadTable(reader,"SpellDuration"));
        var misc=await Task.Run(()=>LoadTable(reader,"SpellMisc",r=>wanted.Contains(Number(r,"SpellID"))&&Number(r,"DifficultyID")==0));
        await Overlay(db,"spell_duration","SpellDuration",durations);
        await Overlay(db,"spell_misc","SpellMisc",misc);
        foreach(var (id,row) in durations)result.Durations[id]=row;
        foreach(var row in misc.Values.Where(r=>Number(r,"DifficultyID")==0))result.Misc[Number(row,"SpellID")]=row;
        result.Calculations=await ItemCalculations.LoadAsync(db,reader);
        return result;
    }
    public string SpellDescription(int id)
    {
        if(!Spells.TryGetValue(id,out var spell)||string.IsNullOrWhiteSpace(Read(spell,"Description"))) return SpellName(id)+" [description unavailable]";
        return SpellText.Resolve(Read(spell,"Description"),id,this);
    }
}

internal static class SpellText
{
    internal static string Resolve(string raw,int spell,TooltipData data)
    {
        string text=Regex.Replace(raw,@"\$(?<spell>\d+)?(?<kind>[sSmMtTaA])(?<index>\d+)",m=> {
            int id=m.Groups["spell"].Success?int.Parse(m.Groups["spell"].Value,CultureInfo.InvariantCulture):spell;
            int index=int.Parse(m.Groups["index"].Value,CultureInfo.InvariantCulture)-1;
            if(!data.Effects.TryGetValue((id,index),out var effect))return "[value unavailable]";
            string kind=m.Groups["kind"].Value.ToLowerInvariant();
            if(kind=="t") return (TooltipData.Amount(effect,"EffectAuraPeriod")/1000m).ToString("0.###",CultureInfo.InvariantCulture);
            if(kind=="a") return "[radius]";
            string[] scaling=["ScalingClass","Coefficient","Variance","EffectRealPointsPerLevel","EffectBonusCoefficient","BonusCoefficientFromAP","EffectPointsPerResource","ResourceCoefficient"];
            if(scaling.Any(k=>TooltipData.Amount(effect,k)!=0))return "[scales]";
            if(!effect.ContainsKey("EffectBasePointsF"))return "[value unavailable]";
            return Math.Abs(TooltipData.Amount(effect,"EffectBasePointsF")).ToString("0.###",CultureInfo.InvariantCulture);
        });
        text=Regex.Replace(text,@"\$(?<spell>\d+)?[dD]\b",m=> {
            int id=m.Groups["spell"].Success?int.Parse(m.Groups["spell"].Value,CultureInfo.InvariantCulture):spell;
            if(!data.Misc.TryGetValue(id,out var misc)||!data.Durations.TryGetValue(TooltipData.Number(misc,"DurationIndex"),out var duration))return "[duration]";
            decimal ms=TooltipData.Amount(duration,"Duration");
            if(TooltipData.Amount(duration,"DurationPerResource")!=0||TooltipData.Amount(duration,"MaxDuration")>ms)return "[scales]";
            if(ms<=0)return "[duration]";
            return ms%60000==0?$"{ms/60000:0.###} min":$"{ms/1000:0.###} sec";
        });
        text=Regex.Replace(text,@"\|c[0-9a-fA-F]{8}|\|r","");
        text=Regex.Replace(text,@"\|T.*?\|t","");
        // Unsupported formulas depend on game context; do not leave raw client script in the tooltip.
        text=Regex.Replace(text,@"\$\{[^}]*\}","[scales]");
        text=Regex.Replace(text,@"\$\d*[a-zA-Z]\d*", "[context value]");
        return text.Replace("\\n","\n").Replace("|n","\n").Trim();
    }
}
