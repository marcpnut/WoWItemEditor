using System.Windows;
using System.Windows.Controls;
namespace AnoWowItemEditor;

internal sealed class CharacterModelView : DockPanel
{
    private readonly ComboBox race=new(){DisplayMemberPath="Label",Width=140},sex=new(){DisplayMemberPath="Label",Width=110};
    private readonly ItemModelView viewport=new(compact:true);
    private readonly TextBlock status=DesignerWindow.Text("Choose items to preview on the character.",11);
    private IReadOnlyDictionary<int,CatalogItem> equipment=new Dictionary<int,CatalogItem>();
    private ModelCharacter profile=new();
    private string signature="";
    private int request;
    private bool binding;
    private CancellationTokenSource? cancellation;
    internal Task Loading {get;private set;}=Task.CompletedTask;
    internal CharacterScene? Scene {get;private set;}
    internal string Feedback=>status.Text;
    internal CharacterModelView()
    {
        viewport.Rotate(0,-90,0);var choices=new WrapPanel{Margin=new Thickness(6)};SetDock(choices,Dock.Top);Children.Add(choices);race.ItemsSource=new[]{new FilterChoice(1,"Human"),new(2,"Orc"),new(3,"Dwarf"),new(4,"Night elf"),new(5,"Undead"),new(6,"Tauren"),new(7,"Gnome"),new(8,"Troll"),new(10,"Blood elf"),new(11,"Draenei")};sex.ItemsSource=new[]{new FilterChoice(0,"Male"),new(1,"Female")};race.SelectedIndex=sex.SelectedIndex=0;choices.Children.Add(race);choices.Children.Add(sex);status.TextWrapping=TextWrapping.Wrap;status.MaxHeight=65;status.Margin=new Thickness(8);SetDock(status,Dock.Bottom);Children.Add(status);Children.Add(viewport);
        race.SelectionChanged+=(_,_)=>Changed();sex.SelectionChanged+=(_,_)=>Changed();
    }
    private void Changed(){if(binding)return;profile=profile with{Race=(race.SelectedItem as FilterChoice)?.Id??1,Sex=(sex.SelectedItem as FilterChoice)?.Id??0,Choices=null};Refresh();}
    internal void Bind(IReadOnlyDictionary<int,CatalogItem> items,ModelCharacter? character=null)
    {
        equipment=new Dictionary<int,CatalogItem>(items);if(character is not null){profile=character;binding=true;race.SelectedItem=race.Items.Cast<FilterChoice>().FirstOrDefault(c=>c.Id==character.Race);sex.SelectedItem=sex.Items.Cast<FilterChoice>().FirstOrDefault(c=>c.Id==character.Sex);binding=false;}Refresh();
    }
    private void Refresh()
    {
        string next=$"{profile.Race}:{profile.Sex}:{profile.Name}:"+string.Join(',',equipment.OrderBy(p=>p.Key).Select(p=>$"{p.Key}:{p.Value.Id}"))+"/"+string.Join(',',(profile.Choices??[]).OrderBy(p=>p.Key).Select(p=>$"{p.Key}:{p.Value}"));if(next==signature)return;signature=next;cancellation?.Cancel();cancellation?.Dispose();cancellation=new();Loading=Render(++request,new Dictionary<int,CatalogItem>(equipment),profile,cancellation.Token);
    }
    private async Task Render(int token,IReadOnlyDictionary<int,CatalogItem> items,ModelCharacter character,CancellationToken cancel)
    {
        await Task.Delay(120);if(token!=request)return;Scene=null;viewport.ShowLoading("Loading character and outfit…");status.Text="Composing body skin and equipped item textures…";
        try{var scene=await CharacterModelBuilder.Build(character,items,cancel);if(token!=request)return;Scene=scene;viewport.ShowScene(scene.Mesh,character.Name);status.Text=$"{items.Count} equipment selections · {scene.Applied} visual slots applied · static pose"+(scene.Notes.Count>1?$" · {scene.Notes.Count-1} detail(s) need review":"");status.ToolTip=string.Join("\n",scene.Notes);}
        catch(OperationCanceledException){}
        catch(Exception ex){if(token==request){viewport.ShowLoading("Character preview unavailable");status.Text=ex.Message;signature="";}}
    }
}
