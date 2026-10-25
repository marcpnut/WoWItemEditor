using System.IO;
using System.Text.Json;
using System.Windows;
namespace AnoWowItemEditor;
public partial class MainWindow
{
    private async Task VerifySavedAppearances(CatalogResult catalog)
    {
        var view=new CharacterModelView(enlarged:true,savedAppearance:true);var window=new Window{Content=view,Width=1000,Height=950,Owner=this};window.Show();var reports=new List<object>();var failures=new List<string>();var lookup=catalog.Items.ToDictionary(i=>i.Id);
        foreach(var hero in await CharacterRepository.ListAsync()){
            var profile=await ModelCharacterRepository.Load(hero);var saved=await CharacterRepository.LoadAsync(hero);var gear=saved.Items.Where(i=>i.Equipped&&i.Slot is not (0 or 14)&&lookup.ContainsKey(i.Entry)).GroupBy(i=>i.Slot).ToDictionary(g=>g.Key,g=>lookup[g.First().Entry]);view.Bind(gear,profile);await view.Loading;bool matches=view.Scene is not null&&(profile.Choices??[]).All(p=>view.Scene.Customizations.TryGetValue(p.Key,out int selected)&&selected==p.Value);if(!matches)failures.Add(hero.Name+": "+view.Feedback);reports.Add(new{hero.Guid,hero.Name,profile.Race,profile.Sex,profile.ClassId,savedChoices=profile.Choices,appliedChoices=view.Scene?.Customizations,matches,notes=view.Scene?.Notes});await RenderEditorWindow(window,"saved-appearance-"+hero.Guid);
        }
        await File.WriteAllTextAsync("artifacts/saved-appearance-verification.json",JsonSerializer.Serialize(reports,new JsonSerializerOptions{WriteIndented=true}));window.Close();if(failures.Count>0)throw new InvalidOperationException(string.Join("\n",failures));Close();
    }
}
