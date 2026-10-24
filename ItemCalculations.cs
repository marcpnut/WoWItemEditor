using System.Globalization;
using System.IO;
using MySqlConnector;
namespace AnoWowItemEditor;

public sealed record ItemValues(int? ItemLevel,int? Armor,Dictionary<int,int> Stats,float? MinDamage,float? MaxDamage,float? Dps,string Note);
public sealed class ItemCalculations
{
    private readonly Dictionary<string,Dictionary<int,Dictionary<string,string>>> tables=[];
    private readonly Dictionary<int,float> socketCost=[];
    // Formula reference: TrinityCore Item.cpp / ItemTemplate.cpp / ItemEnchantmentMgr.cpp.
    // Actual coefficients come exclusively from this server's DB2/GT and active hotfix rows.
    internal static async Task<ItemCalculations> LoadAsync(MySqlConnection db,DBCD.DBCD reader,bool includeOverrides=true,MySqlTransaction? tx=null)
    {
        var result=new ItemCalculations();
        string[] names=["RandPropPoints","ItemArmorTotal","ItemArmorQuality","ItemArmorShield","ArmorLocation","ItemDamageOneHand","ItemDamageOneHandCaster","ItemDamageTwoHand","ItemDamageTwoHandCaster","Curve","CurvePoint","ContentTuning"];
        foreach(string name in names) {
            var rows=await Task.Run(()=>TooltipData.LoadTable(reader,name));
            string sql=System.Text.RegularExpressions.Regex.Replace(name,"(?<=[a-z0-9])([A-Z])","_$1").ToLowerInvariant();
            if(includeOverrides)await TooltipData.Overlay(db,sql,name,rows,tx);result.tables[name]=rows;
        }
        var path=Path.Combine(Path.GetDirectoryName(CatalogRepository.Db2Folder)!,"..","gt","ItemSocketCostPerLevel.txt");
        foreach(string line in File.ReadLines(path).Skip(1)) {
            var fields=line.Split('\t');
            if(fields.Length>=2&&int.TryParse(fields[0],out int level)&&float.TryParse(fields[1],NumberStyles.Float,CultureInfo.InvariantCulture,out float cost))result.socketCost[level]=cost;
        }
        return result;
    }
    private Dictionary<string,string>? Row(string table,int id)=>tables.TryGetValue(table,out var rows)?rows.GetValueOrDefault(id):null;
    private static float F(Dictionary<string,string>? row,string key)=>row is not null&&float.TryParse(row.GetValueOrDefault(key),NumberStyles.Float,CultureInfo.InvariantCulture,out float value)?value:0;
    private static int I(Dictionary<string,string>? row,string key)=>(int)F(row,key);
    internal static int PropertyIndex(int slot,int subclass)=>slot switch {1 or 4 or 5 or 7 or 15 or 17 or 20 or 25=>1,26=>subclass==19?4:1,13 or 21 or 22=>4,3 or 6 or 8 or 10 or 12=>2,2 or 9 or 11 or 14 or 16 or 23=>3,28=>5,_=>0};
    internal static int Round(float value)=>(int)MathF.Floor(value+0.5f);
    internal static float? LinearCurve((float X,float Y)[] points,float input)
    {
        if(points.Length==0)return null;
        if(input<=points[0].X)return points[0].Y;
        for(int p=1;p<points.Length;p++)if(input<=points[p].X) {
            float width=points[p].X-points[p-1].X;
            return width<=0?points[p].Y:points[p-1].Y+(points[p].Y-points[p-1].Y)*(input-points[p-1].X)/width;
        }
        return points[^1].Y;
    }
    public ItemValues Evaluate(CatalogItem item,int level,OwnedItem? instance=null)
    {
        string? unsupported=null;
        if(instance is not null&&instance.BonusLists.Split(' ',StringSplitOptions.RemoveEmptyEntries).Any(s=>!int.TryParse(s,out int id)||id!=0))unsupported="Instance bonus lists require additional calculation; marked values are unresolved.";
        if(instance?.RandomProperty!=0&&instance is not null)unsupported="Random item properties require additional calculation; marked values are unresolved.";
        if(item.Number("ItemLevelOffsetCurveID")!=0||item.Number("ItemSquishEraID")!=0)unsupported="This item uses a scaling rule not yet supported.";
        int itemLevel=item.ItemLevel;
        int curve=item.Number("PlayerLevelToItemLevelCurveID");
        if(curve!=0&&unsupported is null) {
            int input=instance?.FixedLevel>0?instance.FixedLevel:level;
            var tuning=Row("ContentTuning",item.Number("ContentTuningID"));
            if(tuning is not null) {
                int min=I(tuning,"MinLevelSquish"),max=I(tuning,"MaxLevelSquish");
                if(I(tuning,"Flags")!=0||I(tuning,"MinLevelScalingOffset")!=0||I(tuning,"MaxLevelScalingOffset")!=0)unsupported="Conditional content tuning is not yet supported.";
                else if(max>=min&&max>0)input=Math.Clamp(input,min,max);
            }
            var definition=Row("Curve",curve);
            if(definition is null||I(definition,"Type")!=0)unsupported="This scaling curve cannot yet be evaluated.";
            else {
                var points=tables["CurvePoint"].Values.Where(p=>I(p,"CurveID")==curve).OrderBy(p=>I(p,"OrderIndex")).Select(p=>(X:F(p,"Pos1"),Y:F(p,"Pos2"))).ToArray();
                var value=LinearCurve(points,input);
                if(!value.HasValue)unsupported="Scaling curve points are unavailable.";
                else itemLevel=Round(value.Value);
            }
        }
        if(unsupported is not null)return new(null,null,[],null,null,null,unsupported);
        int quality=item.Quality==7?3:item.Quality;
        var stats=new Dictionary<int,int>();
        int index=PropertyIndex(item.Slot,item.Number("SubclassID"));
        var budget=Row("RandPropPoints",itemLevel);
        string? prefix=quality switch {2=>"GoodF",3=>"SuperiorF",4 or 5 or 6=>"EpicF",_=>null};
        if(index>0&&prefix is not null&&budget is not null) {
            float points=F(budget,prefix+index);
            for(int n=1;n<=10;n++) {
                if(item.Number("StatModifierBonusStat"+n)<0||item.Number("StatPercentEditor"+n)==0)continue;
                float.TryParse(item.Text("StatPercentageOfSocket"+n),NumberStyles.Float,CultureInfo.InvariantCulture,out float socket);
                if(socket!=0&&!socketCost.ContainsKey(itemLevel))continue;
                if(points>0)stats[n]=Round(item.Number("StatPercentEditor"+n)*points*0.0001f-socket*socketCost.GetValueOrDefault(itemLevel));
            }
        }
        int? armor=null;
        if(item.ClassId==4&&quality is >=0 and <=6) {
            int sub=item.Number("SubclassID");
            if(sub==6&&Row("ItemArmorShield",itemLevel) is {} shield)armor=Round(F(shield,"Quality"+(quality+1)));
            else if(sub is >=1 and <=4&&Row("ItemArmorTotal",itemLevel) is {} total&&Row("ItemArmorQuality",itemLevel) is {} aq&&Row("ArmorLocation",item.Slot==20?5:item.Slot) is {} loc) {
                string material=new[]{"","Cloth","Leather","Mail","Plate"}[sub];
                string modifier=new[]{"","Clothmodifier","Leathermodifier","Chainmodifier","Platemodifier"}[sub];
                armor=Round(F(total,material)*F(aq,"Qualitymod"+(quality+1))*F(loc,modifier));
            }
        }
        float? minDamage=null,maxDamage=null,dps=null;
        if(item.ClassId==2&&item.Number("ItemDelay")>0&&quality is >=0 and <=6) {
            bool caster=(item.Number("Flags2")&0x200)!=0;int sub=item.Number("SubclassID");
            string? damageTable=item.Slot switch {13 or 21 or 22=>caster?"ItemDamageOneHandCaster":"ItemDamageOneHand",17=>caster?"ItemDamageTwoHandCaster":"ItemDamageTwoHand",15 or 25 or 26 when sub==19=>"ItemDamageOneHandCaster",15 or 25 or 26 when sub is 2 or 3 or 18=>caster?"ItemDamageTwoHandCaster":"ItemDamageTwoHand",_=>null};
            if(damageTable is not null&&Row(damageTable,itemLevel) is {} row) {
                dps=F(row,"Quality"+(quality+1));float average=dps.Value*item.Number("ItemDelay")*0.001f;
                float.TryParse(item.Text("DmgVariance"),NumberStyles.Float,CultureInfo.InvariantCulture,out float variance);
                minDamage=Round(average*(1-variance*0.5f));maxDamage=Round(average*(1+variance*0.5f));
            }
        }
        var missing=ServerProblems(item).Where(p=>p.Contains("has no row")||p.Contains("table unavailable")||p.Contains("no verified damage-table mapping")).ToArray();string note=missing.Length>0?"MISSING SERVER DATA: "+string.Join(" ",missing):curve==0?"Fixed item level: changing character level does not scale this item’s base stats.":$"Item scaling calculated at level {(instance?.FixedLevel>0?instance.FixedLevel:level)}.";
        return new(itemLevel,armor,stats,minDamage,maxDamage,dps,note);
    }
    internal List<string> ServerProblems(CatalogItem item)
    {
        var problems=new List<string>();int level=item.ItemLevel,quality=item.Quality==7?3:item.Quality;
        void Require(string table){if(Row(table,level) is null){var ids=tables.GetValueOrDefault(table)?.Keys.Order().ToArray()??[];problems.Add($"{table} has no row for item level {level}"+(ids.Length>0?$" (installed/effective range {ids[0]}–{ids[^1]}; gaps are also blocked).":" (table unavailable)."));}}
        if(item.ClassId==2&&item.Slot>0){if(quality is <0 or >6)problems.Add("Unsupported weapon quality for the server damage tables.");bool caster=(item.Number("Flags2")&0x200)!=0;int sub=item.Number("SubclassID");string? damage=item.Slot switch{13 or 21 or 22=>caster?"ItemDamageOneHandCaster":"ItemDamageOneHand",17=>caster?"ItemDamageTwoHandCaster":"ItemDamageTwoHand",15 or 25 or 26 when sub==19=>"ItemDamageOneHandCaster",15 or 25 or 26 when sub is 2 or 3 or 18=>caster?"ItemDamageTwoHandCaster":"ItemDamageTwoHand",_=>null};if(damage is null)problems.Add("This weapon/slot combination has no verified damage-table mapping.");else Require(damage);if(item.Number("ItemDelay")<=0)problems.Add("Equipped weapons need a positive weapon speed.");}
        if(item.ClassId==4&&item.Number("SubclassID") is >=1 and <=6&&item.Slot>0){if(quality is <0 or >6)problems.Add("Unsupported armor quality for the server armor tables.");if(item.Number("SubclassID")==6)Require("ItemArmorShield");else if(item.Number("SubclassID")<=4){Require("ItemArmorTotal");Require("ItemArmorQuality");if(Row("ArmorLocation",item.Slot==20?5:item.Slot) is null)problems.Add("ArmorLocation has no row for the selected armor slot.");}}
        if(Enumerable.Range(1,10).Any(n=>item.Number("StatModifierBonusStat"+n)>=0&&item.Number("StatPercentEditor"+n)!=0))Require("RandPropPoints");
        if(new[]{"PlayerLevelToItemLevelCurveID","ItemLevelOffsetCurveID","ItemSquishEraID","ContentTuningID"}.Any(k=>item.Number(k)!=0))problems.Add("Level-scaling definitions require a verified server-level bounds check; saving this scaling layout is blocked.");
        return problems;
    }
    internal int AllocationFor(CatalogItem item,int index,int amount,int level)
    {
        if(amount is <0 or >1000000)throw new InvalidOperationException("Stat values must be between 0 and 1,000,000.");
        if(amount==0)return 0;
        var fields=new Dictionary<string,string>(item.Fields,StringComparer.OrdinalIgnoreCase);var candidate=new CatalogItem(fields);
        int? Value(int allocation){fields["StatPercentEditor"+index]=allocation.ToString(System.Globalization.CultureInfo.InvariantCulture);return Evaluate(candidate,level).Stats.GetValueOrDefault(index);}
        fields["StatPercentEditor"+index]="10000";
        if(!Evaluate(candidate,level).Stats.ContainsKey(index))throw new InvalidOperationException("This item's stat calculation is unsupported at the chosen preview level. Use a supported item/quality/slot/level or edit its stored allocation in Advanced fields.");
        int low=1,high=int.MaxValue;
        while(low<high){int mid=low+(int)(((long)high-low)/2);if(Value(mid)>=amount)high=mid;else low=mid+1;}
        if(Value(low)!=amount)throw new InvalidOperationException("That exact stat value cannot be represented by this item's allocation. Try a nearby value.");
        return low;
    }
}
