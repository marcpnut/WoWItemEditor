using System.Windows;
using System.Windows.Controls;
namespace AnoWowItemEditor;
internal sealed record ItemSubtypeChoice(int ClassId,int SubclassId,string Label)
{
    public override string ToString()=>Label;
}
internal static class ItemKinds
{
    private static Dictionary<(int,int),string> names=new(){[(4,0)]="Miscellaneous",[(4,1)]="Cloth",[(4,2)]="Leather",[(4,3)]="Mail",[(4,4)]="Plate",[(4,6)]="Shield"};
    internal static void Use(TooltipData data){if(data.Subclasses.Count>0)names=new(data.Subclasses);}
    internal static string Name(CatalogItem item)=>names.GetValueOrDefault((item.ClassId,item.Number("SubclassID")))??$"Subclass {item.Number("SubclassID")}";
    internal static void Bind(ComboBox box,TooltipData data,int itemClass=-1)
    {
        Use(data);var selected=box.SelectedItem as ItemSubtypeChoice;var values=new[]{new ItemSubtypeChoice(-1,-1,"All subtypes")}.Concat(names.Where(p=>(itemClass<0||p.Key.Item1==itemClass)&&!string.IsNullOrWhiteSpace(p.Value)).OrderBy(p=>p.Key.Item1).ThenBy(p=>p.Key.Item2).Select(p=>new ItemSubtypeChoice(p.Key.Item1,p.Key.Item2,(itemClass<0?CatalogItem.Classes.GetValueOrDefault(p.Key.Item1)+" · ":"")+p.Value))).ToList();box.DisplayMemberPath="Label";box.ItemsSource=values;box.SelectedItem=values.FirstOrDefault(v=>selected is not null&&v.ClassId==selected.ClassId&&v.SubclassId==selected.SubclassId)??values[0];
    }
    internal static bool Matches(ComboBox box,CatalogItem item)=>box.SelectedItem is not ItemSubtypeChoice choice||choice.ClassId<0||choice.ClassId==item.ClassId&&choice.SubclassId==item.Number("SubclassID");
}
internal sealed class ItemKindFilter : StackPanel
{
    internal ComboBox ItemClass {get;}=new(){DisplayMemberPath="Label"};
    internal ComboBox Subtype {get;}=new(){DisplayMemberPath="Label"};
    private TooltipData data=new();
    private bool setting;
    internal event Action? Changed;
    internal ItemKindFilter(TooltipData data,bool horizontal=false)
    {
        this.data=data;Orientation=horizontal?Orientation.Horizontal:Orientation.Vertical;
        foreach(var (title,combo) in new[]{("Item class",ItemClass),("Subtype · armor material",Subtype)}){var field=new StackPanel{Margin=new Thickness(0,0,horizontal?10:0,8)};if(horizontal)field.Width=170;field.Children.Add(DesignerWindow.Text(title,11));combo.Margin=new Thickness(0,5,0,0);field.Children.Add(combo);Children.Add(field);}
        ItemClass.SelectionChanged+=(_,_)=>{if(setting)return;setting=true;ItemKinds.Bind(Subtype,this.data,(ItemClass.SelectedItem as FilterChoice)?.Id??-1);setting=false;Changed?.Invoke();};Subtype.SelectionChanged+=(_,_)=>{if(!setting)Changed?.Invoke();};Bind(data);
    }
    internal void Bind(TooltipData value){data=value;setting=true;int previous=(ItemClass.SelectedItem as FilterChoice)?.Id??-1;ItemClass.ItemsSource=new[]{new FilterChoice(-1,"All item classes")}.Concat(CatalogItem.Classes.Select(p=>new FilterChoice(p.Key,p.Value))).ToList();ItemClass.SelectedItem=ItemClass.Items.Cast<FilterChoice>().FirstOrDefault(v=>v.Id==previous)??ItemClass.Items[0];ItemKinds.Bind(Subtype,data,previous);setting=false;}
    internal bool Matches(CatalogItem item)=>((ItemClass.SelectedItem as FilterChoice)?.Id is not int type||type<0||type==item.ClassId)&&ItemKinds.Matches(Subtype,item);
}
