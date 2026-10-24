using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MySqlConnector;
namespace AnoWowItemEditor;

public partial class MainWindow
{
    private async Task VerifyNewFeatures(CatalogResult catalog)
    {
        static void Assert(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        using(var db=await DatabaseConnection.OpenAsync(CatalogRepository.Database)){
            await HistoryActions.InitializeAsync(db);
            using(var gate=new MySqlCommand("SELECT GET_LOCK('anocore_studio_forge',8)",db))Assert(Convert.ToInt32(await gate.ExecuteScalarAsync())==1,"Verification lock unavailable");
            try{
                OverrideEntry? fixture=null;ItemSnapshot? captured=null;
                foreach(var candidate in catalog.History.Entries.Where(e=>e.Active&&!e.Managed&&e.ContentOperationId is null&&e.OriginalItem?.RequiredLevel==60&&e.ModifiedItem.RequiredLevel==1)){
                    var snapshot=await HistoryActions.CaptureAsync(db,null,candidate.Id);var payload=snapshot.Tables["item_sparse"].SingleOrDefault(r=>r["VerifiedBuild"]==candidate.Build.ToString());
                    if(snapshot.Tables["item_sparse"].Count==1&&payload is not null&&payload.All(p=>candidate.ModifiedItem.Fields.TryGetValue(p.Key,out var value)&&OverrideEntry.Equivalent(p.Value,value))){fixture=candidate;captured=snapshot;break;}
                }
                var entry=fixture??throw new InvalidOperationException("No stable legacy override fixture is available for rolled-back verification.");var initial=captured!;
                var events=new List<string>();
                using(var tx=await db.BeginTransactionAsync(System.Data.IsolationLevel.Serializable)){
                    try{
                        var revert=await HistoryActions.PrepareCoreAsync(db,tx,entry,catalog.Originals);
                        Assert(HistoryActions.ReviewFields(revert).Any(f=>f.Field=="RequiredLevel"&&f.Original=="1"&&f.Modified=="60"),"Review must show actual target values");
                        var reverted=await HistoryActions.ApplyCoreAsync(db,tx,revert);events.Add(reverted.EventId);
                        Assert(reverted.After.Tables["item_sparse"].Count==0,"Revert falls back to original client fields");
                        var retained=await HistoryActions.StateAsync(db,tx,entry.Id,entry.Build);
                        Assert(retained is {Active:false},"Revert stores an inactive archive");
                        await HistoryActions.SetHiddenCoreAsync(db,tx,entry with{Active=false,Managed=true},true);
                        using(var hidden=new MySqlCommand("SELECT COUNT(*) FROM anocore_studio_backup.item_editor_hidden_history WHERE ItemId=@id AND PayloadBuild=@build",db,tx)){hidden.Parameters.AddWithValue("@id",entry.Id);hidden.Parameters.AddWithValue("@build",entry.Build);Assert(Convert.ToInt32(await hidden.ExecuteScalarAsync())==1,"Inactive entry removed from visible history");}
                        await HistoryActions.SetHiddenCoreAsync(db,tx,entry with{Active=false,Managed=true,Hidden=true},false);
                        var reapply=await HistoryActions.PrepareCoreAsync(db,tx,entry with{Active=false,Managed=true},catalog.Originals);
                        Assert(HistoryActions.ReviewFields(reapply).Any(f=>f.Field=="RequiredLevel"&&f.Original=="60"&&f.Modified=="1"),"Reapply review values");
                        var reapplied=await HistoryActions.ApplyCoreAsync(db,tx,reapply);events.Add(reapplied.EventId);
                        Assert(JsonSerializer.Serialize(initial.Tables["item_sparse"])==JsonSerializer.Serialize(reapplied.After.Tables["item_sparse"]),"Reapply restores exact retained payload");
                        var final=await HistoryActions.StateAsync(db,tx,entry.Id,entry.Build);
                        Assert(final is {Active:true}&&final.Version==retained!.Version+1&&final.ModifiedJson==retained.ModifiedJson,"Archive is immutable across actions");
                        bool activeBlocked=false;try{await HistoryActions.SetHiddenCoreAsync(db,tx,entry,true);}catch(InvalidOperationException ex) when(ex.Message.StartsWith("Active overrides")){activeBlocked=true;}Assert(activeBlocked,"Active history removal is blocked");
                        foreach(string table in new[]{"item","item_effect","item_x_item_effect"})Assert(JsonSerializer.Serialize(initial.Tables[table])==JsonSerializer.Serialize(reapplied.After.Tables[table]),"Shared dependency preserved: "+table);
                        bool conflict=false;try{await HistoryActions.ApplyCoreAsync(db,tx,revert);}catch(InvalidOperationException ex) when(ex.Message.StartsWith("Conflict:")){conflict=true;}Assert(conflict,"Stale review is rejected");
                        foreach(string schema in new[]{"foreverclassic_hotfixes.anocore_studio_changes","anocore_studio_backup.changes"})foreach(string id in events){using var check=new MySqlCommand($"SELECT COUNT(*) FROM {schema} WHERE ChangeId=@id",db,tx);check.Parameters.AddWithValue("@id",id);Assert(Convert.ToInt32(await check.ExecuteScalarAsync())==1,"Audit event and mirror saved together");}
                    }finally{await tx.RollbackAsync();}
                }
                Assert(HistoryActions.Canonical(initial)==HistoryActions.Canonical(await HistoryActions.CaptureAsync(db,null,entry.Id)),"Verification rollback restores all game rows");
                foreach(string id in events){using var check=new MySqlCommand("SELECT COUNT(*) FROM anocore_studio_changes WHERE ChangeId=@id",db);check.Parameters.AddWithValue("@id",id);Assert(Convert.ToInt32(await check.ExecuteScalarAsync())==0,"Verification events rolled back");}
            }finally{using var release=new MySqlCommand("SELECT RELEASE_LOCK('anocore_studio_forge')",db);await release.ExecuteScalarAsync();}
        }
        HistoryState.SelectedIndex=0;HistoryActivity.SelectedIndex=0;HistoryRarity.SelectedIndex=5;HistoryMinLevel.Text="1";HistoryMaxLevel.Text="60";ApplyHistoryFilters();
        Assert(historyView!.Cast<OverrideEntry>().All(e=>e.ModifiedItem.Quality==4&&e.ModifiedItem.RequiredLevel is >=1 and <=60),"History rarity and level filters");
        ClearHistoryFilters(this,new RoutedEventArgs());
        await ShowReferencePage("Spell catalog");Assert(references is not null&&references.Spells.Count>1000,"All-spell reference loaded: "+referenceCount.Text);
        Assert(references!.Spells.Any(s=>s.SpellId==28846),"Set-bonus spell is searchable");
        referenceSearch.Text="28846";Assert(referenceView!.Cast<ReferenceEntry>().Any(s=>s.SpellId==28846),"Exact spell ID search");
        await RenderFeature("spell-catalog-preview");
        referenceSearch.Clear();await ShowReferencePage("Proc catalog");Assert(references.Procs.Any(p=>p.EffectId.HasValue)&&references.Procs.Any(p=>!p.EffectId.HasValue),"Proc definitions and unconfigured candidates are included");
        referenceSearch.Text="Chance on hit";Assert(!referenceView!.IsEmpty&&referenceView.Cast<ReferenceEntry>().All(p=>p.Matches("Chance on hit")),"Proc trigger search");
        await RenderFeature("proc-catalog-preview");
        referenceSearch.Text="__no_such_reference__";Assert(referenceView.IsEmpty&&referenceGrid.SelectedItem is null,"Empty reference results clear selection");
        referenceSearch.Clear();referenceFilters.Usage.SelectedIndex=1;Assert(!referenceView.IsEmpty&&referenceView.Cast<ReferenceEntry>().All(s=>s.UsedBySets),"Set usage filter");referenceFilters.Reset();referenceFilters.School.SelectedIndex=3;referenceFilters.Description.SelectedIndex=1;Assert(!referenceView.IsEmpty&&referenceView.Cast<ReferenceEntry>().All(s=>(s.SchoolMask&4)!=0&&s.HasDescription),"Combined school and description filters");referenceFilters.Reset();
        HistoryState.SelectedIndex=0;ShowRemovedHistory.IsChecked=true;ApplyHistoryFilters();Assert(historyView!.Cast<OverrideEntry>().All(e=>e.OriginalItem is not null),"Items without originals never appear in History, including removed entries");ShowRemovedHistory.IsChecked=false;
        await VerifyContentEditors(catalog,references);
        await VerifyItemAndBonusEditing(catalog,references);
        await VerifyDesigner(catalog,references);
        await VerifySetCreation(catalog,references);
        await VerifySafetyStatsAndBosses(catalog,references);
        await File.WriteAllTextAsync("artifacts/action-reference-verification.json",JsonSerializer.Serialize(new{spells=references.Spells.Count,itemEffects=references.Procs.Count(p=>p.EffectId.HasValue),candidates=references.Procs.Count(p=>!p.EffectId.HasValue),checks="Revert/reapply exact payload roundtrip, immutable archive, audit mirror, stale conflict rejection, shared dependencies unchanged, all test writes rolled back; history filters; complete read-only spell/proc catalogs and search"},new JsonSerializerOptions{WriteIndented=true}));
    }
    private async Task RenderFeature(string name)
    {
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();var content=(FrameworkElement)Content;
        var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(content);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create($"artifacts/{name}.png");encoder.Save(file);
    }
}
