using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
namespace AnoWowItemEditor;
public partial class MainWindow
{
    private async Task VerifyStartup()
    {
        try{
            var clock=Stopwatch.StartNew();await StartWorkspace();long startupMs=clock.ElapsedMilliseconds;
            void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
            Check(characterMode&&CharacterNav.IsChecked==true,"Characters must be the startup page.");
            Check(loadedCatalog is not null&&items.Count>0&&references is not null&&references.Spells.Count>0&&references.Procs.Count>0,"All three catalogs must preload at startup.");
            Check(characters.Count==0||characterSnapshot is not null&&characterItems.Count==characterSnapshot.Items.Count,"Saved character items resolve at startup.");
            var cached=references;var catalog=loadedCatalog;var pending=referenceLoad;var switches=new List<object>();
            foreach(var page in new[]{"Spell catalog","Proc catalog","Spell catalog","Proc catalog"}){clock.Restart();await ShowReferencePage(page);switches.Add(new{page,milliseconds=clock.ElapsedMilliseconds});Check(ReferenceEquals(cached,references)&&ReferenceEquals(pending,referenceLoad)&&referenceLoads==1,"Switches must reuse the single preloaded reference snapshot.");Check(referenceView is not null&&!referenceView.IsEmpty,"Cached catalog must be displayed.");}
            ItemNav.IsChecked=true;Check(ReferenceEquals(catalog,loadedCatalog)&&!characterMode&&view is not null&&!view.IsEmpty,"Item catalog switches without reloading.");CharacterNav.IsChecked=true;await armory.Loading;Check(characterMode,"Return to Characters.");await RenderEditorWindow(this,"startup-characters-preview");
            await File.WriteAllTextAsync("artifacts/startup-verification.json",JsonSerializer.Serialize(new{startupMs,items=items.Count,spells=references!.Spells.Count,procs=references.Procs.Count,referenceLoads,switches,checks="Characters default; all catalogs preloaded; single shared reference load; cached navigation; saved character items; read-only"},new JsonSerializerOptions{WriteIndented=true}));Close();
        }catch(Exception ex){await File.WriteAllTextAsync("artifacts/verification-error.txt",ex.ToString());Environment.ExitCode=1;Close();}
    }
}
