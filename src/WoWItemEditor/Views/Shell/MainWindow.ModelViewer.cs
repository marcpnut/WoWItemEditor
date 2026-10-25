using System.Windows;
using System.Windows.Controls;
namespace WoWItemEditor;
public partial class MainWindow
{
    private readonly ItemModelView itemModel=new(compact:true);
    private CharacterPreviewWindow? catalogCharacterWindow;
    private void OpenCatalogCharacter(object sender,System.Windows.RoutedEventArgs e){if(catalogCharacterWindow is {IsVisible:true}){catalogCharacterWindow.Activate();UpdateCatalogCharacter();return;}catalogCharacterWindow=new(){Owner=this};catalogCharacterWindow.Closed+=(_,_)=>catalogCharacterWindow=null;catalogCharacterWindow.Show();UpdateCatalogCharacter();}
    private readonly ArmoryView armory=new();
    private static void InstallVisualPreview(ScrollViewer scroll,StackPanel panel){if(scroll.Parent is not Panel parent)return;int index=parent.Children.IndexOf(scroll);parent.Children.Remove(scroll);parent.Children.Insert(index,new ItemVisualPreview(panel,scroll));}
    private void SetupItemRowTooltips(){ItemsGrid.LoadingRow+=(_,e)=>{e.Row.ToolTip=new ToolTip();e.Row.ToolTipOpening+=ItemRowToolTip;};ItemsGrid.UnloadingRow+=(_,e)=>e.Row.ToolTipOpening-=ItemRowToolTip;}
    private void ItemRowToolTip(object sender,ToolTipEventArgs e){if(sender is not DataGridRow row||row.ToolTip is not ToolTip tip)return;var owned=row.DataContext as CharacterItemRow;var item=row.DataContext as CatalogItem??owned?.Item;if(item is null)return;var panel=new StackPanel{MaxWidth=360};TooltipView.Fill(panel,ItemTooltip.Build(item,tooltipData,catalogById,previewLevel,owned?.Instance,characterMode?characterSnapshot:null));tip.Background=TooltipView.Brush("#10111B");tip.BorderBrush=TooltipView.Brush("#616781");tip.MaxHeight=Math.Min(800,SystemParameters.WorkArea.Height-20);tip.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};}
    private void TrySelectedCharacterItem(object sender,System.Windows.RoutedEventArgs e){if(SelectedItem() is {} item)armory.TryItem(item);}
    private void ShowArmory(){ArmoryHost.Visibility=characterMode?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;CatalogPreviewBorder.Visibility=characterMode?System.Windows.Visibility.Collapsed:System.Windows.Visibility.Visible;ItemBrowserBody.Visibility=System.Windows.Visibility.Visible;CatalogControlsBorder.Margin=new System.Windows.Thickness(0,0,characterMode?646:0,18);ItemBrowserBody.Margin=new System.Windows.Thickness(0,0,characterMode?646:0,0);CatalogPreviewColumn.MinWidth=characterMode?0:290;CatalogPreviewColumn.Width=new System.Windows.GridLength(characterMode?0:390);if(characterMode){MinHeight=Math.Min(1000,System.Windows.SystemParameters.WorkArea.Height);MinWidth=Math.Min(1440,System.Windows.SystemParameters.WorkArea.Width);Height=Math.Max(Height,MinHeight);Width=Math.Max(Width,MinWidth);}}
    private void UpdateCatalogCharacter()
    {
        if(catalogCharacterWindow is not {IsVisible:true})return;var worn=characterMode?armory.Equipment.ToDictionary(p=>p.Key,p=>p.Value):new Dictionary<int,CatalogItem>();
        if(SelectedItem() is {} item){var targets=GearDesign.Targets(item);int slot=characterMode&&ItemsGrid.SelectedItem is CharacterItemRow{Instance.Equipped:true} owned?owned.Instance.Slot:targets.FirstOrDefault(s=>!worn.ContainsKey(s),targets.FirstOrDefault(-1));if(slot>=0){worn[slot]=item;if(slot==15&&item.Slot==17)worn.Remove(16);if(slot==16&&worn.GetValueOrDefault(15)?.Slot==17)worn.Remove(15);}}
        catalogCharacterWindow.Viewer.Bind(worn,characterMode?armory.Profile:null);
    }

    private void CatalogPreviewTabChanged(object sender,SelectionChangedEventArgs e)
    {
        if(ReferenceEquals(e.Source,CatalogPreviewTabs)&&CatalogPreviewTabs.SelectedIndex==1&&ItemModelHost is not null)itemModel.Bind(SelectedItem());
    }
}
