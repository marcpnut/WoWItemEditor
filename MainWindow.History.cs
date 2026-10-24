using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
namespace AnoWowItemEditor;

public partial class MainWindow
{
    private CatalogResult? loadedCatalog;
    private ICollectionView? historyView;
    private bool historyMode;
    private int historyLevel=60;
    private void BindHistory(CatalogResult result)
    {
        string? selected=(HistoryGrid.SelectedItem as OverrideEntry)?.Key;
        loadedCatalog=result;
        historyView=CollectionViewSource.GetDefaultView(result.History.Entries);
        historyView.SortDescriptions.Add(new SortDescription(nameof(OverrideEntry.Name),ListSortDirection.Ascending));
        HistoryGrid.ItemsSource=historyView;ApplyHistoryFilters();
        if(selected is not null)HistoryGrid.SelectedItem=historyView.Cast<OverrideEntry>().FirstOrDefault(e=>e.Key==selected);
        if(HistoryGrid.SelectedItem is null&&!historyView.IsEmpty)HistoryGrid.SelectedIndex=0;
        ShowHistorySelection();
    }
    private void ShowHistoryPage()
    {
        historyMode=true;PageTitle.Text="Change history";
        PageSubtitle.Text="Inspect database overrides and recorded activity. Compare original and modified items.";
        CatalogPage.Visibility=Placeholder.Visibility=Visibility.Collapsed;HistoryPage.Visibility=Visibility.Visible;
        Reload.Visibility=Visibility.Visible;Reload.Content="↻   Refresh history";
        ApplyHistoryFilters();ShowHistorySelection();
    }
    private void SetCatalogStatus(CatalogResult result)
    {
        Status.Inlines.Clear();Status.Inlines.Add(new Run($"● Connected to MariaDB · {CatalogRepository.Database} · {items.Count:N0} items · "));
        var link=new Hyperlink(new Run($"{result.Overrides:N0} active overrides")){Foreground=new SolidColorBrush(Color.FromRgb(112,184,255)),ToolTip="Open Change history"};
        link.Click+=(_,_)=>HistoryNav.IsChecked=true;Status.Inlines.Add(link);
        Status.Inlines.Add(new Run(" · Installed DB2 build 70450"));
    }
    private void HistoryFilterChanged(object sender,RoutedEventArgs e)
    {if(HistoryGrid is not null)ApplyHistoryFilters();}
    private void ApplyHistoryFilters()
    {
        if(historyView is null||loadedCatalog is null)return;
        string? selected=(HistoryGrid.SelectedItem as OverrideEntry)?.Key;
        string query=HistorySearch.Text.Trim();int state=HistoryState.SelectedIndex,activity=HistoryActivity.SelectedIndex;
        historyView.Filter=value=>value is OverrideEntry entry&&(state!=1||entry.Active)&&(state!=2||!entry.Active)&&(activity!=1||entry.Activity.Count>0)&&(activity!=2||entry.Activity.Count==0)
            &&(query.Length==0||entry.Name.Contains(query,StringComparison.OrdinalIgnoreCase)||entry.Id.ToString()==query||entry.Fields.Any(f=>f.Changed&&f.Field.Contains(query,StringComparison.OrdinalIgnoreCase)));
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
        int active=loadedCatalog.History.Entries.Count(e=>e.Active),inactive=loadedCatalog.History.Entries.Count-active;
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
        OriginalHistoryTooltip.Children.Clear();ModifiedHistoryTooltip.Children.Clear();
        if(HistoryGrid.SelectedItem is not OverrideEntry entry) {
            HistoryDetail.Text="No records match these filters.";OriginalHistorySource.Text=ModifiedHistorySource.Text="";
            HistoryFields.ItemsSource=null;HistoryAudit.Text="";return;
        }
        OriginalHistorySource.Text=entry.OriginalSource+$" · Preview level {historyLevel}";
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
    private void AddComparisonLines(StackPanel target,List<TooltipLine> lines,List<TooltipLine> other,string? highlight)
    {
        var texts=other.Select(l=>l.Text).ToHashSet(StringComparer.Ordinal);
        foreach(var line in lines) {
            var element=RenderLine(line);
            if(highlight is not null&&line.Text.Length>0&&!texts.Contains(line.Text)&&element is Grid grid){grid.Background=(Brush)new BrushConverter().ConvertFromString(highlight)!;grid.ToolTip="Different between original and modified versions";}
            target.Children.Add(element);
        }
    }
}
