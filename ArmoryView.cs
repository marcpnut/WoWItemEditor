using System.Windows;
using System.Windows.Controls;
namespace AnoWowItemEditor;

internal sealed class ArmoryView : DockPanel
{
    private sealed record SlotRow(int Slot,string SlotName,string Name);
    private readonly DataGrid slots=DesignerWindow.Table(("Slot","SlotName"),("Equipped / preview item","Name"));
    private readonly CharacterModelView model=new();
    private readonly StackPanel tooltip=new();
    private readonly TextBlock note=DesignerWindow.Text("Select a character. Swaps affect this preview only.",12);
    private CatalogResult? catalog;
    private CharacterSnapshot? saved;
    private Dictionary<int,CatalogItem> worn=[];
    private int level=60,request;
    private ModelCharacter? profile;
    internal IReadOnlyDictionary<int,CatalogItem> Equipment=>worn;
    internal ModelCharacter? Profile=>profile;
    internal Task Loading=>model.Loading;
    internal CharacterScene? Scene=>model.Scene;
    internal string Feedback=>model.Feedback;
    internal ArmoryView()
    {
        var controls=new WrapPanel{Margin=new Thickness(0,0,0,8)};SetDock(controls,Dock.Top);Children.Add(controls);DesignerWindow.Button("Swap selected slot…",controls).Click+=(_,_)=>Pick();DesignerWindow.Button("Clear selected slot",controls).Click+=(_,_)=>{if(slots.SelectedItem is SlotRow row){worn.Remove(row.Slot);Render(row.Slot);}};DesignerWindow.Button("Reset saved equipment",controls).Click+=(_,_)=>Reset();
        SetDock(note,Dock.Bottom);note.Margin=new Thickness(8);Children.Add(note);var body=new Grid();body.ColumnDefinitions.Add(new(){Width=new GridLength(230)});body.ColumnDefinitions.Add(new());body.ColumnDefinitions.Add(new(){Width=new GridLength(300)});Children.Add(body);body.Children.Add(slots);Grid.SetColumn(model,1);body.Children.Add(model);var preview=DesignerWindow.Preview(tooltip,"SELECTED EQUIPMENT");Grid.SetColumn(preview,2);body.Children.Add(preview);slots.SelectionChanged+=(_,_)=>Tooltip();slots.MouseDoubleClick+=(_,_)=>Pick();
    }
    internal async Task Bind(CharacterSnapshot? snapshot,CatalogResult result,int previewLevel)
    {
        int token=++request;catalog=result;saved=snapshot;level=previewLevel;profile=null;Reset();if(snapshot is null)return;
        try{var loaded=await ModelCharacterRepository.Load(snapshot.Character);if(token!=request)return;profile=loaded;Render();}catch(Exception ex){if(token==request)note.Text="Appearance could not be loaded: "+ex.Message;}
    }
    internal void SetLevel(int value){level=value;Tooltip();}
    private void Reset(){worn=saved?.Items.Where(i=>i.Equipped&&catalog!.Items.Any(c=>c.Id==i.Entry)).GroupBy(i=>i.Slot).ToDictionary(g=>g.Key,g=>catalog!.Items.First(i=>i.Id==g.First().Entry))??[];Render();}
    private void Render(int? selected=null)
    {
        int slot=selected??(slots.SelectedItem as SlotRow)?.Slot??0;slots.ItemsSource=GearDesign.EquipmentSlots.Select(p=>new SlotRow(p.Key,p.Value,worn.GetValueOrDefault(p.Key)?.Name??"Empty")).ToArray();slots.SelectedItem=slots.Items.Cast<SlotRow>().FirstOrDefault(r=>r.Slot==slot);if(saved is not null)model.Bind(worn,profile??new(Name:saved.Character.Name));note.Text=saved is null?"Select a saved character.":$"{saved.Character.Name} · armory preview · double-click a slot to swap · saved equipment is unchanged";Tooltip();
    }
    private CharacterSnapshot? Preview()
    {
        if(saved is null)return null;var items=saved.Items.Where(i=>!i.Equipped).ToList();foreach(var (slot,item) in worn){var original=saved.Items.FirstOrDefault(i=>i.Equipped&&i.Slot==slot&&i.Entry==item.Id);items.Add(original??new(ulong.MaxValue-(ulong)slot,item.Id,1,0,slot,"Armory preview",0,"","",0,0,true));}return saved with{Items=items};
    }
    private void Tooltip(){if(catalog is null)return;if(slots.SelectedItem is SlotRow row&&worn.TryGetValue(row.Slot,out var item))TooltipView.Fill(tooltip,ItemTooltip.Build(item,catalog.Tooltips,catalog.Items.ToDictionary(i=>i.Id),level,character:Preview()));else TooltipView.Fill(tooltip,[new("Empty equipment slot",ItemTooltip.Gray)]);ItemVisualPreview.Outfit(tooltip,worn,profile);}
    private void Pick(){if(catalog is null||saved is null||slots.SelectedItem is not SlotRow row)return;var picker=new ItemModelPicker(catalog,row.Slot,worn,profile){Owner=Window.GetWindow(this)};if(picker.ShowDialog()==true&&picker.Selected is {} item)Swap(row.Slot,item);}
    internal void Swap(int slot,CatalogItem item){if(!GearDesign.Targets(item).Contains(slot))throw new InvalidOperationException("The item does not fit this equipment slot.");worn[slot]=item;if(slot==15&&item.Slot==17)worn.Remove(16);if(slot==16&&worn.GetValueOrDefault(15)?.Slot==17)worn.Remove(15);Render(slot);}
}

internal sealed class ItemModelPicker : Window
{
    private readonly DataGrid items=DesignerWindow.Table(("Name","Name"),("ID","Id"),("Subtype","SubtypeName"),("iLvl","ItemLevel"));
    internal CatalogItem? Selected=>items.SelectedItem as CatalogItem;
    internal ItemModelPicker(CatalogResult catalog,int slot,IReadOnlyDictionary<int,CatalogItem> outfit,ModelCharacter? profile)
    {
        Title="Choose "+GearDesign.EquipmentSlots[slot];Width=1100;Height=800;MinWidth=850;MinHeight=600;Background=TooltipView.Brush("#232428");WindowStartupLocation=WindowStartupLocation.CenterOwner;var root=new DockPanel{Margin=new Thickness(16)};Content=root;var search=new TextBox{ToolTip="Search by item name or exact ID",Margin=new Thickness(0,0,0,8)};DockPanel.SetDock(search,Dock.Top);root.Children.Add(search);var filters=new ItemKindFilter(catalog.Tooltips,true);DockPanel.SetDock(filters,Dock.Top);root.Children.Add(filters);var actions=new WrapPanel{Margin=new Thickness(0,8,0,0)};DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);var choose=DesignerWindow.Button("Use selected item in preview",actions);choose.Click+=(_,_)=>{if(Selected is not null)DialogResult=true;};DesignerWindow.Button("Cancel",actions).Click+=(_,_)=>Close();var body=new Grid();body.ColumnDefinitions.Add(new());body.ColumnDefinitions.Add(new(){Width=new GridLength(430)});root.Children.Add(body);body.Children.Add(items);var panel=new StackPanel();var preview=DesignerWindow.Preview(panel,"CANDIDATE ITEM");Grid.SetColumn(preview,1);body.Children.Add(preview);var lookup=catalog.Items.ToDictionary(i=>i.Id);
        void Filter(){string q=search.Text.Trim();items.ItemsSource=catalog.Items.Where(i=>GearDesign.Targets(i).Contains(slot)&&filters.Matches(i)&&CatalogSearch.Item(i,q)).ToList();items.SelectedIndex=0;}
        items.SelectionChanged+=(_,_)=>{choose.IsEnabled=Selected is not null;if(Selected is not {} item){TooltipView.Fill(panel,[new("No matching items",ItemTooltip.Gray)]);return;}TooltipView.Fill(panel,ItemTooltip.Build(item,catalog.Tooltips,lookup));var worn=new Dictionary<int,CatalogItem>(outfit){[slot]=item};if(slot==15&&item.Slot==17)worn.Remove(16);if(slot==16&&worn.GetValueOrDefault(15)?.Slot==17)worn.Remove(15);ItemVisualPreview.Outfit(panel,worn,profile);};items.MouseDoubleClick+=(_,_)=>{if(Selected is not null)DialogResult=true;};search.TextChanged+=(_,_)=>Filter();filters.Changed+=Filter;Filter();
    }
}
