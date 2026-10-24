using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
namespace AnoWowItemEditor;

internal sealed class SetChecklistWindow : Window
{
    internal CatalogResult Catalog {get;private set;}
    internal ReferenceCatalog? References {get;private set;}
    internal bool Saved {get;private set;}
    internal IReadOnlyList<SetCheck> Checks {get;private set;}=[];
    private readonly int setId,level;
    private readonly CharacterSnapshot? character;
    private readonly DataGrid table=DesignerWindow.Table(("Status","Status"),("Check","Check"),("Item / bonus","Subject"));
    private readonly StackPanel itemPreview=new();
    private readonly TextBox detail=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly TextBlock status=DesignerWindow.Text("Loading selected set's saved drop assignments…",12);
    private readonly CheckBox issues=new(){Content="Only issues / in-game checks",IsChecked=true,Margin=new Thickness(12,8,0,0)};
    private readonly Button open=new(){Content="Open selected check…",Margin=new Thickness(0,0,10,0)};
    private readonly TaskCompletionSource ready=new();
    internal Task Ready=>ready.Task;
    private bool busy;
    internal SetChecklistWindow(CatalogResult catalog,int setId,int level=60,CharacterSnapshot? character=null)
    {
        Catalog=catalog;this.setId=setId;this.level=level;this.character=character;Title="Set testing checklist · "+catalog.Tooltips.Sets[setId].Name;Width=1400;Height=850;MinWidth=1080;MinHeight=650;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(35,36,40));
        var root=new DockPanel{Margin=new Thickness(20),Background=Background};Content=root;var heading=DesignerWindow.Text(Title.ToUpperInvariant(),22);heading.Foreground=Brushes.LightSkyBlue;DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
        var help=DesignerWindow.Text($"Complete saved set · preview level {level} · "+(character is null?"no character selected":"character "+character.Character.Name)+". Select a check to read details; double-click or Open to reach its editor. Checked means data is present, not that live server behavior was tested.",13);help.Margin=new Thickness(0,10,0,12);DockPanel.SetDock(help,Dock.Top);root.Children.Add(help);
        var actions=new WrapPanel{Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(actions,Dock.Top);root.Children.Add(actions);actions.Children.Add(open);DesignerWindow.Button("Refresh checklist",actions).Click+=async(_,_)=>await Refresh();actions.Children.Add(issues);issues.Checked+=(_,_)=>Filter();issues.Unchecked+=(_,_)=>Filter();
        DockPanel.SetDock(status,Dock.Bottom);status.Margin=new Thickness(0,12,0,0);root.Children.Add(status);var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(1.2,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(16)});grid.ColumnDefinitions.Add(new());root.Children.Add(grid);grid.Children.Add(table);var details=new TabControl();details.Items.Add(new TabItem{Header="Check details",Content=detail});details.Items.Add(new TabItem{Header="Selected item",Content=DesignerWindow.Preview(itemPreview,"ITEM PREVIEW")});Grid.SetColumn(details,2);grid.Children.Add(details);
        var button=new FrameworkElementFactory(typeof(Button));button.SetBinding(ContentControl.ContentProperty,new Binding("Action"));button.AddHandler(Button.ClickEvent,new RoutedEventHandler(async(sender,e)=>{e.Handled=true;if(sender is FrameworkElement{DataContext:SetCheck row}){table.SelectedItem=row;await OpenSelected();}}));table.Columns.Add(new DataGridTemplateColumn{Header="Open",CellTemplate=new DataTemplate{VisualTree=button},Width=100});
        table.SelectionChanged+=(_,_)=>{open.IsEnabled=!busy&&table.SelectedItem is SetCheck;detail.Text=table.SelectedItem is SetCheck row?$"{row.Status} · {row.Check}\n{row.Subject}\n\n{row.Detail}\n\nOpen: {row.Action}":"Select a check.";if(table.SelectedItem is SetCheck selected&&Catalog.Items.FirstOrDefault(i=>i.Id==selected.ItemId) is {} item)TooltipView.Fill(itemPreview,ItemTooltip.Build(item,Catalog.Tooltips,Catalog.Items.ToDictionary(i=>i.Id),level,character:character));else TooltipView.Fill(itemPreview,[new("This check has no selected item.",ItemTooltip.Gray)]);};table.MouseDoubleClick+=async(_,e)=>{if(e.OriginalSource is Button)return;await OpenSelected();};open.Click+=async(_,_)=>await OpenSelected();Loaded+=async(_,_)=>{try{await Refresh();}finally{ready.TrySetResult();}};Closing+=(_,e)=>{if(busy)e.Cancel=true;};
    }
    private void Filter(){var selected=table.SelectedItem as SetCheck;table.ItemsSource=Checks.Where(c=>issues.IsChecked!=true||c.Status!="Checked").ToArray();table.SelectedItem=table.Items.Cast<SetCheck>().FirstOrDefault(c=>c.Check==selected?.Check&&c.ItemId==selected.ItemId&&c.SpellId==selected.SpellId)??table.Items.Cast<SetCheck>().FirstOrDefault();}
    private async Task Refresh()
    {
        if(busy)return;busy=true;open.IsEnabled=false;
        try{var set=Catalog.Tooltips.Sets[setId];Dictionary<int,SetDropCheck>? drops=null;string error="";try{drops=await SetChecklist.LoadDrops(set.Members);}catch(Exception ex){error=" Drop check unavailable: "+ex.Message;}Checks=SetChecklist.Build(Catalog,set,level,character,drops);Filter();status.Text=$"{Checks.Count(c=>c.Status=="Blocked")} blocked · {Checks.Count(c=>c.Status=="Review")} to review · {Checks.Count(c=>c.Status=="Checked")} data checks passed · live testing remains separate."+error;}
        catch(Exception ex){status.Text=ex.Message;}finally{busy=false;open.IsEnabled=table.SelectedItem is SetCheck;}
    }
    private async Task OpenSelected()
    {
        if(busy||table.SelectedItem is not SetCheck row)return;busy=true;open.IsEnabled=false;
        try{
            bool changed=false;
            if(row.Action=="Item"){
                var editor=new ItemEditorWindow(Catalog,row.ItemId){Owner=this};editor.ShowDialog();changed=editor.Saved;Saved|=changed;if(changed){var items=new List<CatalogItem>();foreach(int id in editor.SavedItemIds)items.Add((await ItemEditing.LoadAsync(id)).Current);Catalog=SetCatalogRefresh.Items(Catalog,items);}
            }else if(row.Action=="Drops"){
                var editor=new DropLocationWindow(Catalog.Items.Single(i=>i.Id==row.ItemId),persist:true){Owner=this};editor.ShowDialog();changed=editor.Saved;Saved|=changed;
            }else if(row.Action=="Spell"){
                References??=await ReferenceCatalog.LoadAsync();string explanation=References.Explain(row.SpellId);new EffectExplanationWindow("Read-only item spell · "+References.Data.SpellName(row.SpellId),explanation,explanation+"\n\nThis item's equip spell is read-only. Weapon proc assignments are edited through Weapon procs."){Owner=this}.ShowDialog();
            }else if(row.Action is "Bonus" or "Proc"){
                References??=await ReferenceCatalog.LoadAsync();var editor=new ContentEditorWindow(row.Action=="Bonus"?"Set":"Proc",Catalog,References,row.Action=="Bonus"?setId:row.ItemId,row.SpellId>0?row.SpellId:null){Owner=this};editor.ShowDialog();changed=editor.Saved;Saved|=changed;
                if(changed){References=await ReferenceCatalog.LoadAsync();Catalog=Catalog with{Tooltips=ContentEditorWindow.PreviewData(Catalog.Tooltips,References.Data)};if(row.Action=="Bonus"){var session=await ContentEditing.LoadAsync("Set",setId,Catalog.Tooltips.Sets[setId].Members.Length);Catalog.Tooltips.Sets[setId]=Catalog.Tooltips.Sets[setId] with{Bonuses=session.Initial.Select(b=>new SetBonus(b.Pieces,b.SpellId,b.Specialization,b.Trait)).ToArray()};}else{var session=await ContentEditing.LoadAsync("Proc",row.ItemId,17);Catalog.Tooltips.ItemSpells[row.ItemId]=session.Initial.Select(p=>new ItemSpellInfo(p.SpellId,p.Trigger,p.Cooldown)).ToList();}}
            }else if(row.Action=="Membership"){
                if(!(await ContentEditing.ListAsync()).Any(o=>o.Kind=="NewSet"&&o.EntityId==setId&&o.Active)){status.Text="Native set membership is read-only. Inspect the member item definitions; created sets can be changed in Modify created sets.";return;}
                References??=await ReferenceCatalog.LoadAsync();var previous=await CreatedSets.Load(setId);var editor=new CreatedSetsWindow(Catalog,References,initialSetId:setId){Owner=this};editor.ShowDialog();changed=editor.Saved;Saved|=changed;if(changed){References=await ReferenceCatalog.LoadAsync();Catalog=Catalog with{Tooltips=ContentEditorWindow.PreviewData(Catalog.Tooltips,References.Data)};Catalog=SetCatalogRefresh.CreatedSet(Catalog,await CreatedSets.Load(setId,previous),previous,References);}
            }else {var preview=new SetAnalysisWindow(Catalog,setId){Owner=this};preview.ShowDialog();changed=preview.Saved;Saved|=changed;if(changed){Catalog=preview.CatalogSnapshot;References=preview.EditedReferences??References;}}
            Saved|=changed;
        }catch(Exception ex){status.Text=ex.Message;}finally{busy=false;open.IsEnabled=true;}
        await Refresh();
    }
}
