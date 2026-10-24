using System.Windows.Controls;
namespace AnoWowItemEditor;
public partial class MainWindow
{
    private readonly ItemModelView itemModel=new(compact:true);
    private readonly CharacterModelView catalogCharacter=new();
    private readonly ArmoryView armory=new();
    private static void InstallVisualPreview(ScrollViewer scroll,StackPanel panel){if(scroll.Parent is not Panel parent)return;int index=parent.Children.IndexOf(scroll);parent.Children.Remove(scroll);parent.Children.Insert(index,new ItemVisualPreview(panel,scroll));}
    private bool armoryVisible=true;
    private void ToggleArmory(object sender,System.Windows.RoutedEventArgs e){armoryVisible=!armoryVisible;ShowArmory();if(!armoryVisible){CatalogPreviewTabs.SelectedIndex=2;UpdateCatalogCharacter();}}
    private void ShowArmory(){ArmoryHost.Visibility=characterMode&&armoryVisible?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;ItemBrowserBody.Visibility=characterMode&&armoryVisible?System.Windows.Visibility.Collapsed:System.Windows.Visibility.Visible;}
    private void UpdateCatalogCharacter()
    {
        var worn=characterMode?armory.Equipment.ToDictionary(p=>p.Key,p=>p.Value):new Dictionary<int,CatalogItem>();
        if(SelectedItem() is {} item){var targets=GearDesign.Targets(item);int slot=characterMode&&ItemsGrid.SelectedItem is CharacterItemRow{Instance.Equipped:true} owned?owned.Instance.Slot:targets.FirstOrDefault(s=>!worn.ContainsKey(s),targets.FirstOrDefault(-1));if(slot>=0){worn[slot]=item;if(slot==15&&item.Slot==17)worn.Remove(16);if(slot==16&&worn.GetValueOrDefault(15)?.Slot==17)worn.Remove(15);}}
        catalogCharacter.Bind(worn,characterMode?armory.Profile:null);
    }

    private void CatalogPreviewTabChanged(object sender,SelectionChangedEventArgs e)
    {
        if(ReferenceEquals(e.Source,CatalogPreviewTabs)&&CatalogPreviewTabs.SelectedIndex==1&&ItemModelHost is not null)itemModel.Bind(SelectedItem());
        if(ReferenceEquals(e.Source,CatalogPreviewTabs)&&CatalogPreviewTabs.SelectedIndex==2&&CatalogCharacterHost is not null)UpdateCatalogCharacter();
    }
}
