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
    internal ComboBox Trigger {get;}=new();
    internal ComboBox Description {get;}=new();
    internal event Action? Changed;
    private bool editingOptions;
    private List<FilterChoice> allEffects=[],allAuras=[];
    internal SpellFilters()
    {
        Margin=new Thickness(0,10,0,0);
        foreach(var (title,box) in new[]{("School",School),("Effect type",Effect),("Aura type",Aura),("Existing usage",Usage),("Description",Description),("Trigger",Trigger)}){
            var field=new StackPanel{Width=175,Margin=new Thickness(0,0,10,8)};field.Children.Add(new TextBlock{Text=title,Foreground=Brushes.LightGray,Margin=new Thickness(0,0,0,5)});box.DisplayMemberPath="Label";box.SelectionChanged+=(_,_)=>{if(!editingOptions)Changed?.Invoke();};field.Children.Add(box);Children.Add(field);
        }
        School.ItemsSource=new[]{new FilterChoice(-1,"All schools"),new(1,"Physical"),new(2,"Holy"),new(4,"Fire"),new(8,"Nature"),new(16,"Frost"),new(32,"Shadow"),new(64,"Arcane")};
        Usage.ItemsSource=new[]{new FilterChoice(-1,"All spells"),new(1,"Used by sets"),new(2,"Used by items"),new(3,"Server proc rules"),new(4,"No set/item usage")};
        Description.ItemsSource=new[]{new FilterChoice(-1,"All descriptions"),new(1,"Has description"),new(0,"Missing description")};
        Trigger.DisplayMemberPath="";Trigger.ItemsSource=new[]{"All triggers"};Effect.ItemsSource=new[]{new FilterChoice(-1,"All effect types")};Aura.ItemsSource=new[]{new FilterChoice(-1,"All aura types")};EnableTypeSearch(Effect,()=>allEffects);EnableTypeSearch(Aura,()=>allAuras);Reset();
    }
    private sealed record Options(List<string> Triggers,List<FilterChoice> Effects,List<FilterChoice> Auras);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object,Options> options=new();
    internal static void Prepare(IEnumerable<ReferenceEntry> entries)=>_ = OptionsFor(entries);
    private static Options OptionsFor(IEnumerable<ReferenceEntry> entries)=>options.GetValue(entries,_=>new(new[]{"All triggers"}.Concat(entries.SelectMany(e=>e.Triggers).Distinct().Order().Select(t=>t=="—"?"No item-effect template":t)).ToList(),new[]{new FilterChoice(-1,"All effect types")}.Concat(entries.SelectMany(e=>e.EffectTypes).Distinct().Order().Select(n=>new FilterChoice(n,SpellTypeNames.Effect(n)))).ToList(),new[]{new FilterChoice(-1,"All aura types")}.Concat(entries.SelectMany(e=>e.AuraTypes).Distinct().Order().Select(n=>new FilterChoice(n,SpellTypeNames.Aura(n)))).ToList()));
    internal void Bind(IEnumerable<ReferenceEntry> entries)
    {
        int effect=Selected(Effect),aura=Selected(Aura);string? trigger=Trigger.SelectedItem as string;var prepared=OptionsFor(entries);editingOptions=true;
        try{Trigger.ItemsSource=prepared.Triggers;Trigger.SelectedItem=Trigger.Items.Contains(trigger)?trigger:Trigger.Items[0];allEffects=prepared.Effects;allAuras=prepared.Auras;Effect.ItemsSource=allEffects;Aura.ItemsSource=allAuras;Effect.SelectedItem=allEffects.FirstOrDefault(c=>c.Id==effect)??allEffects[0];Aura.SelectedItem=allAuras.FirstOrDefault(c=>c.Id==aura)??allAuras[0];}finally{editingOptions=false;}
    }

    private void EnableTypeSearch(ComboBox box,Func<List<FilterChoice>> choices)
    {
        box.IsEditable=true;box.IsTextSearchEnabled=false;box.StaysOpenOnEdit=true;box.ToolTip="Type a name or ID to narrow choices, then select a type.";
        box.AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler((_,_)=>{if(editingOptions||!box.IsKeyboardFocusWithin)return;string query=box.Text;if(box.SelectedItem is FilterChoice current&&query==current.Label)return;SearchTypes(box,choices(),query);box.IsDropDownOpen=true;}));
        box.DropDownOpened+=(_,_)=>{if(box.SelectedItem is FilterChoice selected&&box.Text==selected.Label){editingOptions=true;box.ItemsSource=choices();box.SelectedItem=choices().FirstOrDefault(c=>c.Id==selected.Id);editingOptions=false;}};
    }
    private void SearchTypes(ComboBox box,List<FilterChoice> choices,string query)
    {
        editingOptions=true;var words=query.Trim().Split(' ',StringSplitOptions.RemoveEmptyEntries);box.ItemsSource=choices.Where(c=>c.Id<0||words.All(w=>c.Label.Contains(w,StringComparison.OrdinalIgnoreCase))).ToList();box.Text=query;if(box.Template?.FindName("PART_EditableTextBox",box) is TextBox edit)edit.CaretIndex=query.Length;editingOptions=false;
    }
    internal void SearchAuraForVerification(string query)=>SearchTypes(Aura,allAuras,query);
    internal void Reset(){editingOptions=true;Effect.ItemsSource=allEffects;Aura.ItemsSource=allAuras;editingOptions=false;foreach(var box in new[]{School,Effect,Aura,Usage,Description,Trigger})box.SelectedIndex=0;Changed?.Invoke();}
    private static int Selected(ComboBox box)=>(box.SelectedItem as FilterChoice)?.Id??-1;
    internal bool Matches(ReferenceEntry entry)
    {
        int school=Selected(School),effect=Selected(Effect),aura=Selected(Aura),usage=Selected(Usage),description=Selected(Description);
        return (Trigger.SelectedIndex<=0||entry.Triggers.Contains((string?)Trigger.SelectedItem=="No item-effect template"?"—":(string?)Trigger.SelectedItem))&&(school<0||(entry.SchoolMask&school)!=0)&&(effect<0||entry.EffectTypes.Contains(effect))&&(aura<0||entry.AuraTypes.Contains(aura))
            &&(usage switch{1=>entry.UsedBySets,2=>entry.UsedByItems,3=>entry.HasProcRules,4=>!entry.UsedBySets&&!entry.UsedByItems,_=>true})&&(description<0||entry.HasDescription==(description==1));
    }
}
