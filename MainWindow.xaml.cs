using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace AnoWowItemEditor;

public partial class MainWindow : Window
{
    private List<CatalogItem> items = [];
    private ICollectionView? view;
    private readonly bool verify;
    private readonly DispatcherTimer filterTimer = new() { Interval=TimeSpan.FromMilliseconds(180) };
    public MainWindow(bool verify=false)
    {
        this.verify=verify;
        InitializeComponent();
        Rarity.ItemsSource = new[] { new FilterChoice(-1,"All rarities") }.Concat(CatalogItem.Qualities.Select((name,id)=>new FilterChoice(id,name)));
        Slot.ItemsSource = new[] { new FilterChoice(-1,"All slots") }.Concat(CatalogItem.Slots.Select(p=>new FilterChoice(p.Key,p.Value)));
        ItemType.ItemsSource = new[] { new FilterChoice(-1,"All item types") }.Concat(CatalogItem.Classes.Select(p=>new FilterChoice(p.Key,p.Value)));
        Rarity.SelectedIndex=Slot.SelectedIndex=ItemType.SelectedIndex=0;
        filterTimer.Tick += (_,_)=> { filterTimer.Stop(); ApplyFilters(); };
        Loaded += async (_,_)=>await LoadCatalog();
    }
    private async Task LoadCatalog()
    {
        Reload.IsEnabled=false;
        Status.Text="Connecting to MariaDB and reading the server item catalog…";
        try
        {
            var result=await new CatalogRepository().LoadAsync();
            items=result.Items;
            view=CollectionViewSource.GetDefaultView(items);
            ItemsGrid.ItemsSource=view;
            ApplyFilters();
            Status.Text=$"● Connected to MariaDB · {CatalogRepository.Database} · {items.Count:N0} items · {result.Overrides:N0} active overrides · Server build 70450";
            if(ItemsGrid.SelectedItem is null && !view.IsEmpty) ItemsGrid.SelectedIndex=0;
            if(verify) await VerifyAndRender(result);
        }
        catch(Exception ex)
        {
            Status.Text=$"Could not refresh items: {ex.Message}";
            if(items.Count==0) { ResultCount.Text="Catalog unavailable"; EmptyMessage.Text="Unable to load items. See the connection message below, then retry."; EmptyMessage.Visibility=Visibility.Visible; }
            if(verify) { Directory.CreateDirectory("artifacts"); await File.WriteAllTextAsync("artifacts/verification-error.txt",ex.ToString()); Environment.ExitCode=1; Close(); }
        }
        finally { Reload.IsEnabled=true; }
    }
    private void FilterChanged(object sender,RoutedEventArgs e)
    {
        if(view is null) return;
        filterTimer.Stop(); filterTimer.Start();
    }
    private void ApplyFilters()
    {
        if(view is null) return;
        string query=Search.Text.Trim();
        int rarity=(Rarity.SelectedItem as FilterChoice)?.Id??-1, slot=(Slot.SelectedItem as FilterChoice)?.Id??-1, type=(ItemType.SelectedItem as FilterChoice)?.Id??-1;
        bool minValid=TryLevel(MinLevel.Text,out int min),maxValid=TryLevel(MaxLevel.Text,out int max);
        string? error=!minValid||!maxValid ? "Enter whole, non-negative levels." : MinLevel.Text.Length>0&&MaxLevel.Text.Length>0&&min>max ? "Minimum level must not exceed maximum level." : null;
        FilterError.Text=error??""; FilterError.Visibility=error is null ? Visibility.Collapsed : Visibility.Visible;
        if(error is not null) return;
        view.Filter=value=>value is CatalogItem item && Matches(item,query,rarity,slot,type,MinLevel.Text.Length>0?min:null,MaxLevel.Text.Length>0?max:null);
        int count=view.Cast<CatalogItem>().Count();
        ResultCount.Text=$"{count:N0} of {items.Count:N0} items";
        EmptyMessage.Text="No items match these filters."; EmptyMessage.Visibility=count==0 ? Visibility.Visible : Visibility.Collapsed;
        if(ItemsGrid.SelectedItem is CatalogItem selected&&!view.Contains(selected)) ItemsGrid.SelectedItem=null;
        if(count==0) ShowTooltip(null);
    }
    internal static bool Matches(CatalogItem item,string query,int rarity,int slot,int type,int? min,int? max) =>
        (query.Length==0||item.Name.Contains(query,StringComparison.OrdinalIgnoreCase)||item.Id.ToString(CultureInfo.InvariantCulture)==query)
        && (rarity<0||item.Quality==rarity)&&(slot<0||item.Slot==slot)&&(type<0||item.ClassId==type)
        && (!min.HasValue||item.RequiredLevel>=min)&&(!max.HasValue||item.RequiredLevel<=max);
    private static bool TryLevel(string text,out int level) { level=0; return string.IsNullOrWhiteSpace(text)||int.TryParse(text,out level)&&level>=0; }
    private void ClearClick(object sender,RoutedEventArgs e)
    { Search.Clear(); MinLevel.Clear(); MaxLevel.Clear(); Rarity.SelectedIndex=Slot.SelectedIndex=ItemType.SelectedIndex=0; filterTimer.Stop(); ApplyFilters(); }
    private async void RefreshClick(object sender,RoutedEventArgs e) => await LoadCatalog();
    private void Navigate(object sender,RoutedEventArgs e)
    {
        if(CatalogPage is null) return;
        string page=(string)((RadioButton)sender).Content;
        bool isItem=page=="Item catalog";
        PageTitle.Text=page; PageSubtitle.Text=isItem ? "Explore your server’s items. Select a row to inspect its details." : "A home for your server’s "+(page=="Spell catalog" ? "spells." : "item procs.");
        CatalogPage.Visibility=isItem?Visibility.Visible:Visibility.Collapsed;
        Placeholder.Visibility=isItem?Visibility.Collapsed:Visibility.Visible;
        Reload.Visibility=isItem?Visibility.Visible:Visibility.Collapsed;
    }
    private void SelectItem(object sender,SelectionChangedEventArgs e) => ShowTooltip(ItemsGrid.SelectedItem as CatalogItem);
    private void ShowTooltip(CatalogItem? item)
    {
        TooltipPanel.Children.Clear(); SourceLabel.Text="";
        void Line(string text,string color="#FFFFFF",double size=13,bool bold=false) => TooltipPanel.Children.Add(new TextBlock { Text=text, Foreground=(Brush)new BrushConverter().ConvertFromString(color)!, FontSize=size, FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,0,0,5) });
        if(item is null) { Line("Select an item","#70B8FF",18); Line("Its tooltip will appear here.","#9299A5"); return; }
        var title=new TextBlock { Text=item.Name, Foreground=item.NameBrush,FontSize=20, FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,9) }; TooltipPanel.Children.Add(title);
        Line(item.QualityName,"#9D9D9D",12);
        Line($"Item Level {item.ItemLevel}","#FFD100");
        string binding=item.Number("Bonding") switch { 1=>"Binds when picked up",2=>"Binds when equipped",3=>"Binds when used",4 or 5=>"Quest item",_=>"" }; if(binding.Length>0) Line(binding);
        if(item.Slot>0) Line(item.SlotName);
        Line(item.TypeName,"#B7BDC8",12);
        if(item.Number("MaxCount")==1) Line("Unique");
        if(item.Number("ContainerSlots")>0) Line($"{item.Number("ContainerSlots")} Slot Bag");
        var stats=new Dictionary<int,string> { [0]="Mana",[1]="Health",[3]="Agility",[4]="Strength",[5]="Intellect",[6]="Spirit",[7]="Stamina",[12]="Defense rating",[13]="Dodge rating",[14]="Parry rating",[31]="Hit rating",[32]="Critical strike rating",[36]="Haste rating",[38]="Attack power",[40]="Versatility",[45]="Spell power",[49]="Mastery" };
        bool hasAllocation=false;
        for(int i=1;i<=10;i++)
        { int stat=item.Number("StatModifierBonusStat"+i),value=item.Number("StatPercentEditor"+i); if(stat<0||value<=0) continue; if(!hasAllocation) Line(" ",size:4); hasAllocation=true; Line($"{stats.GetValueOrDefault(stat)??$"Stat {stat}"}: allocation {value:N0}","#1EFF00"); }
        if(hasAllocation) Line("Stat allocations shown; combat amounts require client scaling.","#9299A5",11);
        if(item.RequiredLevel>0) Line($"Requires Level {item.RequiredLevel}");
        if(item.Number("ItemSet")>0) Line($"Item set: {item.Number("ItemSet")}","#FFD100");
        if(item.Text("Description").Length>0) Line("“"+item.Text("Description")+"”","#FFD100");
        long.TryParse(item.Text("SellPrice"),out long price); if(price>0) Line($"Sell Price: {price/10000}g {price/100%100}s {price%100}c","#DBDEE1",12);
        SourceLabel.Text=$"Item ID {item.Id}\n{item.Source}";
    }
    private async Task VerifyAndRender(CatalogResult result)
    {
        void Check(bool ok,string message) { if(!ok) throw new InvalidOperationException("Verification failed: "+message); }
        Check(result.Items.Count>1000,"Full real catalog loaded");
        Check(result.Items.Select(i=>i.Id).Distinct().Count()==result.Items.Count,"One effective row per item ID");
        Check(result.Items.Count(i=>i.Overridden)==result.Overrides,"Active MariaDB overrides applied");
        var sample=items.FirstOrDefault(i=>i.Id==22476)??items[0];
        Check(Matches(sample,sample.Id.ToString(),-1,-1,-1,null,null),"ID search");
        Check(Matches(sample,sample.Name.ToUpperInvariant(),sample.Quality,sample.Slot,sample.ClassId,sample.RequiredLevel,sample.RequiredLevel),"Combined filters and case-insensitive search");
        Check(!Matches(sample,"",-1,-1,-1,sample.RequiredLevel+1,null),"Level exclusion");
        Search.Text="Bonescythe"; ApplyFilters();
        ItemsGrid.SelectedItem=sample; ItemsGrid.ScrollIntoView(sample);
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        UpdateLayout();
        Directory.CreateDirectory("artifacts");
        var content=(FrameworkElement)Content;
        var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32); bitmap.Render(content);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using(var file=File.Create("artifacts/catalog-preview.png")) encoder.Save(file);
        ClearClick(this,new RoutedEventArgs()); Search.Text="__no_item_matches_83729__"; ApplyFilters(); Check(view!.IsEmpty&&EmptyMessage.Visibility==Visibility.Visible,"Empty search state");
        MinLevel.Text="-1"; ApplyFilters(); Check(FilterError.Visibility==Visibility.Visible,"Invalid level feedback");
        ClearClick(this,new RoutedEventArgs());
        await File.WriteAllTextAsync("artifacts/verification.json",JsonSerializer.Serialize(new { items=items.Count,activeOverrides=result.Overrides,sample=new { sample.Id,sample.Name,sample.RequiredLevel,sample.Source },checks="Catalog, merge uniqueness, source counts, search, combined filters, level range, empty state, invalid range, WPF render passed" },new JsonSerializerOptions { WriteIndented=true }));
        Close();
    }
}
