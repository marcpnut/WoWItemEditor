using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace AnoWowItemEditor;
internal sealed class BossChoice(InstanceBoss boss) : INotifyPropertyChanged
{
    internal InstanceBoss Boss {get;}=boss;
    private bool selected;
    public bool Selected {get=>selected;set{if(selected==value)return;selected=value;PropertyChanged?.Invoke(this,new(nameof(Selected)));}}
    public string Name=>Boss.Name;
    public string Difficulty=>Boss.Difficulty;
    public int Min=>Boss.Min;
    public int Max=>Boss.Max;
    public int Loot=>Boss.LootId;
    public event PropertyChangedEventHandler? PropertyChanged;
}
internal sealed record CurrentDropRow(string Location,string Source,int Loot,decimal Chance);
internal sealed class DropLocationWindow : Window
{
    private CatalogItem item;
    private WorldDropCatalog? world;
    private readonly bool persist;
    private ItemSnapshot? expected;
    private readonly ComboBox mode=new(){Width=220,ItemsSource=new[]{"World drops","Instance bosses only"},SelectedIndex=-1},instance=new(){MinWidth=250,DisplayMemberPath="Label",IsEditable=true,IsTextSearchEnabled=true};
    private readonly TextBox rate=new(){Text="0.01",Width=85},bossSearch=new(){ToolTip="Search boss name, creature ID or loot table"};
    private readonly DataGrid bosses=DesignerWindow.Table(("Boss","Name"),("Difficulty","Difficulty"),("Min","Min"),("Max","Max"),("Loot table","Loot")),preview=DesignerWindow.Table(("Creature / boss","Name"),("Level","Min"),("Maximum","Max"),("Location","Zones")),current=DesignerWindow.Table(("Instance / source","Location"),("Creature / boss","Source"),("Chance %","Chance"),("Loot table","Loot"));
    private readonly TextBlock info=DesignerWindow.Text("Loading drop sources…",13),feedback=DesignerWindow.Text("",12),currentNote=DesignerWindow.Text("",12),title=DesignerWindow.Text("",22);
    private readonly DockPanel bossControls=new();
    private readonly ColumnDefinition selectorColumn=new();
    private readonly Button save=new();
    private readonly List<BossChoice> choices=[];
    private readonly TaskCompletionSource ready=new();
    private List<CurrentDropRow> currentRows=[];
    private bool changing,busy,loaded;
    internal DropPlan? Result {get;private set;}
    internal bool Saved {get;private set;}
    internal Task Ready=>ready.Task;
    internal DropLocationWindow(CatalogItem item,DropPlan? initial=null,WorldDropCatalog? world=null,bool persist=false)
    {
        this.item=item;this.world=world;this.persist=persist;Title="Drop locations · "+item.Name;Width=1350;Height=850;MinWidth=1050;MinHeight=700;Background=new SolidColorBrush(Color.FromRgb(35,36,40));WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new Thickness(20),Background=Background};Content=root;title.Text=Title;title.Foreground=item.NameBrush;DockPanel.SetDock(title,Dock.Top);root.Children.Add(title);
        var controls=new WrapPanel{Margin=new Thickness(0,12,0,12)};DockPanel.SetDock(controls,Dock.Top);root.Children.Add(controls);controls.Children.Add(DesignerWindow.Text("Drop mode  ",13));controls.Children.Add(mode);controls.Children.Add(DesignerWindow.Text("  Chance % per item  ",13));controls.Children.Add(rate);DesignerWindow.Button("Update preview",controls).Click+=(_,_)=>UpdatePlan();
        DockPanel.SetDock(info,Dock.Top);info.Margin=new Thickness(0,0,0,12);root.Children.Add(info);DockPanel.SetDock(feedback,Dock.Bottom);feedback.Margin=new Thickness(0,12,0,0);root.Children.Add(feedback);
        var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);DesignerWindow.Button("Cancel",actions).Click+=(_,_)=>Close();save=DesignerWindow.Button(persist?"Review & save drop locations":"Apply locations to copy draft",actions);save.IsEnabled=false;save.Click+=async(_,_)=>await Save();
        var grid=new Grid{ClipToBounds=true};grid.ColumnDefinitions.Add(selectorColumn);grid.ColumnDefinitions.Add(new());root.Children.Add(grid);bossControls.Margin=new Thickness(0,0,12,0);grid.Children.Add(bossControls);
        void Top(UIElement element){DockPanel.SetDock(element,Dock.Top);bossControls.Children.Add(element);}
        Top(DesignerWindow.Text("1 · Choose instance / raid",13));instance.Margin=new Thickness(0,6,0,10);Top(instance);Top(DesignerWindow.Text("2 · Search and check bosses",13));bossSearch.Margin=new Thickness(0,6,0,10);Top(bossSearch);var hint=DesignerWindow.Text("Selections are retained across instances. Only bosses with verified creature loot tables are listed; shared tables require every affected boss. Selected bosses replace this item's current creature/world drops.",12);hint.Margin=new Thickness(0,0,0,10);Top(hint);
        bosses.Columns.Insert(0,new DataGridCheckBoxColumn{Header="Use",Binding=new System.Windows.Data.Binding("Selected"){Mode=System.Windows.Data.BindingMode.TwoWay,UpdateSourceTrigger=System.Windows.Data.UpdateSourceTrigger.PropertyChanged},Width=55,IsReadOnly=false});bosses.IsReadOnly=false;foreach(var column in bosses.Columns.Skip(1))column.IsReadOnly=true;bossControls.Children.Add(bosses);
        var tabs=new TabControl();Grid.SetColumn(tabs,1);grid.Children.Add(tabs);tabs.Items.Add(new TabItem{Header="Selected drop locations",Content=preview});var existing=new DockPanel();DockPanel.SetDock(currentNote,Dock.Top);currentNote.Margin=new Thickness(0,0,0,10);existing.Children.Add(currentNote);existing.Children.Add(current);tabs.Items.Add(new TabItem{Header="Currently saved drops",Content=existing});
        mode.SelectionChanged+=(_,_)=>{if(changing)return;rate.Text=mode.SelectedIndex==1?"10":"0.01";UpdatePlan();};instance.SelectionChanged+=(_,_)=>FilterBosses();bossSearch.TextChanged+=(_,_)=>FilterBosses();rate.TextChanged+=(_,_)=>{if(!changing)UpdatePlan();};Closing+=(_,e)=>e.Cancel=busy;
        Loaded+=async(_,_)=>{try{busy=true;if(persist){var session=await ItemDropEditing.LoadSession(this.item.Id);this.item=session.Item;expected=session.Expected;title.Text="Drop locations · "+this.item.Name+" · Item "+this.item.Id;title.Foreground=this.item.NameBrush;}this.world??=await WorldDropCatalog.LoadAsync();if(expected is not null){initial=ItemDropEditing.CurrentPlan(this.world,this.item,expected);currentRows=BuildCurrentRows(this.world,expected);current.ItemsSource=currentRows;currentNote.Text=$"{currentRows.Count} direct creature loot rows. Review & save replaces these rows for this item only. Other items in the same loot tables are preserved.";}else currentNote.Text="New copy draft · its source item's drop tables are preserved. These locations apply only to the new duplicate on save.";
            changing=true;foreach(var boss in this.world.Bosses){var row=new BossChoice(boss){Selected=initial?.BossKeys?.Contains(boss.Key)==true};row.PropertyChanged+=(_,_)=>UpdatePlan();choices.Add(row);}instance.ItemsSource=this.world.Bosses.GroupBy(b=>b.MapId).Select(g=>new ContentChoice(g.Key,g.First().Instance)).OrderBy(c=>c.Name).ToList();mode.SelectedIndex=initial?.Mode=="Boss"?1:initial is not null||!persist?0:-1;rate.Text=(initial?.Chance??.01m).ToString(CultureInfo.InvariantCulture);instance.SelectedItem=instance.Items.Cast<ContentChoice>().FirstOrDefault(i=>choices.Any(c=>c.Selected&&c.Boss.MapId==i.Id))??instance.Items.Cast<ContentChoice>().FirstOrDefault();changing=false;FilterBosses();loaded=true;busy=false;UpdatePlan();
        }catch(Exception ex){feedback.Text="Could not load drop locations: "+ex.Message;}finally{busy=false;ready.TrySetResult();}};
    }
    internal static List<CurrentDropRow> BuildCurrentRows(WorldDropCatalog world,ItemSnapshot snapshot)=>snapshot.Tables["world_loot"].Select(r=>{int loot=int.Parse(r["Entry"]!);var bosses=world.Bosses.Where(b=>b.LootId==loot).ToArray();var names=world.Creatures.Where(c=>c.Loot==loot).Select(c=>c.Name).Distinct().ToArray();return new CurrentDropRow(bosses.Length>0?string.Join(", ",bosses.Select(b=>b.Instance).Distinct()):"World / other creature",names.Length>0?string.Join(", ",names):$"Loot table {loot}",loot,decimal.Parse(r["Chance"]!,CultureInfo.InvariantCulture));}).ToList();
    private void FilterBosses(){if(instance.SelectedItem is ContentChoice map){string q=bossSearch.Text.Trim();bosses.ItemsSource=choices.Where(c=>c.Boss.MapId==map.Id&&(q.Length==0||c.Name.Contains(q,StringComparison.OrdinalIgnoreCase)||c.Boss.CreatureId.ToString()==q||c.Loot.ToString()==q)).ToList();}}
    private void UpdatePlan()
    {
        if(changing||world is null||!loaded)return;save.IsEnabled=false;Result=null;bool bossesOnly=mode.SelectedIndex==1;bossControls.Visibility=bossesOnly?Visibility.Visible:Visibility.Collapsed;selectorColumn.Width=bossesOnly?new GridLength(1,GridUnitType.Star):new GridLength(0);
        try{if(mode.SelectedIndex<0)throw new InvalidOperationException("Choose a drop mode, then select an instance and bosses to replace the currently saved creature drops.");if(!decimal.TryParse(rate.Text,NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out decimal chance))throw new InvalidOperationException("Enter a chance using a decimal point.");Result=bossesOnly?BossDrops.Plan(world,item,choices.Where(c=>c.Selected).Select(c=>c.Boss.Key),chance):world.Plan(item,chance);preview.ItemsSource=Result.Targets;info.Text=Result.Summary+"\n"+(bossesOnly?"Boss loot only: selected bosses replace this item's current creature/world drops.":"World drops: automatic eligible creature levels and zones shown below.");feedback.Text=(persist?$"Item {item.Id} · changes require Review & save.":$"Copy draft based on {item.Id} · source loot remains unchanged until the duplicate is created.")+" Server loot-rate multipliers may affect the final rate.";save.IsEnabled=!busy&&Result.Targets.Count>0;}catch(Exception ex){preview.ItemsSource=null;info.Text=ex.Message;}
    }
    private async Task Save(){if(busy||!loaded)return;bosses.CommitEdit();bosses.CommitEdit();UpdatePlan();if(Result is null)return;if(!persist){DialogResult=true;return;}try{if(expected is null)return;string before=currentRows.Count==0?"No direct creature loot rows.":string.Join("\n",currentRows.Select(r=>$"{r.Location} · {r.Source} · {r.Chance}% · Loot table {r.Loot}"));var review=new ContentReviewWindow("item drop locations",item.Name+" · Item "+item.Id,before,Result.Summary+"\n\n"+string.Join("\n",Result.Targets.Select(t=>$"{t.Zones}: {t.Name} · {Result.Chance}%"))){Owner=this};if(review.ShowDialog()!=true)return;busy=true;save.IsEnabled=false;await ItemDropEditing.Save(item,expected,Result);Saved=true;busy=false;DialogResult=true;}catch(Exception ex){feedback.Text=ex.Message;}finally{busy=false;save.IsEnabled=Result is not null;}}
    internal void SelectBossForVerification(string key){changing=true;foreach(var c in choices)c.Selected=c.Boss.Key==key;mode.SelectedIndex=1;rate.Text="10";instance.SelectedItem=instance.Items.Cast<ContentChoice>().First(i=>choices.Any(c=>c.Selected&&c.Boss.MapId==i.Id));changing=false;FilterBosses();UpdatePlan();}
    internal void SearchBossForVerification(string query){bossSearch.Text=query;}
    internal int VisibleBosses=>bosses.Items.Count;
}
