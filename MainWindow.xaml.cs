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
using System.Windows.Documents;
using System.Text.RegularExpressions;
namespace AnoWowItemEditor;

public partial class MainWindow : Window
{
    private List<CatalogItem> items = [];
    private ICollectionView? view;
    private TooltipData tooltipData = new();
    private Dictionary<int,CatalogItem> catalogById = [];
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
            tooltipData=result.Tooltips;
            catalogById=items.ToDictionary(i=>i.Id);
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
        foreach(var line in ItemTooltip.Build(item,tooltipData,catalogById)) TooltipPanel.Children.Add(RenderLine(line));
        SourceLabel.Text=$"Item ID {item.Id} · {item.Source}";
        PreviewScroll.ScrollToTop();
    }
    private static FrameworkElement RenderLine(TooltipLine line)
    {
        TextBlock Text(string value) {
            var block=new TextBlock { Foreground=(Brush)new BrushConverter().ConvertFromString(line.Color)!, FontSize=line.Size, FontWeight=line.Bold?FontWeights.SemiBold:FontWeights.Normal, TextWrapping=TextWrapping.Wrap };
            foreach(string part in Regex.Split(value,@"(\[[^\]]+\])"))
                block.Inlines.Add(new Run(part) { Foreground=part.StartsWith('[')?new SolidColorBrush(Color.FromRgb(112,184,255)):block.Foreground });
            return block;
        }
        var grid=new Grid { Margin=new Thickness(line.Indent,0,0,line.Text.Length==0?4:3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
        grid.Children.Add(Text(line.Text));
        if(line.Right is not null) { grid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); var right=Text(line.Right);right.Margin=new Thickness(10,0,0,0);Grid.SetColumn(right,1);grid.Children.Add(right); }
        return grid;
    }
    private async Task VerifyAndRender(CatalogResult result)
    {
        void Check(bool ok,string message) { if(!ok) throw new InvalidOperationException("Verification failed: "+message); }
        Check(result.Items.Count>1000,"Full real catalog loaded");
        Check(result.Items.Select(i=>i.Id).Distinct().Count()==result.Items.Count,"One effective row per item ID");
        Check(result.Items.Count(i=>i.Overridden)==result.Overrides,"Active MariaDB overrides applied");
        var sample=items.First(i=>i.Id==22416);
        var dreadnaught=tooltipData.Sets[sample.Number("ItemSet")];
        Check(dreadnaught.Members.Length==9&&dreadnaught.Members.Contains(23059),"Dreadnaught includes nine pieces and its ring");
        Check(dreadnaught.Bonuses.Select(b=>b.Pieces).SequenceEqual(new[]{2,4,6,8}),"Dreadnaught bonus thresholds");
        var tooltip=ItemTooltip.Build(sample,tooltipData,catalogById);
        string tooltipText=string.Join("\n",tooltip.Select(l=>l.Text+(l.Right is null?"":"    "+l.Right)));
        Check(tooltipText.Contains("+[scales] Strength")&&tooltipText.Contains("[scales] Armor")&&!tooltipText.Contains("allocation"),"Scaled stats are marked rather than invented");
        Check(tooltipText.Contains("Dreadnaught's Battlegear (0/9)")&&tooltipText.Contains("Ring of the Dreadnaught"),"Set membership and inactive count rendered");
        Check(tooltipText.Contains("Revenge ability by 75.")&&tooltipText.Contains("Shield Slam by 5%."),"Fixed spell amounts resolved from this build");
        Check(!tooltipText.Contains('$'),"No raw spell tokens in Dreadnaught tooltip");
        var synthetic=new TooltipData();
        synthetic.Effects[(1,0)]=new() { ["EffectBasePointsF"]="75",["Coefficient"]="1" };
        Check(SpellText.Resolve("Increases by $s1.",1,synthetic)=="Increases by [scales].","Scaling effect is not mistaken for a fixed value");
        synthetic.Effects[(2,0)]=new() { ["EffectBasePointsF"]="160" };
        Check(SpellText.Resolve("Healing by $2s1.",1,synthetic)=="Healing by 160.","Referenced spell effect resolved");
        Check(SpellText.Resolve("Value $9s2.",1,synthetic).Contains("[value unavailable]"),"Missing spell data remains explicit");
        Check(tooltip.Where(l=>l.Text.StartsWith("(2) Set:")||l.Text.StartsWith("(8) Set:")).All(l=>l.Color==ItemTooltip.Gray),"Set bonuses are inactive without character equipment");
        var ring=ItemTooltip.Build(catalogById[23059],tooltipData,catalogById);
        Check(!ring.Any(l=>l.Text.EndsWith(" Armor")||l.Text.StartsWith("Durability")),"Ring does not gain invented armor or durability");
        foreach(var entry in items)Check(ItemTooltip.Build(entry,tooltipData,catalogById).Count>0,$"Tooltip renders for item {entry.Id}");
        Check(Matches(sample,sample.Id.ToString(),-1,-1,-1,null,null),"ID search");
        Check(Matches(sample,sample.Name.ToUpperInvariant(),sample.Quality,sample.Slot,sample.ClassId,sample.RequiredLevel,sample.RequiredLevel),"Combined filters and case-insensitive search");
        Check(!Matches(sample,"",-1,-1,-1,sample.RequiredLevel+1,null),"Level exclusion");
        Search.Text="Dreadnaught"; ApplyFilters();
        ItemsGrid.SelectedItem=sample; ItemsGrid.ScrollIntoView(sample);
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        UpdateLayout();
        Directory.CreateDirectory("artifacts");
        await File.WriteAllTextAsync("artifacts/dreadnaught-tooltip.txt",tooltipText);
        var fullTooltip=new StackPanel();foreach(var line in tooltip)fullTooltip.Children.Add(RenderLine(line));
        var fullBorder=new Border { Background=new SolidColorBrush(Color.FromRgb(16,17,27)),BorderBrush=new SolidColorBrush(Color.FromRgb(97,103,129)),BorderThickness=new Thickness(1),Padding=new Thickness(20),CornerRadius=new CornerRadius(6),Child=fullTooltip,Width=390 };
        fullBorder.Measure(new Size(390,double.PositiveInfinity));fullBorder.Arrange(new Rect(new Point(),fullBorder.DesiredSize));fullBorder.UpdateLayout();
        var fullBitmap=new RenderTargetBitmap(390,(int)Math.Ceiling(fullBorder.ActualHeight),96,96,PixelFormats.Pbgra32);fullBitmap.Render(fullBorder);
        var fullEncoder=new PngBitmapEncoder();fullEncoder.Frames.Add(BitmapFrame.Create(fullBitmap));using(var output=File.Create("artifacts/dreadnaught-tooltip.png"))fullEncoder.Save(output);
        var content=(FrameworkElement)Content;
        var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32); bitmap.Render(content);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using(var file=File.Create("artifacts/catalog-preview.png")) encoder.Save(file);
        ClearClick(this,new RoutedEventArgs()); Search.Text="__no_item_matches_83729__"; ApplyFilters(); Check(view!.IsEmpty&&EmptyMessage.Visibility==Visibility.Visible,"Empty search state");
        MinLevel.Text="-1"; ApplyFilters(); Check(FilterError.Visibility==Visibility.Visible,"Invalid level feedback");
        ClearClick(this,new RoutedEventArgs());
        await File.WriteAllTextAsync("artifacts/verification.json",JsonSerializer.Serialize(new { items=items.Count,activeOverrides=result.Overrides,sample=new { sample.Id,sample.Name,sample.RequiredLevel,sample.Source },set=new { dreadnaught.Name,pieces=dreadnaught.Members.Length,bonuses=dreadnaught.Bonuses.Select(b=>new{b.Pieces,b.SpellId,text=tooltipData.SpellDescription(b.SpellId)}) },checks="Catalog, filters, validation, nine-piece Dreadnaught set, bonus thresholds, fixed and referenced spell amounts, scaling markers, missing effect fallback, inactive bonuses, WPF renders passed" },new JsonSerializerOptions { WriteIndented=true }));
        Close();
    }
}
