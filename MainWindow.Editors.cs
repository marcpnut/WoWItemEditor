using System.Windows;
namespace AnoWowItemEditor;

public partial class MainWindow
{
    private bool editorLoading;
    private async Task<ReferenceCatalog> EditorReferences()
    {
        if(references is not null)return references;
        referenceLoad??=ReferenceCatalog.LoadAsync();try{references=await referenceLoad;return references;}catch{referenceLoad=null;throw;}
    }
    private async void OpenSetEditor(object sender,RoutedEventArgs e)=>await OpenEditor("Set");
    private async void OpenWeaponEditor(object sender,RoutedEventArgs e)=>await OpenEditor("Proc");
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
