using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace WoWItemEditor;

internal sealed record ItemPreviewContext(CatalogItem Item,CharacterSnapshot? Character);
internal sealed class ItemVisualPreview : DockPanel
{
    private static readonly ConditionalWeakTable<StackPanel,ItemVisualPreview> hosts=new();
    private static readonly ConditionalWeakTable<object,ItemPreviewContext> contexts=new();
    private readonly ItemModelView itemView=new(compact:true);
    private readonly TabControl tabs=new();
    private CharacterPreviewWindow? characterWindow;
    private ItemPreviewContext? context;
    private IReadOnlyDictionary<int,CatalogItem>? outfit;
    private ModelCharacter? profile;
    private int request;
    internal Task Loading=>characterWindow?.Viewer.Loading??itemView.Loading;
    internal int SelectedIndex{get=>tabs.SelectedIndex;set=>tabs.SelectedIndex=value;}
    internal CharacterScene? Scene=>characterWindow?.Viewer.Scene;
    internal ItemVisualPreview(StackPanel tooltip,FrameworkElement? tooltipContent=null)
    {
        hosts.Add(tooltip,this);var open=new Button{Content="On character…",HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,0,0,6)};SetDock(open,Dock.Top);Children.Add(open);open.Click+=(_,_)=>OpenCharacter();Children.Add(tabs);tabs.Items.Add(new TabItem{Header="Tooltip",Content=tooltipContent??new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=new Border{Background=new SolidColorBrush(Color.FromRgb(16,17,27)),Padding=new Thickness(14),Child=tooltip}}});
        tabs.Items.Add(new TabItem{Header="Item model",Content=itemView});tabs.SelectionChanged+=(_,e)=>{if(ReferenceEquals(e.Source,tabs))Refresh();};
    }
    internal static void Remember(object lines,CatalogItem item,CharacterSnapshot? character)=>contexts.Add(lines,new(item,character));
    internal static void Update(StackPanel panel,object lines)
    {
        if(!hosts.TryGetValue(panel,out var host))return;host.context=contexts.TryGetValue(lines,out var value)?value:null;host.Refresh();
    }
    internal static void Outfit(StackPanel panel,IReadOnlyDictionary<int,CatalogItem> items,ModelCharacter? character=null)
    {
        if(!hosts.TryGetValue(panel,out var host))return;host.outfit=items;host.profile=character;host.Refresh();
    }
    internal void Bind(CatalogItem? item,IReadOnlyDictionary<int,CatalogItem>? items=null,ModelCharacter? character=null)
    {
        context=item is null?null:new(item,null);outfit=items;profile=character;Refresh();
    }
    internal void OpenCharacter(){if(characterWindow is {IsVisible:true}){characterWindow.Activate();Refresh();return;}characterWindow=new CharacterPreviewWindow{Owner=Window.GetWindow(this)};characterWindow.Closed+=(_,_)=>{characterWindow=null;request++;};characterWindow.Show();Refresh();}
    private async void Refresh()
    {
        int token=++request;if(SelectedIndex==1)itemView.Bind(context?.Item);if(characterWindow is not {IsVisible:true})return;
        var items=outfit??new Dictionary<int,CatalogItem>();ModelCharacter? character=profile;
        if(outfit is null&&context?.Character is {} saved){items=saved.Items.Where(i=>i.Equipped).Where(i=>PreviewCatalog.TryGetValue(i.Entry,out _)).GroupBy(i=>i.Slot).ToDictionary(g=>g.Key,g=>PreviewCatalog[g.First().Entry]);try{character=await ModelCharacterRepository.Load(saved.Character);}catch{character=new(Name:saved.Character.Name);}}
        var worn=new Dictionary<int,CatalogItem>(items);if(outfit is null&&context is {} selected&&GearDesign.Targets(selected.Item).FirstOrDefault(-1) is int slot&&slot>=0){worn[slot]=selected.Item;if(slot==15&&selected.Item.Slot==17)worn.Remove(16);}
        if(token==request&&characterWindow is {} window){window.Title="Character preview · "+(character?.Name??context?.Item.Name??"Complete outfit");window.Viewer.Bind(worn,character);}
    }
    internal static IReadOnlyDictionary<int,CatalogItem> PreviewCatalog {get;set;}=new Dictionary<int,CatalogItem>();
}

internal sealed class CharacterPreviewWindow : Window
{
    internal CharacterModelView Viewer {get;}=new(enlarged:true);
    internal CharacterPreviewWindow(){Title="Character preview";WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=TooltipView.Brush("#171B25");MinWidth=Math.Min(960,SystemParameters.WorkArea.Width);MinHeight=Math.Min(720,SystemParameters.WorkArea.Height);Width=Math.Max(MinWidth,Math.Min(1150,SystemParameters.WorkArea.Width-30));Height=Math.Max(MinHeight,Math.Min(1000,SystemParameters.WorkArea.Height-30));Content=Viewer;}
}
