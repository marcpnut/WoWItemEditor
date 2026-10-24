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
    private List<CharacterInfo> characters=[];
    private CharacterSnapshot? characterSnapshot;
    private List<CharacterItemRow> characterItems=[];
    private bool characterMode;
    private int characterRequest;
    private int previewLevel=60;
    private bool settingCharacterList;
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
            BindHistory(result);
            if(characterSnapshot is not null)characterItems=CharacterRows(characterSnapshot);
            int previousSet=(SetFilter.SelectedItem as FilterChoice)?.Id??-1;
            var sets=new[]{new FilterChoice(-1,"All equipment sets")}.Concat(tooltipData.Sets.Values.Where(s=>s.Members.Length>0).OrderBy(s=>s.Name).ThenBy(s=>s.Id).Select(s=>new FilterChoice(s.Id,$"{s.Name} · Set {s.Id} · {s.Members.Length} pieces"))).ToList();
            SetFilter.ItemsSource=sets;SetFilter.SelectedItem=sets.FirstOrDefault(s=>s.Id==previousSet)??sets[0];
            BindItems();
            SetCatalogStatus(result);
            if(ItemsGrid.SelectedItem is null && view?.IsEmpty==false) ItemsGrid.SelectedIndex=0;
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
        int set=(SetFilter.SelectedItem as FilterChoice)?.Id??-1;
        var members=set>=0&&tooltipData.Sets.TryGetValue(set,out var selectedSet)?selectedSet.Members.ToHashSet():null;
        view.Filter=value=> {
            var item=value as CatalogItem??(value as CharacterItemRow)?.Item;
            if(item is null)return false;
            if(characterMode&&value is CharacterItemRow row) {
                int storage=StorageFilter.SelectedIndex;
                if(storage==1&&!row.Instance.Equipped)return false;
                if(storage==2&&(row.Instance.Equipped||row.Instance.Slot<0||row.Location.StartsWith("Void")))return false;
                if(storage==3&&!row.Location.StartsWith("Mail"))return false;
                if(storage==4&&!row.Location.StartsWith("Void"))return false;
            }
            return (set<0||members?.Contains(item.Id)==true)&&Matches(item,query,rarity,slot,type,MinLevel.Text.Length>0?min:null,MaxLevel.Text.Length>0?max:null);
        };
        int count=view.Cast<object>().Count();
        ResultCount.Text=$"{count:N0} of {(characterMode?characterItems.Count:items.Count):N0} "+(characterMode?"item instances":"items");
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
    { Search.Clear(); MinLevel.Clear(); MaxLevel.Clear(); Rarity.SelectedIndex=Slot.SelectedIndex=ItemType.SelectedIndex=0; SetFilter.SelectedIndex=0; filterTimer.Stop(); ApplyFilters(); }
    private void OpenSet(int id)
    {
        ItemNav.IsChecked=true;
        ClearClick(this,new RoutedEventArgs());
        SetFilter.SelectedItem=SetFilter.Items.Cast<FilterChoice>().FirstOrDefault(s=>s.Id==id);
        filterTimer.Stop();ApplyFilters();
        if(!view!.IsEmpty)ItemsGrid.SelectedIndex=0;
    }
    private void OpenItem(int id)
    {
        if(!catalogById.TryGetValue(id,out var item)){Status.Text=$"Item {id} is not available in this catalog snapshot.";return;}
        ItemNav.IsChecked=true;
        var set=SetFilter.SelectedItem as FilterChoice;
        ClearClick(this,new RoutedEventArgs());
        if(set is not null&&tooltipData.Sets.TryGetValue(set.Id,out var info)&&info.Members.Contains(id))SetFilter.SelectedItem=set;
        filterTimer.Stop();ApplyFilters();ItemsGrid.SelectedItem=item;ItemsGrid.ScrollIntoView(item);
    }
    private async void RefreshClick(object sender,RoutedEventArgs e)
    {
        if(referenceMode is not null)await ShowReferencePage(referenceMode,true);
        else if(historyMode)await LoadCatalog();
        else if(characterMode){Reload.IsEnabled=false;try{await LoadCharacters();}finally{Reload.IsEnabled=true;}}
        else await LoadCatalog();
    }
    private async void Navigate(object sender,RoutedEventArgs e)
    {
        if(CatalogPage is null) return;
        string page=(string)((RadioButton)sender).Content;
        if(page is "Spell catalog" or "Proc catalog"){await ShowReferencePage(page);return;}
        referenceMode=null;ReferencePage.Visibility=Visibility.Collapsed;
        if(page=="Change history"){ShowHistoryPage();return;}
        historyMode=false;HistoryPage.Visibility=Visibility.Collapsed;
        bool wasCharacter=characterMode;
        characterMode=page=="Characters";
        if(wasCharacter!=characterMode&&view is not null){ClearClick(this,new RoutedEventArgs());StorageFilter.SelectedIndex=0;}
        bool isItem=page=="Item catalog"||characterMode;
        PageTitle.Text=page; PageSubtitle.Text=characterMode?"Browse saved equipment, bags, bank, mail, and void storage. Change the preview level to compare.":isItem ? "Explore your server’s items. Select a row to inspect its details." : "A home for your server’s "+(page=="Spell catalog" ? "spells." : "item procs.");
        CatalogPage.Visibility=isItem?Visibility.Visible:Visibility.Collapsed;
        Placeholder.Visibility=isItem?Visibility.Collapsed:Visibility.Visible;
        Reload.Visibility=isItem?Visibility.Visible:Visibility.Collapsed;
        Reload.Content=characterMode?"↻   Refresh character":"↻   Refresh items";
        AdvancedFilters.IsExpanded=!characterMode;
        CharacterControls.Visibility=LocationColumn.Visibility=CountColumn.Visibility=characterMode?Visibility.Visible:Visibility.Collapsed;
        if(characterMode) {
            BindItems();
            if(characters.Count==0)await LoadCharacters();
            else CharacterSummary.Text=characterSnapshot is null?"Select a saved character.":$"{characterSnapshot.Character.Name} · saved level {characterSnapshot.Character.Level} · {characterItems.Count} instances";
        } else if(isItem){CharacterSummary.Text="Preview only · no character or item changes are saved";BindItems();}
    }
    private void BindItems()
    {
        view=CollectionViewSource.GetDefaultView(characterMode?(object)characterItems:items);
        ItemsGrid.ItemsSource=view;ApplyFilters();
        if(!view.IsEmpty){
            ItemsGrid.SelectedIndex=0;
            var first=ItemsGrid.SelectedItem;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=> {
                if(!ReferenceEquals(ItemsGrid.SelectedItem,first))return;
                ItemsGrid.UpdateLayout();ItemsGrid.ScrollIntoView(first);
                FindScroll(ItemsGrid)?.ScrollToTop();
            }));
        }else ShowTooltip(null);
    }
    private static ScrollViewer? FindScroll(DependencyObject element)
    {
        for(int n=0;n<VisualTreeHelper.GetChildrenCount(element);n++) {
            var child=VisualTreeHelper.GetChild(element,n);
            if(child is ScrollViewer scroll)return scroll;
            if(FindScroll(child) is {} nested)return nested;
        }
        return null;
    }
    private async Task LoadCharacters()
    {
        try {
            ulong? selected=(CharacterPicker.SelectedItem as CharacterInfo)?.Guid;
            characters=await CharacterRepository.ListAsync();
            settingCharacterList=true;
            CharacterPicker.ItemsSource=characters;
            if(characters.Count>0)CharacterPicker.SelectedItem=characters.FirstOrDefault(c=>c.Guid==selected)??characters[0];
            else {characterRequest++;characterSnapshot=null;characterItems=[];CharacterSummary.Text="No saved characters found.";if(characterMode)BindItems();}
            settingCharacterList=false;
            if(characters.Count>0)await LoadSelectedCharacter();
        }catch(Exception ex){CharacterSummary.Text="Could not load characters: "+ex.Message;}
        finally{settingCharacterList=false;}
    }
    private void CharacterSearchChanged(object sender,TextChangedEventArgs e)
    {
        if(CharacterPicker is null)return;
        var selected=CharacterPicker.SelectedItem;
        CharacterPicker.ItemsSource=characters.Where(c=>c.Name.Contains(CharacterSearch.Text.Trim(),StringComparison.OrdinalIgnoreCase)).ToList();
        if(selected is CharacterInfo info&&((IEnumerable<CharacterInfo>)CharacterPicker.ItemsSource).Contains(info))CharacterPicker.SelectedItem=selected;
    }
    private async void CharacterChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!settingCharacterList)await LoadSelectedCharacter();
    }
    private async Task LoadSelectedCharacter()
    {
        int request=++characterRequest;
        if(CharacterPicker.SelectedItem is not CharacterInfo selected){characterSnapshot=null;characterItems=[];CharacterSummary.Text="Select a saved character.";if(characterMode)BindItems();return;}
        CharacterSummary.Text="Loading saved character items…";
        characterSnapshot=null;characterItems=[];if(characterMode)BindItems();
        try {
            var snapshot=await CharacterRepository.LoadAsync(selected);
            if(request!=characterRequest)return;
            characterSnapshot=snapshot;
            characterItems=CharacterRows(snapshot);
            PreviewLevel.Text=selected.Level.ToString();
            ApplyPreviewLevel(this,new RoutedEventArgs());
            int unknown=characterItems.Count(i=>!i.Item.Fields.ContainsKey("Display"));
            CharacterSummary.Text=$"{selected.Name} · saved level {selected.Level} · {characterItems.Count} instances"+(unknown>0?$" · {unknown} missing definitions":"")+(selected.Online?" · Online: unsaved changes may be missing":"");
            if(characterMode)BindItems();
        }catch(Exception ex){if(request==characterRequest)CharacterSummary.Text="Could not load character: "+ex.Message;}
    }
    private List<CharacterItemRow> CharacterRows(CharacterSnapshot snapshot)=>snapshot.Items.Select(i=>new CharacterItemRow(i,catalogById.GetValueOrDefault(i.Entry)??new CatalogItem(new() { ["ID"]=i.Entry.ToString() }))).ToList();
    private void PreviewLevelChanged(object sender,TextChangedEventArgs e)
    {
        if(PreviewLevel is null||ApplyLevelButton is null||LevelFeedback is null)return;
        bool valid=int.TryParse(PreviewLevel.Text,out int level)&&level is >=1 and <=60;
        ApplyLevelButton.IsEnabled=valid;
        if(!valid){PreviewLevel.BorderBrush=Brushes.IndianRed;LevelFeedback.Text=$"Enter a level from 1 to 60. Preview remains at level {previewLevel}.";return;}
        PreviewLevel.ClearValue(Control.BorderBrushProperty);
        LevelFeedback.Text=level==previewLevel?$"Applied level {previewLevel} · click Apply level to recalculate.":$"Level {level} is pending · click Apply level. Current preview: level {previewLevel}.";
    }
    private void ApplyPreviewLevel(object sender,RoutedEventArgs e)
    {
        if(!int.TryParse(PreviewLevel.Text,out int level)||level is <1 or >60){PreviewLevelChanged(this,new TextChangedEventArgs(TextBox.TextChangedEvent,UndoAction.None));return;}
        previewLevel=level;
        ShowTooltip(SelectedItem());
    }
    private void PreviewLevelKeyDown(object sender,System.Windows.Input.KeyEventArgs e)
    {
        if(e.Key==System.Windows.Input.Key.Enter){ApplyPreviewLevel(sender,new RoutedEventArgs());e.Handled=true;}
    }
    private void ResetPreviewLevel(object sender,RoutedEventArgs e)
    {
        PreviewLevel.Text=(characterMode&&characterSnapshot is not null?characterSnapshot.Character.Level:60).ToString();
        ApplyPreviewLevel(sender,e);
    }
    private CatalogItem? SelectedItem()=>ItemsGrid.SelectedItem as CatalogItem??(ItemsGrid.SelectedItem as CharacterItemRow)?.Item;
    private void SelectItem(object sender,SelectionChangedEventArgs e) => ShowTooltip(SelectedItem());
    private void ShowTooltip(CatalogItem? item)
    {
        TooltipPanel.Children.Clear(); SourceLabel.Text="";
        void Line(string text,string color="#FFFFFF",double size=13,bool bold=false) => TooltipPanel.Children.Add(new TextBlock { Text=text, Foreground=(Brush)new BrushConverter().ConvertFromString(color)!, FontSize=size, FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,0,0,5) });
        if(item is null) { Line("Select an item","#70B8FF",18); Line("Its tooltip will appear here.","#9299A5"); if(int.TryParse(PreviewLevel.Text,out int level)&&level==previewLevel)LevelFeedback.Text=$"Applied level {previewLevel} · select an item to see calculated values.";return; }
        var instance=characterMode?(ItemsGrid.SelectedItem as CharacterItemRow)?.Instance:null;
        foreach(var line in ItemTooltip.Build(item,tooltipData,catalogById,previewLevel,instance,characterMode?characterSnapshot:null)) TooltipPanel.Children.Add(RenderLine(line));
        var calculated=tooltipData.Calculations.Evaluate(item,previewLevel,instance);
        if(int.TryParse(PreviewLevel.Text,out int entered)&&entered==previewLevel)
            LevelFeedback.Text=$"Applied level {previewLevel} · preview recalculated. "+(calculated.ItemLevel is null?"Some values remain unresolved; see the item details.":item.Number("PlayerLevelToItemLevelCurveID")==0?"This item has a fixed item level, so its base stats stay the same.":instance?.FixedLevel>0?$"This instance uses saved scaling level {instance.FixedLevel}.":"Calculated values use this level and the item’s scaling curve.");
        SourceLabel.Text=$"Item ID {item.Id} · {item.Source}\n{calculated.Note}"+(instance is null?"":$"\nInstance {instance.Guid} · {instance.Location}");
        PreviewLegend.Text="Server-table base values at preview level "+previewLevel+". [scales] = unresolved rule; [client value] = unavailable. "+(characterMode?"Set highlighting reflects saved equipment. Enchants shown separately; no buffs or talents simulated.":"No character equipped.");
        PreviewScroll.ScrollToTop();
    }
    private FrameworkElement RenderLine(TooltipLine line)
    {
        TextBlock Text(string value) {
            var block=new TextBlock { Foreground=(Brush)new BrushConverter().ConvertFromString(line.Color)!, FontSize=line.Size, FontWeight=line.Bold?FontWeights.SemiBold:FontWeights.Normal, TextWrapping=TextWrapping.Wrap };
            if(line.TargetItem is not null||line.TargetSet is not null) {
                var link=new Hyperlink(new Run(value)) { Foreground=block.Foreground,TextDecorations=null,ToolTip=line.TargetItem is int id?$"View item {id}":"Browse all pieces in this set" };
                link.Click+=(_,_)=> { if(line.TargetItem is int item)OpenItem(item);else if(line.TargetSet is int set)OpenSet(set); };
                block.Inlines.Add(link);return block;
            }
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
        if(File.Exists("artifacts/verification-error.txt"))File.Delete("artifacts/verification-error.txt");
        void Check(bool ok,string message) { if(!ok) throw new InvalidOperationException("Verification failed: "+message); }
        Check(result.Items.Count>1000,"Full real catalog loaded");
        Check(result.Items.Select(i=>i.Id).Distinct().Count()==result.Items.Count,"One effective row per item ID");
        Check(result.Items.Count(i=>i.Overridden)==result.Overrides,"Active MariaDB overrides applied");
        Check(catalogById[13009].Name=="Cow King's Hide"&&catalogById[14662].Name=="Keeper's Hooves","Definitions with different payload/registration builds are recovered");
        Check(catalogById[17776].ClassId==4&&catalogById[13009].ClassId==4,"Native item identities are preserved for SQL-provided sparse definitions");
        var sample=result.Originals[22416];
        var dreadnaught=result.OriginalTooltips.Sets[sample.Number("ItemSet")];
        Check(dreadnaught.Members.Length==9&&dreadnaught.Members.Contains(23059),"Dreadnaught includes nine pieces and its ring");
        Check(dreadnaught.Bonuses.Select(b=>b.Pieces).SequenceEqual(new[]{2,4,6,8}),"Dreadnaught bonus thresholds");
        var tooltip=ItemTooltip.Build(sample,result.OriginalTooltips,result.Originals);
        string tooltipText=string.Join("\n",tooltip.Select(l=>l.Text+(l.Right is null?"":"    "+l.Right)));
        Check(tooltipText.Contains("+21 Strength")&&tooltipText.Contains("+43 Stamina")&&!tooltipText.Contains("allocation"),"Dreadnaught stats are calculated from local budget tables");
        Check(tooltipData.Calculations.Evaluate(sample,1).Stats[1]==tooltipData.Calculations.Evaluate(sample,60).Stats[1],"Fixed-level Dreadnaught is not artificially rescaled by character level");
        Check(ItemCalculations.PropertyIndex(5,4)==1&&ItemCalculations.PropertyIndex(11,0)==3&&ItemCalculations.PropertyIndex(26,19)==4,"Slot-specific property budget selection");
        Check(ItemCalculations.Round(1.5f)==2&&ItemCalculations.Round(1.49f)==1,"Tooltip stat rounding");
        Check(ItemCalculations.LinearCurve([(1,10),(60,128)],30)==68&&ItemCalculations.LinearCurve([(1,10),(60,128)],0)==10&&ItemCalculations.LinearCurve([(1,10),(60,128)],90)==128,"Level curve interpolation and endpoint clamping");
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
        OpenSet(523);Check(view!.Cast<CatalogItem>().Count()==9,"Set filter includes all nine pieces");
        OpenItem(23059);Check(ItemsGrid.SelectedItem is CatalogItem selectedRing&&selectedRing.Id==23059&&view!.Cast<CatalogItem>().Count()==9,"Member navigation preserves set browsing");
        Check(tooltip.Count(l=>l.TargetItem.HasValue)==9&&tooltip.Any(l=>l.TargetSet==523),"Set and all members are navigable");
        OpenItem(22416);
        ItemsGrid.SelectedItem=catalogById[sample.Id]; ItemsGrid.ScrollIntoView(catalogById[sample.Id]);
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
        // Real read-only character loading, plus isolated equipment-state and level-preview checks.
        var savedCharacters=await CharacterRepository.ListAsync();
        int testedInstances=0;
        if(savedCharacters.Count>0) {
            var snapshot=await CharacterRepository.LoadAsync(savedCharacters[0]);testedInstances=snapshot.Items.Count;
            Check(snapshot.Items.All(i=>i.Count>0),"Owned stacks retain positive quantities");
            Check(snapshot.Items.Where(i=>i.Equipped).All(i=>i.Bag==0&&i.Slot is >=0 and <19),"Only equipped root slots count toward set bonuses");
            characters=savedCharacters;
            settingCharacterList=true;CharacterPicker.ItemsSource=characters;CharacterPicker.SelectedIndex=0;settingCharacterList=false;
            await LoadSelectedCharacter();
            CharacterNav.IsChecked=true;
            ClearClick(this,new RoutedEventArgs());ApplyFilters();
            Check(view!.Cast<object>().Count()==snapshot.Items.Count,"Character table preserves every saved instance");
            int storedLevel=snapshot.Character.Level;
            int originalPreview=previewLevel;
            PreviewLevel.Text=originalPreview==1?"2":"1";Check(previewLevel==originalPreview&&LevelFeedback.Text.Contains("pending"),"Editing a level keeps the current preview until applied");
            ApplyLevelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(previewLevel==int.Parse(PreviewLevel.Text)&&characterSnapshot!.Character.Level==storedLevel&&LevelFeedback.Text.Contains("recalculated"),"Apply button recalculates without changing saved character level");
            PreviewLevel.Text="60";ApplyPreviewLevel(this,new RoutedEventArgs());Check(previewLevel==60,"Applied preview can return to level 60");
            PreviewLevel.Text="0";ApplyPreviewLevel(this,new RoutedEventArgs());Check(previewLevel==60&&!ApplyLevelButton.IsEnabled,"Invalid preview level cannot be applied");
            ResetPreviewLevel(this,new RoutedEventArgs());Check(previewLevel==storedLevel,"Reset applies the saved character level");
            UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            var characterBitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);characterBitmap.Render(content);
            var characterEncoder=new PngBitmapEncoder();characterEncoder.Frames.Add(BitmapFrame.Create(characterBitmap));using(var file=File.Create("artifacts/character-preview.png"))characterEncoder.Save(file);
            ItemNav.IsChecked=true;
        }
        var fakeEquipped=dreadnaught.Members.Take(2).Select((id,n)=>new OwnedItem((ulong)n+1,id,1,0,n,"Equipped",100,"","",0,0,true)).ToList();
        var fakeCharacter=new CharacterSnapshot(new CharacterInfo(0,"Preview test",60,1,false,0),fakeEquipped);
        var equippedTooltip=ItemTooltip.Build(sample,result.OriginalTooltips,result.Originals,60,null,fakeCharacter);
        Check(equippedTooltip.Any(l=>l.Text.Contains("(2/9)"))&&equippedTooltip.Any(l=>l.Text.StartsWith("(2) Set:")&&l.Color==ItemTooltip.Green)&&equippedTooltip.Any(l=>l.Text.StartsWith("(4) Set:")&&l.Color==ItemTooltip.Gray),"Equipped piece count and bonus thresholds");
        var unresolved=fakeEquipped[0] with {BonusLists="99999"};
        Check(tooltipData.Calculations.Evaluate(sample,60,unresolved).Stats.Count==0,"Unsupported instance bonuses cannot masquerade as exact stats");
        await File.WriteAllTextAsync("artifacts/character-verification.json",JsonSerializer.Serialize(new{characters=savedCharacters.Count,testedInstances,checks="Read-only inventory, equipment classification, preview-level validation, unchanged saved level, equipped set bonuses, unsupported-instance fallback"},new JsonSerializerOptions{WriteIndented=true}));
        Check(result.History.Entries.Count(e=>e.Active)==result.Overrides,"All database overrides are ingested before hiding server-only definitions from History");
        var bones=result.History.Entries.First(e=>e.Active&&e.OriginalItem?.RequiredLevel==60&&e.ModifiedItem.RequiredLevel==1);
        Check(bones.OriginalItem?.RequiredLevel==60&&bones.ModifiedItem.RequiredLevel==1,"Original snapshot is isolated from live changes");
        Check(bones.Fields.Any(f=>f.Field=="RequiredLevel"&&f.Original=="60"&&f.Modified=="1"&&f.Changed),"Known field difference reports the real before/after values");
        Check(bones.Activity.Count>0&&bones.Activity.All(e=>e.BeforeJson.Length>0&&e.AfterJson.Length>0),"Existing before/after activity retained for future revert/reapply");
        var testTable=new HotfixTable([new() { ["ID"]="1",["VerifiedBuild"]="70009" }],new() { [1]=new(10,1,1,70291) });
        Check(testTable.ActiveRows.ContainsKey(1),"Registration build is not a payload join key");
        var removed=new HotfixTable(testTable.Rows,new() { [1]=new(11,1,2,70450) });Check(removed.ActiveRows.Count==0,"Removed records are not active");
        var invalid=new HotfixTable(testTable.Rows,new() { [1]=new(12,1,3,70450) });Check(invalid.ActiveRows.Count==0,"Non-valid registrations are not active");
        Check(!OverrideEntry.Compare(null,bones.ModifiedItem).Any(f=>f.Changed||f.Known),"Missing originals are not invented as zero-valued items");
        Check(OverrideEntry.Equivalent("0.625","0.6250"),"Numeric formatting is not treated as a data change");
        Check(HotfixHistory.SnapshotItem("{\"OriginalItem\":{\"ID\":\"2\",\"Display\":\"Other item\"},\"Tables\":{}}",1,true) is null,"Another item's snapshot is not presented as this item's original");
        HistoryNav.IsChecked=true;HistoryState.SelectedIndex=1;ApplyHistoryFilters();
        Check(historyView!.Cast<OverrideEntry>().All(e=>e.Active),"Active history filter");
        HistoryActivity.SelectedIndex=1;ApplyHistoryFilters();Check(historyView!.Cast<OverrideEntry>().All(e=>e.Activity.Count>0),"Recorded-activity filter");
        HistoryActivity.SelectedIndex=2;ApplyHistoryFilters();Check(historyView!.Cast<OverrideEntry>().All(e=>e.Activity.Count==0),"Unattributed-record filter");HistoryActivity.SelectedIndex=0;ApplyHistoryFilters();
        HistoryGrid.SelectedItem=bones;HistoryGrid.ScrollIntoView(bones);ShowHistorySelection();
        HistoryLevel.Text="1";ApplyHistoryLevel(this,new RoutedEventArgs());Check(historyLevel==1&&OriginalHistorySource.Text.Contains("level 1")&&ModifiedHistorySource.Text.Contains("level 1")&&HistoryGrid.SelectedItem is OverrideEntry same&&same.Id==bones.Id,"Both history previews use the same level without changing selection");
        HistoryLevel.Text="0";ApplyHistoryLevel(this,new RoutedEventArgs());Check(historyLevel==1,"Invalid history level retains prior preview");HistoryLevel.Text="60";ApplyHistoryLevel(this,new RoutedEventArgs());
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();
        var historyBitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);historyBitmap.Render(content);
        var historyEncoder=new PngBitmapEncoder();historyEncoder.Frames.Add(BitmapFrame.Create(historyBitmap));using(var file=File.Create("artifacts/history-preview.png"))historyEncoder.Save(file);
        HistoryState.SelectedIndex=2;ApplyHistoryFilters();Check(historyView!.Cast<OverrideEntry>().All(e=>!e.Active),"Inactive history filter");
        if(!historyView!.IsEmpty){
            HistoryGrid.SelectedIndex=0;ShowHistorySelection();Check(ModifiedHistoryTitle.Text.Contains("INACTIVE")&&ModifiedHistorySource.Text.Contains("not active"),"Inactive preview is explicitly retained, not live");
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();
            var inactiveBitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);inactiveBitmap.Render(content);
            var inactiveEncoder=new PngBitmapEncoder();inactiveEncoder.Frames.Add(BitmapFrame.Create(inactiveBitmap));using(var file=File.Create("artifacts/history-inactive-preview.png"))inactiveEncoder.Save(file);
        }
        HistorySearch.Text="__missing_history_search__";ApplyHistoryFilters();Check(historyView.IsEmpty&&HistoryGrid.SelectedItem is null,"Empty history search clears stale comparisons");HistorySearch.Clear();
        await File.WriteAllTextAsync("artifacts/history-verification.json",JsonSerializer.Serialize(new{active=result.Overrides,inactive=result.History.Entries.Count(e=>!e.Active),rows=result.History.Entries.Count,recordedItems=result.History.Entries.Count(e=>e.Activity.Count>0),recovered=new[]{catalogById[13009].Name,catalogById[14662].Name},checks="Immutable originals, native identity recovery, cross-build registrations, active/inactive filters, retained inactive payload, synchronized preview level, no fabricated original, recorded activity retained, read-only WPF comparison"},new JsonSerializerOptions{WriteIndented=true}));
        await File.WriteAllTextAsync("artifacts/verification.json",JsonSerializer.Serialize(new { items=items.Count,activeOverrides=result.Overrides,sample=new { sample.Id,sample.Name,sample.RequiredLevel,sample.Source,calculated=tooltipData.Calculations.Evaluate(sample,60) },set=new { dreadnaught.Name,pieces=dreadnaught.Members.Length,bonuses=dreadnaught.Bonuses.Select(b=>new{b.Pieces,b.SpellId,text=tooltipData.SpellDescription(b.SpellId)}) },checks="Catalog, set filtering, clickable navigation, stat/armor calculation, fixed item levels, curve interpolation, character loading, instance fallback, equipment set counts, WPF renders passed" },new JsonSerializerOptions { WriteIndented=true }));
        await VerifyNewFeatures(result);
        Close();
    }
}
