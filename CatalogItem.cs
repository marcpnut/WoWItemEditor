using System.Globalization;
using System.Windows.Media;
namespace AnoWowItemEditor;

public sealed class CatalogItem
{
    public Dictionary<string, string> Fields { get; }
    public bool Overridden { get; }
    public CatalogItem(Dictionary<string,string> fields, bool overridden = false) { Fields = fields; Overridden = overridden; }
    public string Text(string key) => Fields.GetValueOrDefault(key) ?? "";
    public int Number(string key) => int.TryParse(Text(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
    public int Id => Number("ID");
    public string Name => Text("Display").Length > 0 ? Text("Display") : $"Item {Id}";
    public int RequiredLevel => Number("RequiredLevel");
    public int ItemLevel => Number("ItemLevel");
    public int Quality => Number("OverallQualityID");
    public int Slot => Number("InventoryType");
    public int ClassId => Fields.ContainsKey("ClassID") ? Number("ClassID") : -1;
    public string QualityName => Qualities.ElementAtOrDefault(Quality) ?? $"Quality {Quality}";
    public string SlotName => Slots.GetValueOrDefault(Slot) ?? $"Slot {Slot}";
    public string TypeName => Classes.GetValueOrDefault(ClassId) ?? "Unknown";
    public string Source => Overridden ? "MariaDB override" : "Server DB2";
    public Brush NameBrush => new SolidColorBrush((Color)ColorConverter.ConvertFromString(Colors.ElementAtOrDefault(Quality) ?? "#FFFFFF"));
    public static readonly string[] Qualities = ["Poor", "Common", "Uncommon", "Rare", "Epic", "Legendary", "Artifact", "Heirloom", "WoW Token"];
    private static readonly string[] Colors = ["#9D9D9D", "#FFFFFF", "#1EFF00", "#0070DD", "#A335EE", "#FF8000", "#E6CC80", "#00CCFF", "#00CCFF"];
    public static readonly Dictionary<int,string> Slots = new() { [0]="Non-equippable",[1]="Head",[2]="Neck",[3]="Shoulder",[4]="Shirt",[5]="Chest",[6]="Waist",[7]="Legs",[8]="Feet",[9]="Wrists",[10]="Hands",[11]="Finger",[12]="Trinket",[13]="One-hand",[14]="Shield",[15]="Ranged",[16]="Back",[17]="Two-hand",[18]="Bag",[19]="Tabard",[20]="Robe",[21]="Main hand",[22]="Off hand",[23]="Held in off-hand",[24]="Ammo",[25]="Thrown",[26]="Ranged",[27]="Quiver",[28]="Relic" };
    public static readonly Dictionary<int,string> Classes = new() { [0]="Consumable",[1]="Container",[2]="Weapon",[3]="Gem",[4]="Armor",[5]="Reagent",[6]="Projectile",[7]="Trade goods",[8]="Item enhancement",[9]="Recipe",[10]="Money",[11]="Quiver",[12]="Quest",[13]="Key",[14]="Permanent",[15]="Miscellaneous",[16]="Glyph",[17]="Battle pet",[18]="WoW Token" };
}
public sealed record FilterChoice(int Id, string Label)
{
    public override string ToString() => Label;
}
