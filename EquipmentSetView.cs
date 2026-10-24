using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
namespace AnoWowItemEditor;

internal sealed record EquipmentViewItem(object Key,CatalogItem Item,int? Slot=null,bool Active=true);

internal sealed class EquipmentSetView : Grid
{
    private readonly Grid board=new(){Width=526,Height=560};
    private readonly Viewbox mannequin=new(){Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top};
    private readonly StackPanel tooltip=new();
    private readonly Button drops=new(){Content="Edit drop locations…",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(8,0,0,8),IsEnabled=false};
    private readonly TextBlock heading=DesignerWindow.Text("SELECT A SLOT",18),note=DesignerWindow.Text("Select an item to inspect it. Double-click to edit.",12);
    private readonly Expander extraPanel=new(){IsExpanded=true};
    private readonly ListBox overflow=new(){DisplayMemberPath="Item.Name",MaxHeight=110};
    private readonly Dictionary<int,Border> frames=[];
    private readonly Dictionary<int,EquipmentViewItem> equipped=[];
    private readonly Dictionary<int,StackPanel> labels=[];
    private readonly Dictionary<int,System.Windows.Shapes.Path> symbols=[];
    private List<EquipmentViewItem> entries=[];
    private TooltipData? data;
    private IReadOnlyDictionary<int,CatalogItem> lookup=new Dictionary<int,CatalogItem>();
    private object? selectedKey;
    private int level=60;
    private CharacterSnapshot? character;
    internal void HideEditingActions(){readOnly=true;drops.Visibility=Visibility.Collapsed;note.Text="Simulated equipment · select a slot to inspect. No character data is changed.";}
    private bool binding,readOnly;
    internal EquipmentViewItem? Selection {get;private set;}
    internal IReadOnlyDictionary<int,EquipmentViewItem> Equipped=>equipped;
    internal IReadOnlyList<EquipmentViewItem> Overflow {get;private set;}=[];
    internal event Action<EquipmentViewItem?>? SelectionChanged;
    internal event Action<EquipmentViewItem>? EditRequested;
    internal event Action<EquipmentViewItem>? DropRequested;
    internal void RequestSelectedDrops(){if(!readOnly&&Selection is {} row)DropRequested?.Invoke(row);}
    internal event Action<int>? EmptySlotSelected;
    internal EquipmentSetView()
    {
        ColumnDefinitions.Add(new(){Width=new GridLength(550)});ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        var left=new DockPanel{Margin=new Thickness(0,0,10,0)};Children.Add(left);note.Margin=new Thickness(10,0,0,8);DockPanel.SetDock(note,Dock.Top);left.Children.Add(note);
        var extras=extraPanel;extras.Header="Additional pieces / slot conflicts";extras.Content=overflow;extras.Margin=new Thickness(10,6,0,0);DockPanel.SetDock(extras,Dock.Bottom);left.Children.Add(extras);overflow.SelectionChanged+=(_,_)=>{if(!binding&&overflow.SelectedItem is EquipmentViewItem row)Select(row);};overflow.MouseDoubleClick+=(_,_)=>{if(!readOnly&&overflow.SelectedItem is EquipmentViewItem row)EditRequested?.Invoke(row);};
        mannequin.Child=board;left.Children.Add(mannequin);
        Background=new SolidColorBrush(Color.FromRgb(35,36,40));
        var silhouette=new System.Windows.Shapes.Path{Data=Geometry.Parse("M251,48 C225,48 223,99 241,108 L239,126 212,135 190,177 181,267 193,273 209,191 220,176 219,264 208,294 219,350 226,431 217,454 248,454 251,346 260,303 270,346 276,454 307,454 298,431 305,350 315,294 300,264 299,176 312,191 329,273 341,267 332,177 310,135 281,126 279,108 C297,99 297,48 269,48 Z"),Fill=new LinearGradientBrush(Color.FromRgb(63,74,91),Color.FromRgb(23,29,40),90),Stroke=new SolidColorBrush(Color.FromRgb(95,111,133)),StrokeThickness=1.5};Place(silhouette,0,0);
        var caption=DesignerWindow.Text("SET EQUIPMENT",11);caption.Foreground=Brushes.SlateGray;Place(caption,214,470);
        int[] leftSlots=[0,1,2,14,4,3,18,8],rightSlots=[9,5,6,7,10,11,12,13];
        for(int n=0;n<8;n++){AddSlot(leftSlots[n],8,n*58,false);AddSlot(rightSlots[n],348,n*58,true);}
        AddSlot(15,8,493,false);AddSlot(16,181,493,false);AddSlot(17,354,493,true);
        var pane=new DockPanel{Margin=new Thickness(6,0,0,0)};SetColumn(pane,1);Children.Add(pane);DockPanel.SetDock(heading,Dock.Top);heading.Foreground=Brushes.LightSkyBlue;heading.Margin=new Thickness(8,0,0,8);pane.Children.Add(heading);DockPanel.SetDock(drops,Dock.Top);pane.Children.Add(drops);drops.Click+=(_,_)=>RequestSelectedDrops();pane.Children.Add(DesignerWindow.Preview(tooltip,"ITEM TOOLTIP"));
        TooltipView.Fill(tooltip,[new("Select an occupied equipment slot.",ItemTooltip.Gray)]);
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        mannequin.MaxHeight=double.IsInfinity(availableSize.Height)?560:Math.Max(0,availableSize.Height-38-(extraPanel.Visibility==Visibility.Visible?30+(extraPanel.IsExpanded?Math.Min(110,Overflow.Count*30+8):0):0));
        return base.MeasureOverride(availableSize);
    }
    private void Place(FrameworkElement element,double x,double y){element.HorizontalAlignment=HorizontalAlignment.Left;element.VerticalAlignment=VerticalAlignment.Top;element.Margin=new Thickness(x,y,0,0);board.Children.Add(element);}
    private void AddSlot(int slot,double x,double y,bool right)
    {
        var card=new Border{Width=164,Height=53,Background=new SolidColorBrush(Color.FromRgb(24,27,34)),BorderBrush=new SolidColorBrush(Color.FromRgb(65,69,79)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(3),Cursor=Cursors.Hand};frames[slot]=card;Place(card,x,y);
        var row=new DockPanel{Margin=new Thickness(4)};card.Child=row;var icon=new Border{Width=39,Height=41,Background=new SolidColorBrush(Color.FromRgb(15,18,24)),Margin=new Thickness(right?7:0,0,right?0:7,0)};DockPanel.SetDock(icon,right?Dock.Right:Dock.Left);row.Children.Add(icon);var symbol=new System.Windows.Shapes.Path{Data=Geometry.Parse(Symbol(slot)),Width=30,Height=32,Stretch=Stretch.Uniform,Fill=new SolidColorBrush(Color.FromRgb(31,38,48)),Stroke=Brushes.SlateGray,StrokeThickness=1.5,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};symbols[slot]=symbol;icon.Child=symbol;var text=new StackPanel{VerticalAlignment=VerticalAlignment.Center};labels[slot]=text;row.Children.Add(text);
        card.MouseLeftButtonDown+=(_,e)=>{if(equipped.TryGetValue(slot,out var item)){ActivateSlot(slot,e.ClickCount==2);e.Handled=true;}else{ClearSelection();heading.Text=GearDesign.EquipmentSlots[slot];TooltipView.Fill(tooltip,[new(GearDesign.EquipmentSlots[slot],ItemTooltip.Gold,Size:18),new("No item in this slot.",ItemTooltip.Gray)]);EmptySlotSelected?.Invoke(slot);}};
    }
    private static string Symbol(int slot)=>slot switch{
        0=>"M3,29 L3,13 Q4,1 16,1 Q28,1 29,13 L29,29 21,24 21,14 11,14 11,24 Z M14,2 L14,13 M18,2 L18,13",
        1=>"M4,2 Q-2,22 16,24 Q34,22 28,2 M16,24 L11,29 16,34 21,29 Z",
        2=>"M1,11 L9,3 16,10 23,3 31,11 29,24 20,20 16,13 12,20 3,24 Z",
        3 or 4 or 18=>"M10,1 L3,6 0,16 7,18 9,11 9,31 25,31 25,11 27,18 34,16 31,6 24,1 Q17,11 10,1 Z",
        5=>"M1,9 L31,9 31,23 1,23 Z M12,7 L22,7 22,25 12,25 Z M16,11 L18,11 18,21 16,21 Z",
        6=>"M4,1 L28,1 30,30 20,30 16,13 12,30 2,30 Z",
        7=>"M8,1 L24,1 22,21 30,27 30,31 4,31 4,25 10,19 Z M9,7 L23,7",
        8=>"M4,5 L27,1 30,27 7,31 Z M6,13 L28,9 M7,19 L29,15",
        9=>"M10,16 L10,3 14,3 15,13 16,1 20,1 20,13 22,3 26,3 25,15 28,7 31,9 28,25 22,31 9,29 1,17 5,14 Z",
        10 or 11=>"M16,8 A11,11 0 1 1 15.9,8 M16,13 A6,6 0 1 0 16.1,13 M16,1 L10,6 16,12 22,6 Z",
        12 or 13=>"M16,1 L21,10 31,12 24,20 25,31 16,26 7,31 8,20 1,12 11,10 Z",
        14=>"M11,1 L21,1 31,31 Q16,25 1,31 Z M11,1 Q16,10 21,1",
        15=>"M27,1 L31,1 31,5 13,23 9,19 Z M4,16 L18,30 15,33 1,19 Z M7,24 L10,27 3,34 0,31 Z",
        16=>"M1,4 Q16,8 31,4 L29,22 Q26,28 16,34 Q6,28 3,22 Z M16,8 L16,29 M6,13 L26,13",
        _=>"M5,1 Q31,17 5,33 L8,28 Q25,17 8,6 Z M5,1 L5,33 M1,17 L31,17 25,12 M31,17 L25,22"
    };
    internal void Bind(IEnumerable<EquipmentViewItem> items,TooltipData tooltips,IReadOnlyDictionary<int,CatalogItem> catalog,int previewLevel=60,CharacterSnapshot? snapshot=null)
    {
        entries=items.ToList();character=snapshot;data=tooltips;lookup=catalog;level=previewLevel;equipped.Clear();var extra=new List<EquipmentViewItem>();
        foreach(var row in entries.OrderByDescending(e=>e.Slot.HasValue).ThenByDescending(e=>e.Item.Slot==17)){
            var targets=row.Slot.HasValue?new[]{row.Slot.Value}:GearDesign.Targets(row.Item);int? target=targets.Where(s=>frames.ContainsKey(s)&&!equipped.ContainsKey(s)&&!(s==16&&equipped.GetValueOrDefault(15)?.Item.Slot==17)).Cast<int?>().FirstOrDefault();
            if(!target.HasValue){extra.Add(row);continue;}equipped[target.Value]=row;
        }
        Overflow=extra;extraPanel.Visibility=extra.Count>0?Visibility.Visible:Visibility.Collapsed;extraPanel.Header=$"Additional pieces / slot conflicts ({extra.Count})";binding=true;overflow.ItemsSource=extra;overflow.SelectedItem=null;binding=false;
        foreach(int slot in frames.Keys){labels[slot].Children.Clear();bool filled=equipped.TryGetValue(slot,out var row);var color=filled?row!.Item.NameBrush:Brushes.SlateGray;symbols[slot].Stroke=color;frames[slot].Opacity=filled&&!row!.Active?0.55:1;
            labels[slot].Children.Add(new TextBlock{Text=GearDesign.EquipmentSlots[slot],FontSize=10,Foreground=Brushes.Gray});labels[slot].Children.Add(new TextBlock{Text=filled?row!.Item.Name:slot==16&&equipped.GetValueOrDefault(15)?.Item.Slot==17?"Two-hand weapon":"Empty",FontSize=11,Foreground=color,TextTrimming=TextTrimming.CharacterEllipsis});frames[slot].ToolTip=readOnly&&filled?row!.Item.Name+" · select for tooltip":filled?$"{row!.Item.Name}\nItem level {row.Item.ItemLevel}\nSelect for tooltip · double-click to edit":GearDesign.EquipmentSlots[slot];}
        var selected=entries.FirstOrDefault(e=>Equals(e.Key,selectedKey))??entries.FirstOrDefault(e=>e.Item.Id==Selection?.Item.Id&&e.Slot==Selection.Slot)??entries.FirstOrDefault();if(selected is not null)Select(selected);else ClearSelection();
    }
    internal void SelectSlot(int slot){if(equipped.TryGetValue(slot,out var row))Select(row);else ClearSelection();}
    internal void ActivateSlot(int slot,bool edit=false){SelectSlot(slot);if(edit&&!readOnly&&Selection is {} row)EditRequested?.Invoke(row);}
    internal string TooltipText=>string.Join("\\n",tooltip.Children.OfType<Grid>().SelectMany(g=>g.Children.OfType<TextBlock>()).Select(t=>t.Text));
    internal void SelectKey(object? key){var row=entries.FirstOrDefault(e=>Equals(e.Key,key));if(row is not null)Select(row);}
    private void Select(EquipmentViewItem row){Selection=row;drops.IsEnabled=true;selectedKey=row.Key;heading.Text=$"{row.Item.Name} · iLvl {row.Item.ItemLevel}";if(data is not null)TooltipView.Fill(tooltip,ItemTooltip.Build(row.Item,data,lookup,level,character?.Items.FirstOrDefault(i=>i.Equipped&&i.Location!="Set preview"&&i.Guid.Equals(row.Key)),character));Highlight();SelectionChanged?.Invoke(row);}
    private void ClearSelection(){Selection=null;drops.IsEnabled=false;selectedKey=null;heading.Text="SELECT A SLOT";TooltipView.Fill(tooltip,[new("Select an occupied equipment slot.",ItemTooltip.Gray)]);Highlight();SelectionChanged?.Invoke(null);}
    private void Highlight(){foreach(var (slot,frame) in frames){bool selected=equipped.TryGetValue(slot,out var item)&&Equals(item.Key,selectedKey);frame.BorderBrush=selected?Brushes.LightSkyBlue:equipped.TryGetValue(slot,out item)?item.Item.NameBrush:new SolidColorBrush(Color.FromRgb(65,69,79));frame.BorderThickness=new Thickness(selected?2:1);}}
}
