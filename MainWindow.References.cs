using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
namespace AnoWowItemEditor;

public partial class MainWindow
{
    private ReferenceCatalog? references;
    private Task<ReferenceCatalog>? referenceLoad;
    private string? referenceMode;
    private int referenceGeneration;
    private TextBox referenceSearch=null!,referenceDetails=null!;
    private TextBlock referenceCount=null!;
    private DataGrid referenceGrid=null!;
    private ICollectionView? referenceView;
    private SpellFilters referenceFilters=null!;
    private void BuildReferencePage()
    {
        var layout=new DockPanel();ReferencePage.Children.Add(layout);
        var top=new StackPanel{Margin=new Thickness(0,0,0,16)};DockPanel.SetDock(top,Dock.Top);layout.Children.Add(top);
        top.Children.Add(new TextBlock{Text="Search all fields · names, IDs, descriptions, effects, auras and triggers",Foreground=Brushes.LightGray,Margin=new Thickness(0,0,0,8)});
        referenceSearch=new TextBox();referenceSearch.TextChanged+=(_,_)=>FilterReferences();top.Children.Add(referenceSearch);
        referenceFilters=new SpellFilters();referenceFilters.Changed+=FilterReferences;top.Children.Add(referenceFilters);
        var clear=new Button{Content="Clear search & filters",HorizontalAlignment=HorizontalAlignment.Left};clear.Click+=(_,_)=>{referenceSearch.Clear();referenceFilters.Reset();};top.Children.Add(clear);
        referenceCount=new TextBlock{Margin=new Thickness(0,10,0,0),Foreground=Brushes.LightGray};top.Children.Add(referenceCount);
        var body=new Grid();body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});body.ColumnDefinitions.Add(new(){Width=new GridLength(14)});body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});layout.Children.Add(body);
        referenceGrid=new DataGrid{IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false};referenceGrid.SelectionChanged+=(_,_)=>ShowReferenceSelection();body.Children.Add(referenceGrid);
        var textStyle=new Style(typeof(TextBlock));textStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty,Brushes.LightGray));textStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis));
        foreach(var column in new[]{("Name","Name",2d),("Spell ID","SpellId",.65),("Effect ID","EffectLabel",.65),("Trigger","Trigger",1d)})referenceGrid.Columns.Add(new DataGridTextColumn{Header=column.Item1,Binding=new Binding(column.Item2),Width=new DataGridLength(column.Item3,DataGridLengthUnitType.Star),ElementStyle=textStyle});
        var splitter=new GridSplitter{Width=5,HorizontalAlignment=HorizontalAlignment.Center,Background=Brushes.DimGray,ResizeDirection=GridResizeDirection.Columns,ResizeBehavior=GridResizeBehavior.PreviousAndNext};Grid.SetColumn(splitter,1);body.Children.Add(splitter);
        var detail=new DockPanel();Grid.SetColumn(detail,2);body.Children.Add(detail);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(actions,Dock.Top);detail.Children.Add(actions);
        foreach(bool effect in new[]{false,true}){var copy=new Button{Content=effect?"Copy effect ID":"Copy spell ID",Margin=new Thickness(0,0,10,0)};copy.Click+=(_,_)=>{if(referenceGrid.SelectedItem is ReferenceEntry entry){string? id=effect?entry.EffectId?.ToString():entry.SpellId.ToString();if(id is not null)Clipboard.SetText(id);}};actions.Children.Add(copy);}
        var editBonus=new Button{Content="Edit in a set…",ToolTip="Edit values for a set that uses the selected spell"};editBonus.Click+=async(_,_)=>{if(referenceGrid.SelectedItem is ReferenceEntry entry)await OpenSpellSetEditor(entry);};actions.Children.Add(editBonus);
        referenceDetails=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(16)};detail.Children.Add(referenceDetails);
    }
    private async Task ShowReferencePage(string page,bool refresh=false)
    {
        if(referenceSearch is null)BuildReferencePage();
        if(referenceMode!=page){referenceSearch!.Clear();referenceFilters.Reset();}
        referenceMode=page;historyMode=characterMode=false;HistoryPage.Visibility=CatalogPage.Visibility=Placeholder.Visibility=Visibility.Collapsed;ReferencePage.Visibility=Visibility.Visible;
        PageTitle.Text=page;PageSubtitle.Text=page=="Spell catalog"?"All spells, including set bonuses. Select a spell to inspect it or edit a set that uses it.":"Configured item effects and spells with proc rules. Use the Spell catalog for all other spells.";
        Reload.Visibility=Visibility.Visible;Reload.Content="↻   Refresh references";
        try{
            if(refresh){references=null;referenceLoad=null;referenceGeneration++;}
            if(references is null){referenceCount.Text="Loading installed spell tables and active MariaDB hotfixes…";referenceView=null;referenceGrid.ItemsSource=null;referenceDetails.Clear();int generation=referenceGeneration;referenceLoad??=ReferenceCatalog.LoadAsync();var loaded=await referenceLoad;if(generation!=referenceGeneration)return;references=loaded;}
            if(referenceMode!=page)return;
            referenceFilters.Bind(page=="Spell catalog"?references.Spells:references.Procs);referenceView=CollectionViewSource.GetDefaultView(page=="Spell catalog"?references.Spells:references.Procs);referenceGrid.ItemsSource=referenceView;FilterReferences();
        }catch(Exception ex){referenceLoad=null;if(referenceMode==page)referenceCount.Text="Could not load references: "+ex.Message;}
    }
    private void FilterReferences()
    {
        if(referenceView is null)return;string query=referenceSearch.Text.Trim();bool exactId=int.TryParse(query,out int id)&&id>0;
        referenceView.Filter=value=>value is ReferenceEntry entry&&(exactId&&(entry.SpellId==id||referenceMode=="Proc catalog"&&entry.EffectId==id)||entry.Matches(query)&&referenceFilters.Matches(entry));
        referenceCount.Text=$"{referenceView.Cast<ReferenceEntry>().Count():N0} shown · "+(exactId?"Direct ID matches ignore filters · ":"")+"Installed DB2 + active MariaDB hotfixes";
        if(referenceGrid.SelectedItem is null||!referenceView.Contains(referenceGrid.SelectedItem))referenceGrid.SelectedIndex=referenceView.IsEmpty?-1:0;ShowReferenceSelection();
    }
    private void ShowReferenceSelection(){referenceDetails.Text=referenceGrid.SelectedItem is ReferenceEntry entry?$"{entry.Name}\n{entry.Kind} · {entry.Trigger}\n\n{entry.Details}":"No matching records.";referenceDetails.ScrollToHome();}
}
