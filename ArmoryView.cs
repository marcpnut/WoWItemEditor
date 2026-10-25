using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
namespace AnoWowItemEditor;

internal sealed class ArmoryView : DockPanel
{
    private readonly CharacterModelView model=new(savedAppearance:true,portrait:true);
    private readonly StackPanel tooltip=new();
    private readonly TextBlock note=DesignerWindow.Text("Select a saved character.",11),selection=DesignerWindow.Text("Select an equipment slot",12);
    private readonly Dictionary<int,Button> slots=[];
    private readonly Dictionary<int,System.Windows.Shapes.Path> symbols=[];
    private CatalogResult? catalog;
    private CharacterSnapshot? saved;
    private Dictionary<int,CatalogItem> worn=[];
    private int level=60,request,selectedSlot;
    private ModelCharacter? profile;
    private CatalogItem? inspectedItem;
    internal IReadOnlyDictionary<int,CatalogItem> Equipment=>worn;
    internal ModelCharacter? Profile=>profile;
    internal Task Loading=>model.Loading;
    internal CharacterScene? Scene=>model.Scene;
    internal string Feedback=>model.Feedback;
    internal event Func<int,Task>? EditRequested;
    internal event Func<Task>? CatalogReloadRequested;
    internal ArmoryView()
    {
        Background=TooltipView.Brush("#171B25");var controls=new WrapPanel{Margin=new Thickness(0,0,0,6)};SetDock(controls,Dock.Top);Children.Add(controls);DesignerWindow.Button("Find armor / weapons…",controls).Click+=async(_,_)=>await Pick(-1);DesignerWindow.Button("Change slot…",controls).Click+=async(_,_)=>await Pick(selectedSlot);DesignerWindow.Button("Reset equipment preview",controls).Click+=(_,_)=>Reset();
        var bottom=new StackPanel{Margin=new Thickness(5)};SetDock(bottom,Dock.Bottom);Children.Add(bottom);selection.Margin=new Thickness(0,4,0,4);bottom.Children.Add(selection);var actions=new WrapPanel();bottom.Children.Add(actions);DesignerWindow.Button("Edit item for everyone…",actions).Click+=async(_,_)=>{if(inspectedItem is {} item&&EditRequested is not null)await EditRequested(item.Id);};DesignerWindow.Button("Clear selected slot",actions).Click+=(_,_)=>{worn.Remove(selectedSlot);Render();};note.Margin=new Thickness(0,5,0,0);bottom.Children.Add(note);
        var body=new Grid();body.ColumnDefinitions.Add(new(){Width=new GridLength(55)});body.ColumnDefinitions.Add(new());body.ColumnDefinitions.Add(new(){Width=new GridLength(55)});body.RowDefinitions.Add(new());body.RowDefinitions.Add(new(){Height=GridLength.Auto});Children.Add(body);UniformGrid left=new UniformGrid{Rows=8,VerticalAlignment=VerticalAlignment.Stretch},right=new UniformGrid{Rows=8,VerticalAlignment=VerticalAlignment.Stretch};body.Children.Add(left);Grid.SetColumn(right,2);body.Children.Add(right);int[] leftSlots=[0,1,2,14,4,3,18,8],rightSlots=[9,5,6,7,10,11,12,13];foreach(int slot in leftSlots)left.Children.Add(Slot(slot));foreach(int slot in rightSlots)right.Children.Add(Slot(slot));Grid.SetColumn(model,1);body.Children.Add(model);var hands=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Center};Grid.SetRow(hands,1);Grid.SetColumnSpan(hands,3);body.Children.Add(hands);foreach(int slot in new[]{15,16,17})hands.Children.Add(Slot(slot));
    }
    private Button Slot(int slot)
    {
        var symbol=new System.Windows.Shapes.Path{Data=Geometry.Parse(EquipmentSetView.Symbol(slot)),Width=27,Height=29,Stretch=Stretch.Uniform,Stroke=Brushes.SlateGray,Fill=TooltipView.Brush("#232E40"),StrokeThickness=1.4};var button=new Button{Content=symbol,Width=41,Height=41,Padding=new Thickness(4),Margin=new Thickness(2),Background=TooltipView.Brush("#111722"),BorderThickness=new Thickness(2),ToolTip=GearDesign.EquipmentSlots[slot],Cursor=Cursors.Hand};slots[slot]=button;symbols[slot]=symbol;button.Click+=(_,_)=>SelectSlot(slot);button.MouseDoubleClick+=async(_,e)=>{e.Handled=true;SelectSlot(slot);if(worn.TryGetValue(slot,out var item)&&EditRequested is not null)await EditRequested(item.Id);else await Pick(slot);};button.ToolTipOpening+=(_,_)=>{var panel=new StackPanel{MaxWidth=350};if(catalog is not null&&worn.TryGetValue(slot,out var item))TooltipView.Fill(panel,ItemTooltip.Build(item,catalog.Tooltips,catalog.Items.ToDictionary(i=>i.Id),level,character:Preview()));else TooltipView.Fill(panel,[new(GearDesign.EquipmentSlots[slot],ItemTooltip.Gold),new("Empty · Change slot to find an item",ItemTooltip.Gray)]);button.ToolTip=new ToolTip{Content=panel,Background=TooltipView.Brush("#10111B"),BorderBrush=Brushes.SlateGray};};return button;
    }
    internal async Task Bind(CharacterSnapshot? snapshot,CatalogResult result,int previewLevel)
    {
        int token=++request;catalog=result;saved=snapshot;level=previewLevel;profile=null;Reset();if(snapshot is null)return;try{var loaded=await ModelCharacterRepository.Load(snapshot.Character);if(token!=request)return;profile=loaded;Render();}catch(Exception ex){if(token==request)note.Text="Appearance could not be loaded: "+ex.Message;}
    }
    internal void RefreshCatalog(CatalogResult result){catalog=result;var lookup=result.Items.ToDictionary(i=>i.Id);worn=worn.Where(p=>lookup.ContainsKey(p.Value.Id)).ToDictionary(p=>p.Key,p=>lookup[p.Value.Id]);Render();}
    internal void SetLevel(int value){level=value;SelectSlot(selectedSlot);}
    private void Reset(){var lookup=catalog?.Items.ToDictionary(i=>i.Id)??[];worn=saved?.Items.Where(i=>i.Equipped&&lookup.ContainsKey(i.Entry)).GroupBy(i=>i.Slot).ToDictionary(g=>g.Key,g=>lookup[g.First().Entry])??[];Render();}
    private void Render()
    {
        foreach(var (slot,button) in slots){bool filled=worn.TryGetValue(slot,out var item);button.BorderBrush=filled?item!.NameBrush:Brushes.DimGray;symbols[slot].Stroke=filled?item!.NameBrush:Brushes.SlateGray;button.Opacity=filled?1:.6;}if(saved is not null&&profile is not null)model.Bind(worn,profile);else model.Pending(saved is null?"Select a saved character.":"Loading saved character appearance…");note.Text="Equipment swaps are preview only. Double-click an item or slot to edit its server-wide definition.";SelectSlot(selectedSlot);
    }
    internal void Inspect(CatalogItem? item){if(item is null)return;inspectedItem=item;selection.Text=item.Name+" · use Try selected item to equip it in this preview";selection.Foreground=item.NameBrush;}
    internal void SelectSlot(int slot){selectedSlot=slot;foreach(var entry in slots)entry.Value.BorderThickness=new Thickness(entry.Key==slot?3:1);selection.Text=GearDesign.EquipmentSlots[slot]+" · "+(worn.TryGetValue(slot,out var item)?item.Name:"Empty");inspectedItem=item;selection.Foreground=item?.NameBrush??Brushes.LightGray;}
    private CharacterSnapshot? Preview(){if(saved is null)return null;var items=saved.Items.Where(i=>!i.Equipped).ToList();foreach(var (slot,item) in worn){var original=saved.Items.FirstOrDefault(i=>i.Equipped&&i.Slot==slot&&i.Entry==item.Id);items.Add(original??new(ulong.MaxValue-(ulong)slot,item.Id,1,0,slot,"Equipment preview",0,"","",0,0,true));}return saved with{Items=items};}
    private async Task Pick(int slot){if(catalog is null||saved is null)return;var picker=new ItemModelPicker(catalog,slot,worn,profile){Owner=Window.GetWindow(this)};bool accepted=picker.ShowDialog()==true;int id=picker.Selected?.Id??0,target=picker.SelectedSlot;if(picker.Saved&&CatalogReloadRequested is not null)await CatalogReloadRequested();if(accepted&&id>0&&catalog.Items.FirstOrDefault(i=>i.Id==id) is {} selected)Swap(target,selected);}
    internal void TryItem(CatalogItem item){var targets=GearDesign.Targets(item);if(targets.Length==0){note.Text="This item cannot be equipped.";return;}int slot=targets.Contains(selectedSlot)?selectedSlot:targets.FirstOrDefault(s=>!worn.ContainsKey(s),targets[0]);Swap(slot,item);}
    internal void Swap(int slot,CatalogItem item){if(!GearDesign.Targets(item).Contains(slot))throw new InvalidOperationException("The item does not fit this equipment slot.");selectedSlot=slot;worn[slot]=item;if(slot==15&&item.Slot==17)worn.Remove(16);if(slot==16&&worn.GetValueOrDefault(15)?.Slot==17)worn.Remove(15);Render();}
}

internal sealed class ItemModelPicker : Window
{
    private readonly ComboBox target=new(){DisplayMemberPath="Label",MinWidth=140};
    private int fixedSlot;
    private bool settingTarget;
    internal bool Saved {get;private set;}
    internal int SelectedSlot=>(target.SelectedItem as FilterChoice)?.Id??fixedSlot;
    private readonly DataGrid items=DesignerWindow.Table(("Name","Name"),("ID","Id"),("Subtype","SubtypeName"),("iLvl","ItemLevel"));
    internal CatalogItem? Selected=>items.SelectedItem as CatalogItem;
    internal ItemModelPicker(CatalogResult catalog,int slot,IReadOnlyDictionary<int,CatalogItem> outfit,ModelCharacter? profile)
    {
        fixedSlot=slot;Title=slot<0?"Find armor / weapons to try on":"Choose "+GearDesign.EquipmentSlots[slot];Width=1100;Height=800;MinWidth=850;MinHeight=600;Background=TooltipView.Brush("#232428");WindowStartupLocation=WindowStartupLocation.CenterOwner;var root=new DockPanel{Margin=new Thickness(16)};Content=root;var search=new TextBox{ToolTip="Search by item name or exact ID",Margin=new Thickness(0,0,0,8)};DockPanel.SetDock(search,Dock.Top);root.Children.Add(search);var filters=new ItemKindFilter(catalog.Tooltips,true);DockPanel.SetDock(filters,Dock.Top);root.Children.Add(filters);var actions=new WrapPanel{Margin=new Thickness(0,8,0,0)};DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);actions.Children.Add(DesignerWindow.Text("Equip in",12));actions.Children.Add(target);var choose=DesignerWindow.Button("Try on selected item",actions);choose.Click+=(_,_)=>{if(Selected is not null&&SelectedSlot>=0)DialogResult=true;};DesignerWindow.Button("Edit selected item for everyone…",actions).Click+=async(_,_)=>await EditSelected();DesignerWindow.Button("Cancel",actions).Click+=(_,_)=>Close();var body=new Grid();body.ColumnDefinitions.Add(new());body.ColumnDefinitions.Add(new(){Width=new GridLength(430)});root.Children.Add(body);body.Children.Add(items);var panel=new StackPanel();var preview=DesignerWindow.Preview(panel,"CANDIDATE ITEM");Grid.SetColumn(preview,1);body.Children.Add(preview);var lookup=catalog.Items.ToDictionary(i=>i.Id);
        void Filter(){string q=search.Text.Trim();items.ItemsSource=catalog.Items.Where(i=>i.ClassId is 2 or 4&&GearDesign.Targets(i).Length>0&&(slot<0||GearDesign.Targets(i).Contains(slot))&&filters.Matches(i)&&CatalogSearch.Item(i,q)).ToList();items.SelectedIndex=0;}
        void Preview(){choose.IsEnabled=Selected is not null&&SelectedSlot>=0;if(Selected is not {} item){TooltipView.Fill(panel,[new("No matching items",ItemTooltip.Gray)]);return;}TooltipView.Fill(panel,ItemTooltip.Build(item,catalog.Tooltips,lookup));var worn=new Dictionary<int,CatalogItem>(outfit);int chosen=SelectedSlot;if(chosen>=0){worn[chosen]=item;if(chosen==15&&item.Slot==17)worn.Remove(16);if(chosen==16&&worn.GetValueOrDefault(15)?.Slot==17)worn.Remove(15);}ItemVisualPreview.Outfit(panel,worn,profile);}
        items.SelectionChanged+=(_,_)=>{settingTarget=true;int previous=SelectedSlot;target.ItemsSource=Selected is {} item?GearDesign.Targets(item).Where(s=>fixedSlot<0||s==fixedSlot).Select(s=>new FilterChoice(s,GearDesign.EquipmentSlots[s])).ToArray():Array.Empty<FilterChoice>();target.SelectedItem=target.Items.Cast<FilterChoice>().FirstOrDefault(c=>c.Id==previous)??target.Items.Cast<FilterChoice>().FirstOrDefault();settingTarget=false;Preview();};target.SelectionChanged+=(_,_)=>{if(!settingTarget)Preview();};
        async Task EditSelected(){if(Selected is not {} item)return;var editor=new ItemEditorWindow(catalog,item.Id){Owner=this};editor.ShowDialog();if(!editor.Saved)return;Saved=true;var changed=new List<CatalogItem>();foreach(int id in editor.SavedItemIds)changed.Add((await ItemEditing.LoadAsync(id)).Current);catalog=SetCatalogRefresh.Items(catalog,changed);lookup=catalog.Items.ToDictionary(i=>i.Id);Filter();items.SelectedItem=items.Items.Cast<CatalogItem>().FirstOrDefault(i=>i.Id==item.Id);}
        items.MouseDoubleClick+=async(_,_)=>await EditSelected();search.TextChanged+=(_,_)=>Filter();filters.Changed+=Filter;Filter();
    }
}
