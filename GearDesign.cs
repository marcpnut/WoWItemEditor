using System.Globalization;
namespace AnoWowItemEditor;

internal sealed record DesignMetric(string Name,string Before,string After,string Change);
internal sealed record EquipPreview(CharacterSnapshot Before,CharacterSnapshot After,OwnedItem[] Removed,List<string> Notes,bool Allowed);
internal sealed record BudgetLine(string Stat,string Amount,int Allocation,string Share);
internal sealed record BudgetReport(List<BudgetLine> Lines,long Allocation,List<CatalogItem> Peers,string Assessment,string Notes);
internal static class GearDesign
{
    internal static readonly Dictionary<int,string> EquipmentSlots=new(){[0]="Head",[1]="Neck",[2]="Shoulder",[3]="Shirt",[4]="Chest",[5]="Waist",[6]="Legs",[7]="Feet",[8]="Wrists",[9]="Hands",[10]="Finger 1",[11]="Finger 2",[12]="Trinket 1",[13]="Trinket 2",[14]="Back",[15]="Main hand",[16]="Off hand",[17]="Ranged / relic",[18]="Tabard"};
    internal static int[] Targets(CatalogItem item)=>item.Slot switch{1=>[0],2=>[1],3=>[2],4=>[3],5 or 20=>[4],6=>[5],7=>[6],8=>[7],9=>[8],10=>[9],11=>[10,11],12=>[12,13],13=>[15,16],14 or 22 or 23=>[16],15 or 25 or 26 or 28=>[17],16=>[14],17 or 21=>[15],19=>[18],_=>[]};
    internal static EquipPreview Replace(CharacterSnapshot character,CatalogItem candidate,int target,IReadOnlyDictionary<int,CatalogItem> catalog,int level)
    {
        var notes=new List<string>();bool allowed=Targets(candidate).Contains(target);
        if(!allowed)notes.Add("This item does not fit the selected equipment slot.");
        var equipped=character.Items.Where(i=>i.Equipped).ToList();var remove=equipped.Where(i=>i.Slot==target).ToList();
        if(target==15&&candidate.Slot==17)remove.AddRange(equipped.Where(i=>i.Slot==16));
        if(target==16){var main=equipped.FirstOrDefault(i=>i.Slot==15);if(main is not null&&catalog.GetValueOrDefault(main.Entry)?.Slot==17){remove.Add(main);notes.Add("The two-handed main-hand weapon is removed to free the off-hand slot.");}}
        remove=remove.DistinctBy(i=>i.Guid).ToList();
        int mask=candidate.Number("AllowableClass");if(mask>0&&(mask&(1<<(character.Character.ClassId-1)))==0){notes.Add("The character's class is excluded by this item.");allowed=false;}
        if(candidate.RequiredLevel>level){notes.Add($"Requires level {candidate.RequiredLevel}; preview level is {level}.");allowed=false;}
        int skill=candidate.Number("RequiredSkill");if(skill>0&&(character.Skills?.GetValueOrDefault(skill)??0)<candidate.Number("RequiredSkillRank")){notes.Add("The character does not meet the saved required skill rank.");allowed=false;}
        if(candidate.Number("MaxCount")==1&&equipped.Except(remove).Any(i=>i.Entry==candidate.Id)){notes.Add("A unique copy of this item remains in another slot.");allowed=false;}
        if(target==16&&candidate.ClassId==2)notes.Add("Off-hand weapon proficiency and dual-wield talents require an in-game check.");
        if(candidate.Number("ItemLimitCategory")!=0)notes.Add("This item's category equip restrictions require an in-game check.");
        notes.Add("Preview uses base candidate stats. Enchants, gems, talents, proc power, set-bonus stat effects and combat ratings are not added to totals. Class proficiencies and custom equip rules are not simulated.");
        var replacement=new OwnedItem(ulong.MaxValue,candidate.Id,1,0,target,"Comparison preview",0,"","",0,0,true);
        return new(character,character with{Items=character.Items.Where(i=>!remove.Contains(i)).Append(replacement).ToList()},remove.ToArray(),notes,allowed);
    }
    private sealed class Totals {internal Dictionary<string,double> Values=[];internal HashSet<string> Unknown=[];}
    private static Totals Sum(IEnumerable<OwnedItem> equipment,IReadOnlyDictionary<int,CatalogItem> catalog,TooltipData data,int level)
    {
        var total=new Totals();foreach(var owned in equipment.Where(i=>i.Equipped)){
            if(!catalog.TryGetValue(owned.Entry,out var item)){total.Unknown.Add("Unknown item definition");continue;}
            var value=data.Calculations.Evaluate(item,level,owned.Guid==ulong.MaxValue?null:owned);
            void Add(string key,double? amount){if(amount.HasValue)total.Values[key]=total.Values.GetValueOrDefault(key)+amount.Value;else total.Unknown.Add(key);}
            if(item.ClassId==4&&item.Number("SubclassID") is >=1 and <=6&&item.Slot>0)Add("Armor",value.Armor);
            for(int n=1;n<=10;n++)if(item.Number("StatModifierBonusStat"+n)>=0&&item.Number("StatPercentEditor"+n)!=0){string key=ItemTooltip.StatNames.GetValueOrDefault(item.Number("StatModifierBonusStat"+n))??$"Stat {item.Number("StatModifierBonusStat"+n)}";Add(key,value.Stats.TryGetValue(n,out int amount)?amount:null);}
            if(item.ClassId==2&&owned.Slot is 15 or 16 or 17){string name=EquipmentSlots[owned.Slot];Add(name+" weapon DPS",value.Dps);Add(name+" minimum damage",value.MinDamage);Add(name+" maximum damage",value.MaxDamage);}
        }return total;
    }
    internal static List<DesignMetric> Compare(CharacterSnapshot before,CharacterSnapshot after,IReadOnlyDictionary<int,CatalogItem> catalog,TooltipData data,int level)
    {
        var a=Sum(before.Items,catalog,data,level);var b=Sum(after.Items,catalog,data,level);if(a.Unknown.Contains("Unknown item definition")||b.Unknown.Contains("Unknown item definition")){a.Unknown.UnionWith(a.Values.Keys.Concat(b.Values.Keys));b.Unknown.UnionWith(a.Values.Keys.Concat(b.Values.Keys));}return a.Values.Keys.Concat(b.Values.Keys).Concat(a.Unknown).Concat(b.Unknown).Distinct().OrderBy(k=>k).Select(k=>new DesignMetric(k,a.Unknown.Contains(k)?"[unresolved]":a.Values.GetValueOrDefault(k).ToString("0.##",CultureInfo.InvariantCulture),b.Unknown.Contains(k)?"[unresolved]":b.Values.GetValueOrDefault(k).ToString("0.##",CultureInfo.InvariantCulture),a.Unknown.Contains(k)||b.Unknown.Contains(k)?"[unresolved]":(b.Values.GetValueOrDefault(k)-a.Values.GetValueOrDefault(k)).ToString("+0.##;-0.##;0",CultureInfo.InvariantCulture))).ToList();
    }
    internal static List<DesignMetric> PieceTotals(IEnumerable<CatalogItem> pieces,TooltipData data,int level)
    {
        var list=pieces.ToList();var instances=list.Select((p,n)=>new OwnedItem(ulong.MaxValue,p.Id,1,0,-1,"Set preview",0,"","",0,0,true)).ToList();var total=Sum(instances,list.ToDictionary(p=>p.Id),data,level);return total.Values.Keys.Concat(total.Unknown).Distinct().Order().Select(k=>new DesignMetric(k,"",total.Unknown.Contains(k)?"[unresolved]":total.Values[k].ToString("0.##",CultureInfo.InvariantCulture),"")).ToList();
    }
    internal static List<string> SetChanges(CharacterSnapshot before,CharacterSnapshot after,IReadOnlyDictionary<int,CatalogItem> catalog,TooltipData data)
    {
        int Count(CharacterSnapshot c,int set)=>c.Items.Where(i=>i.Equipped&&catalog.GetValueOrDefault(i.Entry)?.Number("ItemSet")==set).Select(i=>i.Entry).Distinct().Count();
        bool Active(CharacterSnapshot c,ItemSetInfo set,SetBonus b)=>Count(c,set.Id)>=b.Pieces&&set.Flags==0&&b.Trait==0&&(b.Specialization==0||b.Specialization==c.Character.Specialization)&&(set.RequiredSkill==0||(c.Skills?.GetValueOrDefault(set.RequiredSkill)??0)>=set.RequiredSkillRank);
        var lines=new List<string>();foreach(var set in data.Sets.Values){int a=Count(before,set.Id),b=Count(after,set.Id);if(a==b)continue;if(set.Flags!=0||set.Bonuses.Any(x=>x.Trait!=0))lines.Add($"{set.Name}: conditional set flags / trait restrictions require in-game verification.");lines.Add($"{set.Name}: {a} → {b} pieces");foreach(var bonus in set.Bonuses)if(Active(before,set,bonus)!=Active(after,set,bonus))lines.Add($"  {(Active(after,set,bonus)?"GAIN":"LOSE")} ({bonus.Pieces}) {data.SpellName(bonus.SpellId)}: {data.SpellDescription(bonus.SpellId)}");}return lines;
    }
    internal static BudgetReport Budget(CatalogItem item,IEnumerable<CatalogItem> catalog,TooltipData data,int level)
    {
        var values=data.Calculations.Evaluate(item,level);var lines=new List<BudgetLine>();long total=0;for(int n=1;n<=10;n++){int allocation=item.Number("StatPercentEditor"+n),stat=item.Number("StatModifierBonusStat"+n);if(stat<0||allocation==0)continue;total+=allocation;lines.Add(new(ItemTooltip.StatNames.GetValueOrDefault(stat)??$"Stat {stat}",values.Stats.TryGetValue(n,out int amount)?amount.ToString():"[scales]",allocation,(allocation/100m).ToString("0.##",CultureInfo.InvariantCulture)+"%"));}
        int SlotGroup(CatalogItem i)=>i.Slot==20?5:i.Slot;
        var peers=catalog.Where(i=>i.Id!=item.Id&&i.ItemLevel==item.ItemLevel&&i.Quality==item.Quality&&SlotGroup(i)==SlotGroup(item)&&i.ClassId==item.ClassId&&i.Number("SubclassID")==item.Number("SubclassID")).OrderBy(i=>i.Name).ToList();
        long Allocation(CatalogItem i)=>Enumerable.Range(1,10).Where(n=>i.Number("StatModifierBonusStat"+n)>=0).Sum(n=>(long)i.Number("StatPercentEditor"+n));
        var spent=peers.Select(Allocation).Order().ToArray();string assessment;
        if(spent.Length==0)assessment="No exact peers: budget strength cannot be rated against comparable items.";
        else{double median=spent.Length%2==0?((double)spent[spent.Length/2-1]+spent[spent.Length/2])/2:spent[spent.Length/2];assessment=$"{spent.Length} exact peers{(spent.Length<5?" (small sample)":"")} · median allocation {median:N0} · this item {total:N0}";if(median>0)assessment+=$" ({(total/median-1)*100:+0.#;-0.#;0}% vs median)";assessment+=median==0?" · peers have no stat allocation; no strength rating":median>0&&total>median*1.2?" · unusually high allocation":median>0&&total<median*.8?" · unusually low allocation":" · within peer range guide";}
        return new(lines,total,peers,assessment,"10,000 allocation units = 100% of the server's stat point basis for this level, quality and slot. This is a stat allocation guide, not a power score or a hard cap. Sockets can deduct stats; armor, weapon damage, spells, procs and set bonuses are separate. Peer flags use ±20% of the median as a design guide.");
    }
}
