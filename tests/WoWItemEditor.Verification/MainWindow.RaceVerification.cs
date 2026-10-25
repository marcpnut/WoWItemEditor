using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace WoWItemEditor;
public partial class MainWindow
{
    private async Task VerifyRaces(CatalogResult catalog)
    {
        var view=new CharacterModelView(enlarged:true);var window=new Window{Content=view,Width=850,Height=900,Owner=this};window.Show();var reports=new List<object>();var failures=new List<string>();var captures=new List<string>();
        foreach(var (race,name) in new[]{(1,"Human"),(2,"Orc"),(3,"Dwarf"),(4,"NightElf"),(5,"Undead"),(6,"Tauren"),(7,"Gnome"),(8,"Troll"),(10,"BloodElf"),(11,"Draenei"),(95,"HighOrderSkyborne"),(96,"WindshaperSkyborne")})foreach(int sex in new[]{0,1}){
            if(Environment.GetCommandLineArgs().Contains("--server-races")&&race is not (95 or 96))continue;if(Environment.GetCommandLineArgs().Contains("--race-probe")&&!(race is 1 or 2 or 7))continue;var worn=Environment.GetCommandLineArgs().Contains("--dressed")?SetDesignReview.Equip(new(new(0,"Outfit",60,1,false,0),[]),catalog.Tooltips.Sets[523].Members.Where(id=>catalog.Items.Any(i=>i.Id==id)).Select(id=>catalog.Items.First(i=>i.Id==id)),catalog.Items.ToDictionary(i=>i.Id),60).Slots:new Dictionary<int,CatalogItem>();view.Bind(worn,new(race,sex,name+(sex==0?" male":" female")));await view.Loading;string key=$"{(Environment.GetCommandLineArgs().Contains("--dressed")?"outfit":"race")}-{race}-{sex}";await RenderEditorWindow(window,key);captures.Add("artifacts/"+key+".png");if(view.Scene is null)failures.Add(key+": "+view.Feedback);reports.Add(new{race,sex,body=view.Scene?.BodyFile,triangles=view.Scene?.Mesh.Triangles,error=view.Scene is null?view.Feedback:null,notes=view.Scene?.Notes,geometry=view.Scene is null?null:await StaticItemModel.Describe(ItemModelAssets.Shared,view.Scene.BodyFile)});
        }
        var visual=new DrawingVisual();using(var draw=visual.RenderOpen())for(int n=0;n<captures.Count;n++){var picture=new BitmapImage();picture.BeginInit();picture.CacheOption=BitmapCacheOption.OnLoad;picture.UriSource=new Uri(Path.GetFullPath(captures[n]));picture.EndInit();draw.DrawImage(picture,new Rect(n%5*255,n/5*270,255,270));}var bitmap=new RenderTargetBitmap(1275,1080,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Environment.GetCommandLineArgs().Contains("--dressed")?"artifacts/playable-races-outfit-preview.png":"artifacts/playable-races-preview.png"))encoder.Save(file);await File.WriteAllTextAsync(Environment.GetCommandLineArgs().Contains("--dressed")?"artifacts/race-outfit-verification.json":"artifacts/race-model-verification.json",JsonSerializer.Serialize(reports,new JsonSerializerOptions{WriteIndented=true}));window.Close();if(failures.Count>0)throw new InvalidOperationException(string.Join("\n",failures));Close();
    }
}
