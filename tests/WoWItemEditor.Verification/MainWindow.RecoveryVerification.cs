using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace WoWItemEditor;
public partial class MainWindow
{
    private async Task VerifyRecovery(CatalogResult catalog)
    {
        if(!Environment.GetCommandLineArgs().Contains("--recovery-regression-only"))
        {
        using var db=await DatabaseConnection.OpenAsync(CatalogRepository.Database);
        await ActiveChangeRecovery.Initialize(db);
        async Task<string> State(){var state=new List<object>();foreach(string table in new[]{ContentEditing.Operations,ActiveChangeRecovery.Batches,"item_sparse","item","item_set","item_set_spell","item_effect","item_x_item_effect","hotfix_data","hotfix_blob","spell_effect",AppSettings.Current.WorldDatabase+".spell_proc",AppSettings.Current.WorldDatabase+".creature_loot_template"})state.Add(await WorldDrops.Query(db,null,$"SELECT * FROM {table} ORDER BY 1"));return JsonSerializer.Serialize(state); }
        string before=await State();
        var plan=await ActiveChangeRecovery.Prepare(message=>File.WriteAllText("artifacts/recovery-progress.txt",message));await File.WriteAllTextAsync("artifacts/recovery-plan.json",JsonSerializer.Serialize(plan,new JsonSerializerOptions{WriteIndented=true}));
        using(var tx=await db.BeginTransactionAsync(System.Data.IsolationLevel.Serializable))
        {
            try
            {
                bool rejected=false;
                try{await ActiveChangeRecovery.Run(db,tx,"stale-review",true);}catch(InvalidOperationException ex) when(ex.Message.Contains("changed after the review")){rejected=true;}
                if(!rejected)throw new Exception("Stale recovery review was accepted.");
                var applied=await ActiveChangeRecovery.Run(db,tx,plan.Guard,true,message=>File.WriteAllText("artifacts/recovery-progress.txt",message));
                if(applied.Rows.Count!=plan.Rows.Count)throw new Exception("Recovery count changed.");
                var data=await WorldDrops.Query(db,tx,$"SELECT AfterJson FROM {ContentEditing.Operations} WHERE Kind='Item' AND IsActive=1 ORDER BY RecordedUtc");
                foreach(var group in data.Select(r=>JsonSerializer.Deserialize<ItemSnapshot>(r["AfterJson"]!)!).SelectMany(s=>s.Tables.GetValueOrDefault("item_sparse")??[]).GroupBy(r=>r["ID"])){
                    var retained=group.Last();var current=await WorldDrops.Query(db,tx,$"SELECT * FROM item_sparse WHERE ID={int.Parse(group.Key!)}");
                    if(!current.Any(r=>retained.All(p=>p.Key=="VerifiedBuild"||r.GetValueOrDefault(p.Key)==p.Value)))throw new Exception("Final saved item definition was not restored: "+group.Key);
                }
                var batches=await WorldDrops.Query(db,tx,$"SELECT OperationCount FROM {ActiveChangeRecovery.Batches} ORDER BY CreatedUtc DESC LIMIT 1");
                if(batches.Count!=1||int.Parse(batches[0]["OperationCount"]!)!=plan.Rows.Count)throw new Exception("Recovery batch backup missing.");
            }
            finally{await tx.RollbackAsync();}
        }
        if(await State()!=before)throw new Exception("Recovery test failed to roll back history.");
        }
        await VerifyContentEditors(catalog,await EditorReferences());
        var window=new ActiveRecoveryWindow{Owner=this};window.Show();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);window.UpdateLayout();
        var content=(FrameworkElement)window.Content;var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create("artifacts/recovery-window.png"))encoder.Save(output);window.Close();
        await File.WriteAllTextAsync("artifacts/recovery-verification.txt","PASS: real active-history scan, stale-review rejection, complete transactional recovery and batch backup, history rollback. No live recovery committed.");
    }
}
