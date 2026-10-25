using System.Globalization;
namespace WoWItemEditor;

internal sealed record BonusValueRule(bool Supported,string Meaning,string Unit,decimal Min,decimal Max,string Reason)
{
    internal string Limits=>Supported?$"Editor limit: {Min:N0} to {Max:N0} {Unit}. {Reason}":Reason;
}

// Conservative editing policy, not a claim that the custom server has a universal safe numeric cap.
internal static class BonusValueRules
{
    internal static BonusValueRule For(Dictionary<string,string> row)
    {
        int effect=TooltipData.Number(row,"Effect"),aura=TooltipData.Number(row,"EffectAura"),operation=TooltipData.Number(row,"EffectMiscValue1");
        BonusValueRule Block(string reason)=>new(false,"Read-only effect","",0,0,reason);
        BonusValueRule Flat(string meaning,string unit="points",decimal max=10000)=>new(true,meaning,unit,0,max,"Fixed amount; applied by this effect's existing targeting and conditions.");
        BonusValueRule Percent(string meaning)=>new(true,meaning,"percentage points",0,100,"An additive chance bonus can still combine with other bonuses. This limit applies to this effect alone.");
        if(!BonusSpells.FixedAmount(row))return Block("Read-only: scaling, random ranges or coefficients require dedicated server support.");
        if(effect is 2 or 10)return Flat(effect==2?"Damage dealt by this effect":"Health restored by this effect");
        if(effect!=6)return Block("Read-only: this effect's value may be a count, identifier or scripted parameter; no validated numeric editor is available.");
        return aura switch{
            52=>Percent("Weapon critical strike chance added"),
            57=>Percent("Spell critical strike chance added"),
            54=>Percent("Melee/ranged hit chance added"),
            55=>Percent("Spell hit chance added"),
            29=>Flat("Attribute amount added (the existing effect selects the attribute)","stat points",1000),
            189=>Flat("Combat rating added (rating is not critical strike percentage)","rating",1000),
            13=>Flat("Damage bonus for the effect's existing school/targets"),
            22=>Flat("Resistance added for the effect's existing schools"),
            99 or 124=>Flat("Attack power added"),
            115 or 135=>Flat("Healing bonus added"),
            107 when row.ContainsKey("EffectMiscValue1")&&operation==0=>Flat("Flat damage/healing added to the affected ability; 150 adds 150 points, not 150%"),
            107 when row.ContainsKey("EffectMiscValue1")&&operation==7=>Percent("Critical strike chance added to the affected ability"),
            108 when row.ContainsKey("EffectMiscValue1")&&operation==0=>new(true,"Damage/healing modifier for the affected ability; 25 means +25%","percent",0,100,"Percentage modifier, not flat damage. Editor limit is a conservative policy."),
            _=>Block("Read-only: the meaning and safe bounds of this aura/modifier have not been verified. Existing behavior is preserved.")
        };
    }
    internal static void Validate(Dictionary<string,string> row,string value)
    {
        var rule=For(row);
        if(!rule.Supported)throw new InvalidOperationException(rule.Reason);
        if(!decimal.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out decimal number)||number<rule.Min||number>rule.Max||decimal.Truncate(number)!=number)
            throw new InvalidOperationException($"{rule.Meaning}: enter a whole number from {rule.Min:N0} to {rule.Max:N0} {rule.Unit}. Values are rejected rather than silently clamped.");
    }
}
