using System.IO;
using System.Text.Json;
using System.Windows;
namespace AnoWowItemEditor;
public partial class MainWindow
{
    private async Task VerifyEquipmentLayout(CatalogResult catalog)
    {
        static void Assert(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        var lookup=catalog.Items.ToDictionary(i=>i.Id);var ring=catalog.Items.First(i=>i.Slot==11);var twoHand=catalog.Items.First(i=>i.Slot==17);var shield=catalog.Items.First(i=>i.Slot==14);var head=catalog.Items.First(i=>i.Slot==1);var view=new EquipmentSetView();
        view.Bind([new("ring1",ring),new("ring2",ring),new("ring3",ring),new("twohand",twoHand),new("shield",shield)],catalog.Tooltips,lookup);
        Assert(view.Equipped[10].Key.Equals("ring1")&&view.Equipped[11].Key.Equals("ring2"),"Rings occupy separate finger slots");Assert(view.Equipped[15].Item.Id==twoHand.Id&&!view.Equipped.ContainsKey(16),"Two-handed weapon reserves both hands");Assert(view.Overflow.Count==2,"Conflicting/extra pieces remain accessible");
        var set=catalog.Tooltips.Sets[523];view.Bind(set.Members.Where(lookup.ContainsKey).Select(id=>new EquipmentViewItem(id,lookup[id])),catalog.Tooltips,lookup);Assert(view.Equipped.ContainsKey(0)&&view.Equipped.ContainsKey(4),"Dreadnaught occupies head and chest slots");view.ActivateSlot(0);var selected=view.Selection!.Item;Assert(view.TooltipText.Contains(selected.Name)&&view.TooltipText.Contains("Requires Level"),"Single selection shows the actual WoW item tooltip");int edited=0;view.EditRequested+=row=>edited=row.Item.Id;view.ActivateSlot(0,true);Assert(edited==selected.Id,"Double-click activation requests the selected item editor");view.Bind(set.Members.Where(lookup.ContainsKey).Select(id=>new EquipmentViewItem(id,lookup[id])),catalog.Tooltips,lookup);Assert(view.Selection?.Item.Id==selected.Id,"Refreshing layout retains selected item");
        var window=new Window{Title="Equipment layout verification",Content=view,Width=1260,Height=850,Background=Background,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner};window.Show();await RenderEditorWindow(window,"equipment-layout-preview");window.Close();
        var created=(await ContentEditing.ListAsync()).FirstOrDefault(o=>o.Kind=="NewSet"&&o.Active&&catalog.Tooltips.Sets.ContainsKey(o.EntityId));if(created is not null){var session=await CreatedSets.Load(created.EntityId);var modify=new CreatedSetsWindow(catalog,await ReferenceCatalog.LoadAsync(),session){Owner=this};modify.Show();await RenderEditorWindow(modify,"modify-created-sets-preview");Assert(modify.FooterVisible,"Action buttons remain visible below equipment layout");modify.Close();}
        view.Bind([],catalog.Tooltips,lookup);Assert(view.Selection is null&&view.Equipped.Count==0,"Empty sets clear tooltip and occupied slots");
        await File.WriteAllTextAsync("artifacts/equipment-layout-verification.json",JsonSerializer.Serialize(new{checks="WoW slot positions, separate ring slots, two-hand reservation, visible overflow, selected real tooltip, double-click edit event, selection retention and empty-set clearing; no game writes"},new JsonSerializerOptions{WriteIndented=true}));
    }
}
