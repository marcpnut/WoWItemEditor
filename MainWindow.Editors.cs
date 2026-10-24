using System.Windows;
namespace AnoWowItemEditor;

public partial class MainWindow
{
    private bool editorLoading;
    private async void OpenDesigner(object sender,RoutedEventArgs e)
    {
        if(loadedCatalog is null||editorLoading)return;editorLoading=true;try{int? id=(ItemsGrid.SelectedItem as CatalogItem)?.Id??(ItemsGrid.SelectedItem as CharacterItemRow)?.Item.Id;var window=new DesignerWindow(loadedCatalog,id){Owner=this};window.ShowDialog();if(window.Saved){references=null;referenceLoad=null;await LoadCatalog();}}catch(Exception ex){Status.Text="Could not open designer: "+ex.Message;}finally{editorLoading=false;}
    }
    private void EditItemDoubleClick(object sender,System.Windows.Input.MouseButtonEventArgs e){if(ItemsGrid.SelectedItem is not null)OpenItemEditor(sender,new RoutedEventArgs());}
    private async void OpenItemEditor(object sender,RoutedEventArgs e)
    {
        if(loadedCatalog is null||editorLoading)return;editorLoading=true;
        try{int? id=(ItemsGrid.SelectedItem as CatalogItem)?.Id??(ItemsGrid.SelectedItem as CharacterItemRow)?.Item.Id;var editor=new ItemEditorWindow(loadedCatalog,id){Owner=this};editor.ShowDialog();if(editor.Saved)await LoadCatalog();}catch(Exception ex){Status.Text="Could not open item editor: "+ex.Message;}finally{editorLoading=false;}
    }
    private async Task<ReferenceCatalog> EditorReferences()
    {
        if(references is not null)return references;
        referenceLoad??=ReferenceCatalog.LoadAsync();try{references=await referenceLoad;return references;}catch{referenceLoad=null;throw;}
    }
    private async void OpenSetEditor(object sender,RoutedEventArgs e)=>await OpenEditor("Set");
    private async void OpenWeaponEditor(object sender,RoutedEventArgs e)=>await OpenEditor("Proc");
    private async Task OpenSpellSetEditor(ReferenceEntry spell)
    {
        if(loadedCatalog is null||editorLoading)return;
        var sets=loadedCatalog.Tooltips.Sets.Values.Where(s=>spell.SetIds.Contains(s.Id)).ToList();
        if(sets.Count==0){Status.Text=$"Spell {spell.SpellId} is not assigned to a set. Open Set bonuses to add it to a set first.";return;}
        editorLoading=true;try{int id=sets[0].Id;if(sets.Count>1){var picker=new Window{Title="Choose the set to edit",Width=460,Height=190,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Background};var panel=new System.Windows.Controls.StackPanel{Margin=new Thickness(20)};picker.Content=panel;var combo=new System.Windows.Controls.ComboBox{ItemsSource=sets.Select(s=>new ContentChoice(s.Id,s.Name)).ToList(),DisplayMemberPath="Label",SelectedIndex=0};panel.Children.Add(combo);var select=new System.Windows.Controls.Button{Content="Edit selected set",Margin=new Thickness(0,14,0,0)};select.Click+=(_,_)=>picker.DialogResult=true;panel.Children.Add(select);if(picker.ShowDialog()!=true)return;id=((ContentChoice)combo.SelectedItem).Id;}
            var editor=new ContentEditorWindow("Set",loadedCatalog,await EditorReferences(),id,spell.SpellId){Owner=this};editor.ShowDialog();if(editor.Saved){references=null;referenceLoad=null;referenceGeneration++;await LoadCatalog();if(referenceMode is not null)await ShowReferencePage(referenceMode,true);}
        }catch(Exception ex){Status.Text="Could not open this set bonus: "+ex.Message;}finally{editorLoading=false;}
    }
    private async Task OpenEditor(string kind)
    {
        if(loadedCatalog is null||editorLoading)return;editorLoading=true;
        try{Status.Text="Loading spell references for the editor…";var refs=await EditorReferences();var selected=(ItemsGrid.SelectedItem as CatalogItem);int? id=kind=="Set"?selected?.Number("ItemSet"):selected?.ClassId==2?selected.Id:null;if(id==0)id=null;
            var editor=new ContentEditorWindow(kind,loadedCatalog,refs,id){Owner=this};editor.ShowDialog();if(editor.Saved){references=null;referenceLoad=null;await LoadCatalog();}else SetCatalogStatus(loadedCatalog);
        }catch(Exception ex){Status.Text="Could not open editor: "+ex.Message;}finally{editorLoading=false;}
    }
    private async void OpenContentHistory(object sender,RoutedEventArgs e)
    {
        if(editorLoading)return;editorLoading=true;try{var history=new ContentHistoryWindow(await EditorReferences()){Owner=this};history.ShowDialog();if(history.Changed){references=null;referenceLoad=null;await LoadCatalog();}}catch(Exception ex){Status.Text="Could not open edit history: "+ex.Message;}finally{editorLoading=false;}
    }
}
