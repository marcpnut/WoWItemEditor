using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
namespace AnoWowItemEditor;

internal sealed class SpellPicker : Window
{
    internal ReferenceEntry? Selected {get;private set;}
    internal SpellPicker(ReferenceCatalog catalog,bool procs,int? selectedSpellId=null,int? selectedEffectId=null)
    {
        Title=procs?"Choose a spell or item-effect template":"Choose a set-bonus spell";Width=1180;Height=790;MinWidth=850;MinHeight=600;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(35,36,40));
        var layout=new DockPanel{Margin=new Thickness(20)};Content=layout;
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);layout.Children.Add(top);
        var search=new TextBox{ToolTip="Name, exact spell/effect ID or description"};top.Children.Add(new TextBlock{Text="Search spell name, ID or description",Margin=new Thickness(0,0,0,8)});top.Children.Add(search);
        var filters=new SpellFilters();filters.Bind(catalog.Spells);top.Children.Add(filters);
        var count=new TextBlock{Margin=new Thickness(0,0,0,10)};top.Children.Add(count);
        var bottom=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(bottom,Dock.Bottom);layout.Children.Add(bottom);
        var cancel=new Button{Content="Cancel",IsCancel=true,Margin=new Thickness(0,0,12,0)};cancel.Click+=(_,_)=>DialogResult=false;bottom.Children.Add(cancel);
        var choose=new Button{Content="Use selected spell",IsEnabled=false};bottom.Children.Add(choose);
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(14)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});layout.Children.Add(grid);
        var list=new DataGrid();list.Columns.Add(Column("Name","Name",2));list.Columns.Add(Column("Spell ID","SpellId",1));if(procs)list.Columns.Add(Column("Trigger","Trigger",1));grid.Children.Add(list);
        var detail=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetColumn(detail,2);grid.Children.Add(detail);
        var configured=catalog.Procs.Select(p=>p.SpellId).ToHashSet();
        var pickerEntries=procs?catalog.Procs.Concat(catalog.Spells.Where(s=>!configured.Contains(s.SpellId))).ToList():catalog.Spells;
        var view=new ListCollectionView(pickerEntries);list.ItemsSource=view;
        void Filter(){view.Filter=value=>value is ReferenceEntry entry&&entry.Matches(search.Text.Trim())&&filters.Matches(entry);count.Text=$"{view.Count:N0} matches · selection does not edit spell definitions";if(list.SelectedItem is null||!view.Contains(list.SelectedItem))list.SelectedIndex=view.IsEmpty?-1:0;}
        list.SelectionChanged+=(_,_)=>{Selected=list.SelectedItem as ReferenceEntry;choose.IsEnabled=Selected is not null;detail.Text=Selected?.Details??"No matching spells.";detail.ScrollToHome();};
        search.TextChanged+=(_,_)=>Filter();filters.Changed+=Filter;choose.Click+=(_,_)=>{if(Selected is not null)DialogResult=true;};
        if(selectedSpellId.HasValue)search.Text=selectedSpellId.Value.ToString();else Filter();
        var selected=view.Cast<ReferenceEntry>().FirstOrDefault(e=>e.SpellId==selectedSpellId&&(!selectedEffectId.HasValue||e.EffectId==selectedEffectId))??view.Cast<ReferenceEntry>().FirstOrDefault(e=>e.SpellId==selectedSpellId);
        if(selected is not null){list.SelectedItem=selected;Loaded+=(_,_)=>list.ScrollIntoView(selected);}
    }
    internal static DataGridTextColumn Column(string title,string property,double width=1)
    {
        var style=new Style(typeof(TextBlock));style.Setters.Add(new Setter(TextBlock.ForegroundProperty,Brushes.LightGray));style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis));return new(){Header=title,Binding=new Binding(property),Width=new DataGridLength(width,DataGridLengthUnitType.Star),ElementStyle=style};
    }
}
