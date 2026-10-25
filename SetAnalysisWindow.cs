using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace WoWItemEditor;

internal sealed class SetAnalysisWindow : Window
{
    private CatalogResult catalog;
    private Dictionary<int,CatalogItem> lookup;
    private readonly int? initialSet;
    private readonly int[]? selectedIds;
    private readonly ComboBox designed=new(){DisplayMemberPath="Label",Width=360},reference=new(){DisplayMemberPath="Label",Width=360},character=new(){Width=280};
    private readonly ComboBox ringSlot=new(){DisplayMemberPath="Label",Width=155},trinketSlot=new(){DisplayMemberPath="Label",Width=155},handSlot=new(){DisplayMemberPath="Label",Width=155};
    private readonly TextBox level=new(){Text="60",Width=65};
    private readonly CheckBox subset=new(){Content="Use selected pieces only",Margin=new Thickness(12,22,0,0)},commonOnly=new(){Content="Compare common slots only",IsChecked=true,Margin=new Thickness(12,22,0,0)};
    private readonly TextBlock status=DesignerWindow.Text("Loading saved characters…",12),previewNote=DesignerWindow.Text("Choose a saved character and a set.",13),balanceNote=DesignerWindow.Text("",13);
    private readonly TextBox restrictions=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=105};
    private readonly Expander detail=new(){Header="Equip restrictions, set bonuses & preview limits",Margin=new Thickness(0,0,0,10)};
    private readonly EquipmentSetView equipment=new();
    private readonly DataGrid metrics=DesignerWindow.Table(("Base gear stat","Name"),("Current gear","Before"),("With set","After"),("Change","Change")),balanceMetrics=DesignerWindow.Table(("Base gear stat","Name"),("Reference set","Before"),("Designed set","After"),("Change","Change")),budgets=DesignerWindow.Table(("Designed piece","Item"),("Slot","Slot"),("iLvl","Level"),("Rarity","Rarity"),("Allocation","Allocation"),("Exact peers","Peers"),("Assessment","Assessment"));
    private readonly TextBox findings=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},bonuses=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly TabControl tabs=new();
    private CharacterSnapshot? snapshot;
    private bool binding;
    private int previewLevel=60,request;
    private readonly TaskCompletionSource ready=new();
    internal Task Ready=>ready.Task;
    internal bool Saved {get;private set;}
    internal CatalogResult CatalogSnapshot=>catalog;
    internal ReferenceCatalog? EditedReferences {get;private set;}
    internal SetEquipPreview? PreviewResult {get;private set;}
    internal SetBalanceReport? BalanceResult {get;private set;}
    internal SetAnalysisWindow(CatalogResult catalog,int? setId=null,int[]? selectedItems=null)
    {
        this.catalog=catalog;lookup=catalog.Items.ToDictionary(i=>i.Id);initialSet=setId;selectedIds=selectedItems;Title="Set preview & balance";Width=1560;Height=950;MinWidth=1250;MinHeight=760;Background=new SolidColorBrush(Color.FromRgb(35,36,40));WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new Thickness(20),Background=Background};Content=root;var heading=DesignerWindow.Text("SET PREVIEW & BALANCE",23);heading.Foreground=Brushes.LightSkyBlue;DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
        var controls=new WrapPanel{Margin=new Thickness(0,12,0,10)};DockPanel.SetDock(controls,Dock.Top);root.Children.Add(controls);Field(controls,"Designed set",designed);Field(controls,"Reference set for balance",reference);Field(controls,"Preview level",level);var apply=DesignerWindow.Button("Apply level",controls);apply.VerticalAlignment=VerticalAlignment.Bottom;apply.Margin=new Thickness(0,0,0,6);apply.Click+=(_,_)=>{if(!int.TryParse(level.Text,out int n)||n is <1 or >60){status.Text="Enter a preview level from 1 to 60.";return;}previewLevel=n;Render();};controls.Children.Add(subset);subset.IsEnabled=selectedItems is {Length:>0};subset.IsChecked=subset.IsEnabled;controls.Children.Add(commonOnly);DesignerWindow.Button("Set testing checklist…",controls).Click+=(_,_)=>{if(designed.SelectedItem is not ContentChoice set)return;var review=new SetChecklistWindow(catalog,set.Id,previewLevel,snapshot){Owner=this};review.ShowDialog();if(review.Saved){Saved=true;EditedReferences=review.References??EditedReferences;catalog=review.Catalog;lookup=catalog.Items.ToDictionary(i=>i.Id);BindSets();Render();}};
        DockPanel.SetDock(status,Dock.Bottom);status.Margin=new Thickness(0,10,0,0);root.Children.Add(status);root.Children.Add(tabs);tabs.Items.Add(new TabItem{Header="Full-set character preview",Content=CharacterPage()});tabs.Items.Add(new TabItem{Header="Set balance review",Content=BalancePage()});
        equipment.HideEditingActions();detail.Content=restrictions;designed.SelectionChanged+=(_,_)=>{if(!binding)Render();};reference.SelectionChanged+=(_,_)=>{if(!binding)RenderBalance();};subset.Checked+=(_,_)=>Render();subset.Unchecked+=(_,_)=>Render();commonOnly.Checked+=(_,_)=>RenderBalance();commonOnly.Unchecked+=(_,_)=>RenderBalance();
        BindSets();Loaded+=async(_,_)=>{try{binding=true;character.ItemsSource=await CharacterRepository.ListAsync();if(character.Items.Count>0)character.SelectedIndex=0;binding=false;if(character.SelectedItem is not null)await LoadCharacter();else{status.Text="No saved characters found. Set balance review remains available.";RenderBalance();}}catch(Exception ex){status.Text=ex.Message;}finally{binding=false;ready.TrySetResult();}};
    }
    private static void Field(Panel panel,string title,Control input){var field=new StackPanel{Margin=new Thickness(0,0,12,6)};field.Children.Add(DesignerWindow.Text(title,11));input.Margin=new Thickness(0,5,0,0);field.Children.Add(input);panel.Children.Add(field);}
    private FrameworkElement CharacterPage()
    {
        var root=new DockPanel{Margin=new Thickness(8),ClipToBounds=true};var controls=new WrapPanel{Margin=new Thickness(0,0,0,10)};DockPanel.SetDock(controls,Dock.Top);root.Children.Add(controls);Field(controls,"Character",character);Field(controls,"Single ring replaces",ringSlot);Field(controls,"Single trinket replaces",trinketSlot);Field(controls,"Single one-hand weapon replaces",handSlot);var reload=DesignerWindow.Button("Reload character",controls);reload.VerticalAlignment=VerticalAlignment.Bottom;reload.Click+=async(_,_)=>await LoadCharacter();
        foreach(var (box,slots) in new[]{(ringSlot,new[]{10,11}),(trinketSlot,new[]{12,13}),(handSlot,new[]{15,16})}){box.ItemsSource=slots.Select(s=>new FilterChoice(s,GearDesign.EquipmentSlots[s])).ToArray();box.SelectedIndex=0;box.SelectionChanged+=(_,_)=>{if(!binding)RenderCharacter();};}character.SelectionChanged+=async(_,_)=>{if(!binding)await LoadCharacter();};
        DockPanel.SetDock(previewNote,Dock.Top);previewNote.Margin=new Thickness(0,0,0,10);root.Children.Add(previewNote);DockPanel.SetDock(detail,Dock.Top);root.Children.Add(detail);metrics.Height=200;var stats=new Expander{Header="Character base-stat comparison · current gear / with set / change",Content=metrics,Margin=new Thickness(0,8,0,0)};DockPanel.SetDock(stats,Dock.Bottom);root.Children.Add(stats);root.Children.Add(equipment);return root;
    }
    private FrameworkElement BalancePage()
    {
        var root=new DockPanel{Margin=new Thickness(8),ClipToBounds=true};DockPanel.SetDock(balanceNote,Dock.Top);balanceNote.Margin=new Thickness(0,0,0,10);root.Children.Add(balanceNote);balanceMetrics.Height=165;DockPanel.SetDock(balanceMetrics,Dock.Top);root.Children.Add(balanceMetrics);budgets.Height=180;DockPanel.SetDock(budgets,Dock.Top);root.Children.Add(budgets);var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new());root.Children.Add(grid);grid.Children.Add(Panel("Design findings & limits",findings));var b=Panel("Reference and designed set bonuses",bonuses);Grid.SetColumn(b,1);grid.Children.Add(b);return root;
    }
    private static FrameworkElement Panel(string title,UIElement content){var panel=new DockPanel{Margin=new Thickness(0,10,12,0)};var text=DesignerWindow.Text(title,13);text.Margin=new Thickness(0,0,0,6);text.Foreground=Brushes.LightSkyBlue;DockPanel.SetDock(text,Dock.Top);panel.Children.Add(text);panel.Children.Add(content);return panel;}
    private void BindSets(){int? current=(designed.SelectedItem as ContentChoice)?.Id,baseline=(reference.SelectedItem as ContentChoice)?.Id;binding=true;var choices=catalog.Tooltips.Sets.Values.Where(s=>s.Members.Any(lookup.ContainsKey)).OrderBy(s=>s.Name).Select(s=>new ContentChoice(s.Id,s.Name)).ToArray();designed.ItemsSource=choices;reference.ItemsSource=choices;designed.SelectedItem=choices.FirstOrDefault(s=>s.Id==(current??initialSet))??choices.FirstOrDefault();reference.SelectedItem=choices.FirstOrDefault(s=>s.Id==baseline)??choices.FirstOrDefault(s=>s.Id==523&&s.Id!=(designed.SelectedItem as ContentChoice)?.Id)??choices.FirstOrDefault(s=>s.Id!=(designed.SelectedItem as ContentChoice)?.Id)??choices.FirstOrDefault();binding=false;RenderBalance();}
    private CatalogItem[] Pieces(){if(designed.SelectedItem is not ContentChoice selected)return [];var set=catalog.Tooltips.Sets[selected.Id];return set.Members.Where(id=>lookup.ContainsKey(id)&&(!(subset.IsChecked==true&&selected.Id==initialSet)||selectedIds?.Contains(id)==true)).Select(id=>lookup[id]).ToArray();}
    private async Task LoadCharacter(){int token=++request;snapshot=null;RenderCharacter();if(character.SelectedItem is not CharacterInfo selected)return;try{status.Text="Loading saved equipment; character data will not be changed…";var loaded=await CharacterRepository.LoadAsync(selected);if(token!=request)return;snapshot=loaded;previewLevel=Math.Clamp(loaded.Character.Level,1,60);level.Text=previewLevel.ToString();Render();status.Text="Simulation never writes character equipment. Checklist edits require review & save.";}catch(Exception ex){if(token==request)status.Text=ex.Message;}}
    private void Render(){if(binding)return;RenderCharacter();RenderBalance();}
    private void RenderCharacter()
    {
        if(binding)return;PreviewResult=null;if(snapshot is null){equipment.Bind([],catalog.Tooltips,lookup,previewLevel);metrics.ItemsSource=null;restrictions.Clear();previewNote.Text="Choose a saved character and a designed set.";return;}var pieces=Pieces();var preferred=new Dictionary<int,int>();foreach(var (type,box) in new[]{(11,ringSlot),(12,trinketSlot),(13,handSlot)}){var flex=pieces.Where(i=>i.Slot==type).ToArray();if(flex.Length==1&&box.SelectedItem is FilterChoice selected)preferred[flex[0].Id]=selected.Id;}
        var result=SetDesignReview.Equip(snapshot,pieces,lookup,previewLevel,preferred);PreviewResult=result;var rows=result.After.Items.Where(i=>i.Equipped&&lookup.ContainsKey(i.Entry)).Select(i=>new EquipmentViewItem(i.Guid,lookup[i.Entry],i.Slot));equipment.Bind(rows,catalog.Tooltips,lookup,previewLevel,result.After);if(result.Slots.Count>0)equipment.SelectSlot(result.Slots.Keys.First());metrics.ItemsSource=GearDesign.Compare(snapshot,result.After,lookup,catalog.Tooltips,previewLevel);var missing=snapshot.Items.Where(i=>i.Equipped&&!lookup.ContainsKey(i.Entry)).Select(i=>i.Entry).ToArray();previewNote.Text=$"{snapshot.Character.Name} · preview level {previewLevel} · {result.Slots.Count}/{pieces.Length} set pieces placed · "+(result.Allowed?"Saved class/level/skill checks passed; proficiency checks still apply.":"Equip restrictions/conflicts found: totals are hypothetical.")+(missing.Length>0?" Missing current item definitions: "+string.Join(", ",missing):"");var changes=GearDesign.SetChanges(snapshot,result.After,lookup,catalog.Tooltips);restrictions.Text=string.Join("\n",result.Problems.Concat(changes.Count>0?changes:["No set bonus threshold changes."]).Concat(result.Notes));detail.IsExpanded=result.Problems.Count>0;
    }
    private void RenderBalance(){if(binding||designed.SelectedItem is not ContentChoice a||reference.SelectedItem is not ContentChoice b)return;var pieces=Pieces();var report=SetDesignReview.Balance(catalog,catalog.Tooltips.Sets[a.Id],catalog.Tooltips.Sets[b.Id],pieces,previewLevel,commonOnly.IsChecked==true);BalanceResult=report;balanceMetrics.ItemsSource=report.Metrics;budgets.ItemsSource=report.Pieces;findings.Text=string.Join("\n\n",report.Findings);bonuses.Text=string.Join("\n\n",report.Bonuses);balanceNote.Text=$"{a.Name} vs {b.Name} · {pieces.Length} designed pieces · {report.CommonSlots} common slots · preview level {previewLevel}. Stats are base values; bonus/proc power is reviewed separately.";}
    internal async Task SetVerificationCharacter(CharacterSnapshot value){await Ready;request++;binding=true;character.ItemsSource=new[]{value.Character};character.SelectedIndex=0;binding=false;snapshot=value;previewLevel=value.Character.Level;level.Text=previewLevel.ToString();Render();}
    internal void SelectVerificationTab(int index){tabs.SelectedIndex=index;}
}
