using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace AnoWowItemEditor;

internal sealed class SpellFilters : WrapPanel
{
    internal ComboBox School {get;}=new();
    internal new ComboBox Effect {get;}=new();
    internal ComboBox Aura {get;}=new();
    internal ComboBox Usage {get;}=new();
    internal ComboBox Description {get;}=new();
    internal event Action? Changed;
    internal SpellFilters()
    {
        Margin=new Thickness(0,10,0,0);
        foreach(var (title,box) in new[]{("School",School),("Effect type",Effect),("Aura type",Aura),("Existing usage",Usage),("Description",Description)}){
            var field=new StackPanel{Width=175,Margin=new Thickness(0,0,10,8)};field.Children.Add(new TextBlock{Text=title,Foreground=Brushes.LightGray,Margin=new Thickness(0,0,0,5)});box.DisplayMemberPath="Label";box.SelectionChanged+=(_,_)=>Changed?.Invoke();field.Children.Add(box);Children.Add(field);
        }
        School.ItemsSource=new[]{new FilterChoice(-1,"All schools"),new(1,"Physical"),new(2,"Holy"),new(4,"Fire"),new(8,"Nature"),new(16,"Frost"),new(32,"Shadow"),new(64,"Arcane")};
        Usage.ItemsSource=new[]{new FilterChoice(-1,"All spells"),new(1,"Used by sets"),new(2,"Used by items"),new(3,"Server proc rules"),new(4,"No set/item usage")};
        Description.ItemsSource=new[]{new FilterChoice(-1,"All descriptions"),new(1,"Has description"),new(0,"Missing description")};
        Effect.ItemsSource=new[]{new FilterChoice(-1,"All effect types")};Aura.ItemsSource=new[]{new FilterChoice(-1,"All aura types")};Reset();
    }
    internal void Bind(IEnumerable<ReferenceEntry> entries)
    {
        int effect=Selected(Effect),aura=Selected(Aura);
        Effect.ItemsSource=new[]{new FilterChoice(-1,"All effect types")}.Concat(entries.SelectMany(s=>s.EffectTypes).Distinct().Order().Select(n=>new FilterChoice(n,$"Effect {n}"))).ToList();
        Aura.ItemsSource=new[]{new FilterChoice(-1,"All aura types")}.Concat(entries.SelectMany(s=>s.AuraTypes).Distinct().Order().Select(n=>new FilterChoice(n,$"Aura {n}"))).ToList();
        Effect.SelectedItem=Effect.Items.Cast<FilterChoice>().FirstOrDefault(c=>c.Id==effect)??Effect.Items[0];Aura.SelectedItem=Aura.Items.Cast<FilterChoice>().FirstOrDefault(c=>c.Id==aura)??Aura.Items[0];
    }
    internal void Reset(){foreach(var box in new[]{School,Effect,Aura,Usage,Description})box.SelectedIndex=0;Changed?.Invoke();}
    private static int Selected(ComboBox box)=>(box.SelectedItem as FilterChoice)?.Id??-1;
    internal bool Matches(ReferenceEntry entry)
    {
        int school=Selected(School),effect=Selected(Effect),aura=Selected(Aura),usage=Selected(Usage),description=Selected(Description);
        return (school<0||(entry.SchoolMask&school)!=0)&&(effect<0||entry.EffectTypes.Contains(effect))&&(aura<0||entry.AuraTypes.Contains(aura))
            &&(usage switch{1=>entry.UsedBySets,2=>entry.UsedByItems,3=>entry.HasProcRules,4=>!entry.UsedBySets&&!entry.UsedByItems,_=>true})&&(description<0||entry.HasDescription==(description==1));
    }
}
