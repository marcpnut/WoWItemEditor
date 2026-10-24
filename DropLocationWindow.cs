using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
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
internal sealed class DropLocationWindow : Window
{
    private readonly CatalogItem item;
    private WorldDropCatalog? world;
    private readonly bool persist;
    private ItemSnapshot? expected;
    private readonly ComboBox mode=new(){Width=220,ItemsSource=new[]{"World drops","Instance bosses only"},SelectedIndex=0},instance=new(){Width=350,DisplayMemberPath="Label"};
    private readonly TextBox rate=new(){Text="0.01",Width=85};
    private readonly DataGrid bosses=DesignerWindow.Table(("Boss","Name"),("Difficulty","Difficulty"),("Min","Min"),("Max","Max"),("Loot table","Loot")),preview=DesignerWindow.Table(("Creature / boss","Name"),("Level","Min"),("Maximum","Max"),("Location","Zones"));
    private readonly TextBlock info=DesignerWindow.Text("Loading drop sources…",13),feedback=DesignerWindow.Text("",12);
    private readonly StackPanel bossControls=new();
    private readonly Button save=new();
    private readonly List<BossChoice> choices=[];
    private readonly TaskCompletionSource ready=new();
    private bool changing,busy;
    internal DropPlan? Result {get;private set;}
    internal bool Saved {get;private set;}
    internal Task Ready=>ready.Task;
    internal DropLocationWindow(CatalogItem item,DropPlan? initial=null,WorldDropCatalog? world=null,bool persist=false)
    {
        this.item=item;this.world=world;this.persist=persist;Title="Drop location · "+item.Name;Width=1280;Height=820;MinWidth=1000;MinHeight=700;Background=new SolidColorBrush(Color.FromRgb(35,36,40));WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new Thickness(20),Background=Background};Content=root;var title=DesignerWindow.Text(Title,22);title.Foreground=Brushes.LightSkyBlue;DockPanel.SetDock(title,Dock.Top);root.Children.Add(title);var controls=new WrapPanel{Margin=new Thickness(0,12,0,12)};DockPanel.SetDock(controls,Dock.Top);root.Children.Add(controls);controls.Children.Add(mode);controls.Children.Add(DesignerWindow.Text("  Drop chance % per item  ",13));controls.Children.Add(rate);var update=DesignerWindow.Button("Preview selected locations",controls);update.Click+=(_,_)=>UpdatePlan();
        DockPanel.SetDock(info,Dock.Top);info.Margin=new Thickness(0,0,0,12);root.Children.Add(info);DockPanel.SetDock(feedback,Dock.Bottom);feedback.Margin=new Thickness(0,12,0,0);root.Children.Add(feedback);var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);var cancel=DesignerWindow.Button("Cancel",actions);cancel.Click+=(_,_)=>Close();save=DesignerWindow.Button(persist?"Review & save drop locations":"Use locations for this copy",actions);save.IsEnabled=false;save.Click+=async(_,_)=>await Save();
        var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new());root.Children.Add(grid);bossControls.Margin=new Thickness(0,0,12,0);grid.Children.Add(bossControls);bossControls.Children.Add(DesignerWindow.Text("Instance / raid",12));instance.Margin=new Thickness(0,6,0,10);bossControls.Children.Add(instance);bossControls.Children.Add(DesignerWindow.Text("Check bosses to receive this item. Choices are kept when switching instances. Shared loot tables require every affected boss to be selected. Existing drops for other items are preserved.",12));bosses.Height=420;bosses.Margin=new Thickness(0,10,0,0);bosses.Columns.Insert(0,new DataGridCheckBoxColumn{Header="Use",Binding=new System.Windows.Data.Binding("Selected"){Mode=System.Windows.Data.BindingMode.TwoWay,UpdateSourceTrigger=System.Windows.Data.UpdateSourceTrigger.PropertyChanged},Width=55,IsReadOnly=false});bosses.IsReadOnly=false;foreach(var col in bosses.Columns.Skip(1))col.IsReadOnly=true;bossControls.Children.Add(bosses);Grid.SetColumn(preview,1);grid.Children.Add(preview);
        mode.SelectionChanged+=(_,_)=>{if(changing)return;rate.Text=mode.SelectedIndex==1?"10":"0.01";UpdatePlan();};instance.SelectionChanged+=(_,_)=>FilterBosses();rate.TextChanged+=(_,_)=>{if(!changing)UpdatePlan();};Closing+=(_,e)=>e.Cancel=busy;
        Loaded+=async(_,_)=>{try{busy=true;if(persist){expected=await ItemDropEditing.Load(item.Id);initial=expected.Tables["world_drop_policy"].FirstOrDefault() is {} policy?JsonSerializer.Deserialize<DropPlan>(policy["PlanJson"]!):initial;}this.world??=await WorldDropCatalog.LoadAsync();changing=true;foreach(var boss in this.world.Bosses){var row=new BossChoice(boss){Selected=initial?.BossKeys?.Contains(boss.Key)==true};row.PropertyChanged+=(_,_)=>UpdatePlan();choices.Add(row);}instance.ItemsSource=this.world.Bosses.GroupBy(b=>b.MapId).Select(g=>new ContentChoice(g.Key,g.First().Instance)).OrderBy(c=>c.Name).ToList();mode.SelectedIndex=initial?.Mode=="Boss"?1:0;rate.Text=(initial?.Chance??.01m).ToString(CultureInfo.InvariantCulture);instance.SelectedItem=instance.Items.Cast<ContentChoice>().FirstOrDefault(i=>choices.Any(c=>c.Selected&&c.Boss.MapId==i.Id))??instance.Items.Cast<ContentChoice>().FirstOrDefault();changing=false;FilterBosses();busy=false;UpdatePlan();}catch(Exception ex){feedback.Text=ex.Message;}finally{busy=false;ready.TrySetResult();}};
    }
    private void FilterBosses(){if(instance.SelectedItem is ContentChoice map)bosses.ItemsSource=choices.Where(c=>c.Boss.MapId==map.Id).ToList();}
    private void UpdatePlan()
    {
        if(changing||world is null)return;save.IsEnabled=false;Result=null;bossControls.Visibility=mode.SelectedIndex==1?Visibility.Visible:Visibility.Collapsed;
        try{if(!decimal.TryParse(rate.Text,NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out decimal chance))throw new InvalidOperationException("Enter a chance using a decimal point.");Result=mode.SelectedIndex==1?BossDrops.Plan(world,item,choices.Where(c=>c.Selected).Select(c=>c.Boss.Key),chance):world.Plan(item,chance);preview.ItemsSource=Result.Targets;info.Text=Result.Summary+"\n"+(Result.Mode=="Boss"?"These selected bosses replace the item's previous creature/world drops. Other items in their loot tables are preserved.":"World drops use the displayed automatic level/zone rules.");feedback.Text="Draft only · server loot-rate multipliers may affect the final rate.";save.IsEnabled=!busy&&Result.Targets.Count>0;}catch(Exception ex){preview.ItemsSource=null;info.Text=ex.Message;}
    }
    private async Task Save(){if(busy)return;bosses.CommitEdit();bosses.CommitEdit();UpdatePlan();if(Result is null)return;if(!persist){DialogResult=true;return;}try{if(expected is null)return;var review=new ContentReviewWindow("item drop locations",item.Name,$"Existing creature loot rows: {expected.Tables["world_loot"].Count}. They will be replaced for this item only.",Result.Summary+"\n\n"+string.Join("\n",Result.Targets.Select(t=>$"{t.Zones}: {t.Name} · {Result.Chance}%"))){Owner=this};if(review.ShowDialog()!=true)return;busy=true;save.IsEnabled=false;await ItemDropEditing.Save(item,expected,Result);Saved=true;busy=false;DialogResult=true;}catch(Exception ex){feedback.Text=ex.Message;}finally{busy=false;save.IsEnabled=Result is not null;}}
    internal void SelectBossForVerification(string key){changing=true;foreach(var c in choices)c.Selected=c.Boss.Key==key;mode.SelectedIndex=1;rate.Text="10";instance.SelectedItem=instance.Items.Cast<ContentChoice>().First(i=>choices.Any(c=>c.Selected&&c.Boss.MapId==i.Id));changing=false;FilterBosses();UpdatePlan();}
}
