using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
namespace WoWItemEditor;

public partial class MainWindow
{
    private CatalogResult? loadedCatalog;
    private ICollectionView? historyView;
    private bool historyMode;
    private int historyLevel=60;
    private bool historyActionBusy;
    private void BindHistory(CatalogResult result)
    {
        string? selected=(HistoryGrid.SelectedItem as OverrideEntry)?.Key;
        loadedCatalog=result;
        if(HistoryRarity.ItemsSource is null) {
            HistoryRarity.ItemsSource=new[]{new FilterChoice(-1,"All rarities")}.Concat(CatalogItem.Qualities.Select((name,id)=>new FilterChoice(id,name))).ToList();
            HistorySlot.ItemsSource=new[]{new FilterChoice(-1,"All slots")}.Concat(CatalogItem.Slots.Select(p=>new FilterChoice(p.Key,p.Value))).ToList();
            HistoryType.ItemsSource=new[]{new FilterChoice(-1,"All item types")}.Concat(CatalogItem.Classes.Select(p=>new FilterChoice(p.Key,p.Value))).ToList();
            HistoryRarity.SelectedIndex=HistorySlot.SelectedIndex=HistoryType.SelectedIndex=0;
        }
        ItemKinds.Bind(HistorySubtype,result.Tooltips,(HistoryType.SelectedItem as FilterChoice)?.Id??-1);
        int set=(HistorySet.SelectedItem as FilterChoice)?.Id??-1;
        HistorySet.ItemsSource=new[]{new FilterChoice(-1,"All equipment sets")}.Concat(result.Tooltips.Sets.Values.OrderBy(s=>s.Name).ThenBy(s=>s.Id).Select(s=>new FilterChoice(s.Id,$"{s.Name} · Set {s.Id}"))).ToList();
        HistorySet.SelectedItem=HistorySet.Items.Cast<FilterChoice>().FirstOrDefault(s=>s.Id==set)??HistorySet.Items[0];
        historyView=CollectionViewSource.GetDefaultView(result.History.Entries);
        historyView.SortDescriptions.Add(new SortDescription(nameof(OverrideEntry.Name),ListSortDirection.Ascending));
        HistoryGrid.ItemsSource=historyView;ApplyHistoryFilters();
        if(selected is not null)HistoryGrid.SelectedItem=historyView.Cast<OverrideEntry>().FirstOrDefault(e=>e.Key==selected);
        if(HistoryGrid.SelectedItem is null&&!historyView.IsEmpty)HistoryGrid.SelectedIndex=0;
        ShowHistorySelection();
    }
    private void ShowHistoryPage()
    {
        referenceMode=null;ReferencePage.Visibility=Visibility.Collapsed;historyMode=true;PageTitle.Text="Change history";
        PageSubtitle.Text="Inspect database overrides and recorded activity. Compare original and modified items.";
        CatalogPage.Visibility=Placeholder.Visibility=Visibility.Collapsed;HistoryPage.Visibility=Visibility.Visible;
        Reload.Visibility=Visibility.Visible;Reload.Content="↻   Refresh history";
        ApplyHistoryFilters();ShowHistorySelection();
    }
    private void SetCatalogStatus(CatalogResult result)
    {
        Status.Inlines.Clear();Status.Inlines.Add(new Run($"● Connected to MariaDB · {CatalogRepository.Database} · {items.Count:N0} items · "));
        var link=new Hyperlink(new Run($"{result.History.Entries.Count(e=>e.Active&&e.OriginalItem is not null):N0} active item overrides")){Foreground=new SolidColorBrush(Color.FromRgb(112,184,255)),ToolTip="Open Change history"};
        link.Click+=(_,_)=>HistoryNav.IsChecked=true;Status.Inlines.Add(link);
        Status.Inlines.Add(new Run(" · Installed DB2 build 70450"));
    }
    private void HistoryFilterChanged(object sender,RoutedEventArgs e)
    {if(ReferenceEquals(sender,HistoryType)&&HistorySubtype is not null&&loadedCatalog is not null)ItemKinds.Bind(HistorySubtype,loadedCatalog.Tooltips,(HistoryType.SelectedItem as FilterChoice)?.Id??-1);if(HistoryGrid is not null)ApplyHistoryFilters();}
    private void ApplyHistoryFilters()
    {
        if(historyView is null||loadedCatalog is null)return;
        string? selected=(HistoryGrid.SelectedItem as OverrideEntry)?.Key;
        string query=HistorySearch.Text.Trim();int state=HistoryState.SelectedIndex,activity=HistoryActivity.SelectedIndex;
        int rarity=(HistoryRarity.SelectedItem as FilterChoice)?.Id??-1,slot=(HistorySlot.SelectedItem as FilterChoice)?.Id??-1,type=(HistoryType.SelectedItem as FilterChoice)?.Id??-1,set=(HistorySet.SelectedItem as FilterChoice)?.Id??-1;
        if(!TryLevel(HistoryMinLevel.Text,out int min)||!TryLevel(HistoryMaxLevel.Text,out int max)||HistoryMinLevel.Text.Length>0&&HistoryMaxLevel.Text.Length>0&&min>max){HistoryCount.Text="Enter a valid required-level range. The previous filter remains applied.";return;}
        historyView.Filter=value=>value is OverrideEntry entry&&entry.OriginalItem is not null&&(!entry.Hidden||entry.Active||ShowRemovedHistory.IsChecked==true)&&(state!=1||entry.Active)&&(state!=2||!entry.Active)&&(activity!=1||entry.Activity.Count>0)&&(activity!=2||entry.Activity.Count==0)
            &&ItemKinds.Matches(HistorySubtype,entry.ModifiedItem)&&Matches(entry.ModifiedItem,"",rarity,slot,type,HistoryMinLevel.Text.Length>0?min:null,HistoryMaxLevel.Text.Length>0?max:null)&&(set<0||entry.ModifiedItem.Number("ItemSet")==set||loadedCatalog.Tooltips.Sets.GetValueOrDefault(set)?.Members.Contains(entry.Id)==true)
            &&(CatalogSearch.Item(entry.ModifiedItem,query)||entry.OriginalItem is not null&&CatalogSearch.Item(entry.OriginalItem,query)||CatalogSearch.Matches(query,string.Join(" ",entry.Fields.Select(f=>$"{f.Field} {f.Original} {f.Modified}"))));
        SetHistoryCount();
        var visible=historyView.Cast<OverrideEntry>().ToList();
        var target=visible.FirstOrDefault(e=>e.Key==selected)??visible.FirstOrDefault(e=>e.OriginalItem is not null&&e.Activity.Count>0)??visible.FirstOrDefault();
        historyView.MoveCurrentTo(target);HistoryGrid.SelectedItem=target;
        if(target is not null)HistoryGrid.ScrollIntoView(target);
        ShowHistorySelection();
    }
    private void SetHistoryCount()
    {
        if(loadedCatalog is null||historyView is null)return;
        int active=loadedCatalog.History.Entries.Count(e=>e.OriginalItem is not null&&e.Active),inactive=loadedCatalog.History.Entries.Count(e=>e.OriginalItem is not null&&!e.Active);
        HistoryCount.Text=$"{historyView.Cast<OverrideEntry>().Count():N0} shown · {active:N0} active · {inactive:N0} inactive · Active means a valid latest hotfix registration; it does not establish authorship.";
        if(loadedCatalog.History.Warnings.Count>0)HistoryCount.Text+=" "+string.Join(" ",loadedCatalog.History.Warnings);
    }
    private void ApplyHistoryLevel(object sender,RoutedEventArgs e)
    {
        if(!int.TryParse(HistoryLevel.Text,out int level)||level is <1 or >60){HistoryCount.Text="Enter a preview level from 1 to 60. Both previews retain the previously applied level.";HistoryLevel.BorderBrush=Brushes.IndianRed;return;}
        HistoryLevel.ClearValue(Control.BorderBrushProperty);historyLevel=level;SetHistoryCount();ShowHistorySelection();
    }
    private void HistorySelectionChanged(object sender,RoutedEventArgs e)
    {if(HistoryFields is not null)ShowHistorySelection();}
    private void ShowHistorySelection()
    {
        if(OriginalHistoryTooltip is null||loadedCatalog is null)return;
        RevertButton.IsEnabled=ReapplyButton.IsEnabled=RemoveHistoryButton.IsEnabled=false;
        OriginalHistoryTooltip.Children.Clear();ModifiedHistoryTooltip.Children.Clear();ItemVisualPreview.Update(OriginalHistoryTooltip,Array.Empty<TooltipLine>());ItemVisualPreview.Update(ModifiedHistoryTooltip,Array.Empty<TooltipLine>());
        if(HistoryGrid.SelectedItem is not OverrideEntry entry) {
            HistoryDetail.Text="No records match these filters.";OriginalHistorySource.Text=ModifiedHistorySource.Text="";
            HistoryFields.ItemsSource=null;HistoryAudit.Text="";return;
        }
        OriginalHistorySource.Text=entry.OriginalSource+$" · Preview level {historyLevel}";
        bool native=loadedCatalog.Originals.TryGetValue(entry.Id,out var originalDefinition)&&originalDefinition.Fields.ContainsKey("Display");
        RemoveHistoryButton.IsEnabled=!historyActionBusy&&!entry.Active;
        RemoveHistoryButton.Content=entry.Hidden?"Restore to history":"Remove from history";
        RemoveHistoryButton.ToolTip=entry.Active?"Only inactive entries can be removed.":"Change visibility in history; stored item data and activity remain available.";
        RevertButton.IsEnabled=!historyActionBusy&&entry.Active&&(entry.Managed||native);
        RevertButton.ToolTip=native||entry.Managed?"Review reverting the selected item-field override":"Original client definition is unavailable; a safe revert is not possible.";
        ReapplyButton.IsEnabled=!historyActionBusy&&!entry.Active&&(entry.Managed||entry.ContentOperationId is not null||entry.Source=="Database override");
        ReapplyButton.ToolTip=entry.Managed||entry.Source=="Database override"?"Review activating the retained item-field override":"A complete restorable payload is unavailable for this activity snapshot.";
        ModifiedHistoryTitle.Text=entry.Active?"CURRENT · ACTIVE":"MODIFIED · INACTIVE (RETAINED)";
        ModifiedHistorySource.Text=entry.Active?$"Effective database item · Preview level {historyLevel}":$"Stored item fields for a future reapply; not active. Preview level {historyLevel}. Referenced spells/sets use current tables.";
        HistoryDetail.Text=$"{entry.Name} · ID {entry.Id} · Payload build {entry.Build} · {entry.Status}\n{entry.StateReason} {entry.Summary}. {entry.Attribution}.";
        var original=entry.OriginalItem is null?new List<TooltipLine>():ItemTooltip.Build(entry.OriginalItem,loadedCatalog.OriginalTooltips,loadedCatalog.Originals,historyLevel);
        var modifiedCatalog=loadedCatalog.Items.ToDictionary(i=>i.Id);modifiedCatalog[entry.Id]=entry.ModifiedItem;
        var modified=ItemTooltip.Build(entry.ModifiedItem,loadedCatalog.Tooltips,modifiedCatalog,historyLevel);
        if(entry.OriginalItem is null)OriginalHistoryTooltip.Children.Add(new TextBlock{Text="No original definition is available in the installed DB2 or preserved activity. Original values are unknown.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray});
        else AddComparisonLines(OriginalHistoryTooltip,original,modified,"#203149");
        AddComparisonLines(ModifiedHistoryTooltip,modified,original,entry.OriginalItem is null?null:"#243C2B");
        HistoryFields.ItemsSource=entry.Fields.Where(f=>ChangedFieldsOnly.IsChecked!=true||f.Changed||!f.Known).ToList();
        HistoryAudit.Text=entry.Activity.Count==0?"No recorded app activity exists for this item. A database override alone does not tell us who created it or when.":string.Join("\n\n",entry.Activity.Select(e=>$"Recorded {e.RecordedUtc} · {e.Action}\nActor: {e.Actor}\n{e.Summary}\nProvenance: {e.Provenance}\nChange ID: {e.Id}"));
        OriginalHistoryScroll.ScrollToTop();ModifiedHistoryScroll.ScrollToTop();
    }
    private void ClearHistoryFilters(object sender,RoutedEventArgs e)
    {HistorySearch.Clear();HistoryRarity.SelectedIndex=HistorySlot.SelectedIndex=HistoryType.SelectedIndex=HistorySet.SelectedIndex=0;HistorySubtype.SelectedIndex=0;HistoryMinLevel.Clear();HistoryMaxLevel.Clear();ApplyHistoryFilters();}
    private async void ReviewRevert(object sender,RoutedEventArgs e)=>await ReviewHistoryAction(false);
    private async void ReviewReapply(object sender,RoutedEventArgs e)=>await ReviewHistoryAction(true);
    private async void RemoveHistory(object sender,RoutedEventArgs e)
    {
        if(historyActionBusy||HistoryGrid.SelectedItem is not OverrideEntry entry||entry.Active)return;
        historyActionBusy=true;ShowHistorySelection();Reload.IsEnabled=false;
        try{await HistoryActions.SetHiddenAsync(entry,!entry.Hidden);await LoadCatalog();ApplyHistoryFilters();}
        catch(Exception ex){MessageBox.Show(this,ex.Message,"History could not be updated",MessageBoxButton.OK,MessageBoxImage.Information);}
        finally{historyActionBusy=false;Reload.IsEnabled=true;ShowHistorySelection();}
    }
    private async Task ReviewHistoryAction(bool reapply)
    {
        if(historyActionBusy||loadedCatalog is null||HistoryGrid.SelectedItem is not OverrideEntry entry||entry.Active==reapply)return;
        historyActionBusy=true;ShowHistorySelection();Reload.IsEnabled=false;
        try {
            if(entry.ContentOperationId is not null){
                var op=(await ContentEditing.ListAsync()).Single(o=>o.Id==entry.ContentOperationId);
                var current=System.Text.Json.JsonSerializer.Deserialize<ItemSnapshot>(op.Active?op.After:op.Before)!;var target=System.Text.Json.JsonSerializer.Deserialize<ItemSnapshot>(op.Active?op.Before:op.After)!;
                var confirmation=new ContentReviewWindow("item",entry.Name,ItemEditing.SnapshotSummary(current),ItemEditing.SnapshotSummary(target)){Owner=this};if(confirmation.ShowDialog()!=true)return;
                await ContentEditing.ToggleAsync(op);await LoadCatalog();HistoryCount.Text+=" Saved successfully. Reload the server and verify in game.";return;
            }
            var plan=await HistoryActions.PrepareAsync(entry,loadedCatalog.Originals);
            var review=new HistoryActionReview(plan){Owner=this};
            if(review.ShowDialog()!=true)return;
            await HistoryActions.ExecuteAsync(plan);
            Status.Text=(reapply?"Reapplied":"Reverted")+$" item {entry.Id}. History saved. Reload the server and verify in game.";
            await LoadCatalog();
            HistoryState.SelectedIndex=reapply?1:2;ApplyHistoryFilters();
            HistoryGrid.SelectedItem=historyView!.Cast<OverrideEntry>().FirstOrDefault(row=>row.Key==entry.Key);ShowHistorySelection();
            HistoryCount.Text+=" Saved successfully. Server reload and in-game verification are pending.";
        }catch(Exception ex){MessageBox.Show(this,ex.Message,"Action could not be applied",MessageBoxButton.OK,MessageBoxImage.Information);}
        finally{historyActionBusy=false;Reload.IsEnabled=true;ShowHistorySelection();}
    }
    private void AddComparisonLines(StackPanel target,List<TooltipLine> lines,List<TooltipLine> other,string? highlight)
    {
        ItemVisualPreview.Update(target,lines);var texts=other.Select(l=>l.Text).ToHashSet(StringComparer.Ordinal);
        foreach(var line in lines) {
            var element=RenderLine(line);
            if(highlight is not null&&line.Text.Length>0&&!texts.Contains(line.Text)&&element is Grid grid){grid.Background=(Brush)new BrushConverter().ConvertFromString(highlight)!;grid.ToolTip="Different between original and modified versions";}
            target.Children.Add(element);
        }
    }
}
