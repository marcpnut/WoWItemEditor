using System.Globalization;
namespace AnoWowItemEditor;

public sealed record TooltipLine(string Text,string Color="#FFFFFF",string? Right=null,int Indent=0,double Size=13,bool Bold=false);

internal static class ItemTooltip
{
    internal const string Gold="#FFD100",Green="#1EFF00",Gray="#8B8B95";
    private static readonly Dictionary<int,string> StatNames=new() {
        [0]="Mana",[1]="Health",[3]="Agility",[4]="Strength",[5]="Intellect",[6]="Spirit",[7]="Stamina",
        [12]="defense rating",[13]="dodge rating",[14]="parry rating",[15]="block rating",
        [16]="melee hit rating",[17]="ranged hit rating",[18]="spell hit rating",
        [19]="melee critical strike rating",[20]="ranged critical strike rating",[21]="spell critical strike rating",
        [28]="melee haste rating",[29]="ranged haste rating",[30]="spell haste rating",[31]="hit rating",
        [32]="critical strike rating",[35]="resilience rating",[36]="haste rating",[37]="expertise rating",
        [38]="attack power",[39]="ranged attack power",[40]="versatility",[41]="healing spell power",
        [42]="spell damage",[43]="mana regeneration",[44]="armor penetration rating",[45]="spell power",
        [46]="health regeneration",[47]="spell penetration",[48]="block value",[49]="mastery rating",
        [50]="bonus armor",[51]="Fire Resistance",[52]="Frost Resistance",[53]="Holy Resistance",
        [54]="Shadow Resistance",[55]="Nature Resistance",[56]="Arcane Resistance",
        [57]="PvP power",[61]="speed",[62]="leech",[63]="avoidance",
        [71]="Agility / Strength / Intellect",[72]="Agility / Strength",[73]="Agility / Intellect",[74]="Strength / Intellect"
    };
    internal static List<TooltipLine> Build(CatalogItem item,TooltipData data,IReadOnlyDictionary<int,CatalogItem> catalog)
    {
        var lines=new List<TooltipLine>();
        void Add(string text,string color="#FFFFFF",string? right=null,int indent=0,double size=13,bool bold=false)=>lines.Add(new(text,color,right,indent,size,bold));
        void Gap()=>Add("",size:5);
        Add(item.Name,item.NameBrush.ToString(),size:18,bold:true);
        Add($"Item Level {item.ItemLevel}",Gold);
        string binding=item.Number("Bonding") switch {1=>"Binds when picked up",2=>"Binds when equipped",3=>"Binds when used",4 or 5=>"Quest Item",_=>""};
        if(binding.Length>0)Add(binding);
        if(item.Number("MaxCount")==1)Add("Unique");
        else if(item.Number("MaxCount")>1)Add($"Unique ({item.Number("MaxCount")})");
        string subtype=data.Subclasses.GetValueOrDefault((item.ClassId,item.Number("SubclassID")))??item.TypeName;
        if(item.Slot>0)Add(item.SlotName,right:subtype);
        if(item.Number("ContainerSlots")>0)Add($"{item.Number("ContainerSlots")} Slot Bag");
        bool armor=item.ClassId==4&&item.Number("SubclassID") is >=1 and <=6&&item.Slot>0;
        bool weapon=item.ClassId==2&&item.Number("ItemDelay")>0;
        if(armor)Add("[scales] Armor");
        if(weapon) { Add("[scales] – [scales] Damage",right:$"Speed {item.Number("ItemDelay")/1000m:0.00}"); Add("([scales] damage per second)"); }
        var secondary=new List<TooltipLine>();
        for(int i=1;i<=10;i++)
        {
            int stat=item.Number("StatModifierBonusStat"+i); if(stat<0||item.Number("StatPercentEditor"+i)==0)continue;
            string name=StatNames.GetValueOrDefault(stat)??$"Stat {stat}";
            if(stat<=7||stat is >=51 and <=56||stat>=71)Add("+[scales] "+name);
            else secondary.Add(new($"Equip: Increases {name} by [scales].",Green));
        }
        // Durability is client-derived and is not stored in this build's item record.
        if(armor||weapon)Add("Durability [client value] / [client value]");
        int mask=item.Number("AllowableClass");
        var classicClasses=new[]{1,2,3,4,5,7,8,9,11};
        var allowed=classicClasses.Where(c=>(mask&(1<<(c-1)))!=0).ToArray();
        if(mask>0&&allowed.Length>0&&allowed.Length<classicClasses.Length)
            Add("Classes: "+string.Join(", ",allowed.Select(c=>data.Classes.GetValueOrDefault(c)??$"Class {c}")));
        if(item.RequiredLevel>0)Add($"Requires Level {item.RequiredLevel}");
        if(item.Number("RequiredSkill")>0)Add($"Requires {data.Skills.GetValueOrDefault(item.Number("RequiredSkill"))??$"Skill {item.Number("RequiredSkill")}"} ({item.Number("RequiredSkillRank")})");
        if(item.Number("MinFactionID")>0) {
            string[] standings=["Hated","Hostile","Unfriendly","Neutral","Friendly","Honored","Revered","Exalted"];
            Add($"Requires {data.Factions.GetValueOrDefault(item.Number("MinFactionID"))??$"Faction {item.Number("MinFactionID")}"} – {standings.ElementAtOrDefault(item.Number("MinReputation"))??"[reputation]"}");
        }
        if(secondary.Count>0){Gap();lines.AddRange(secondary);}
        if(data.ItemSpells.TryGetValue(item.Id,out var spells)) {
            foreach(var effect in spells.Distinct()) {
                string trigger=effect.Trigger switch {0 or 5=>"Use",1=>"Equip",2=>"Chance on hit",6=>"Learn",_=>$"Trigger {effect.Trigger}"};
                Add($"{trigger}: {data.SpellDescription(effect.SpellId)}",Green);
                if(effect.Cooldown>0)Add($"({effect.Cooldown/1000m:0.##} sec cooldown)",Green);
            }
        }
        for(int n=1;n<=3;n++) {
            int socket=item.Number("SocketType"+n); if(socket==0)continue;
            string socketName=socket switch {1=>"Meta",2=>"Red",4=>"Yellow",8=>"Blue",14=>"Prismatic",_=>$"Type {socket}"};
            Add("◇ "+socketName+" Socket",Gray);
        }
        int socketBonus=item.Number("SocketMatchEnchantmentId");
        if(socketBonus==0)socketBonus=item.Number("Socket_match_enchantment_ID");
        if(socketBonus>0)Add("Socket Bonus: "+(data.Enchantments.GetValueOrDefault(socketBonus)??"[description unavailable]"),Gray);
        if(item.Number("ItemSet")>0) {
            Gap();
            if(data.Sets.TryGetValue(item.Number("ItemSet"),out var set)) {
                Add($"{set.Name} (0/{set.Members.Length})",Gold);
                foreach(int member in set.Members) Add(catalog.TryGetValue(member,out var entry)?entry.Name:$"Item {member}",Gray,indent:12);
                Gap();
                foreach(var bonus in set.Bonuses)Add($"({bonus.Pieces}) Set: {data.SpellDescription(bonus.SpellId)}"+(bonus.Specialization>0?$" [Specialization {bonus.Specialization}]":""),Gray);
            } else Add($"Set {item.Number("ItemSet")} [details unavailable]",Gold);
        }
        if(item.Text("Description").Length>0){Gap();Add("“"+item.Text("Description")+"”",Gold);}
        if(long.TryParse(item.Text("SellPrice"),NumberStyles.Integer,CultureInfo.InvariantCulture,out long price)&&price>0) {
            Gap();Add($"Sell Price: {price/10000}g {price/100%100}s {price%100}c");
        }
        return lines;
    }
}
