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
    private async Task VerifyContentEditors(CatalogResult catalog,ReferenceCatalog refs)
    {
        static void Assert(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        var set=await ContentEditing.LoadAsync("Set",523,catalog.Tooltips.Sets[523].Members.Length);
        Assert(set.Initial.Count>=2,"Real set bonus fixture");
        int newThreshold=Enumerable.Range(1,set.MaxPieces).First(n=>set.Initial.SkipLast(1).All(r=>r.Pieces!=n));
        var setDraft=set.Initial.SkipLast(1).Select((r,n)=>n==0?r with{SpellId=r.SpellId==28846?28844:28846}:r).Append(new ContentRow(0,0,28846,newThreshold)).ToArray();ContentEditing.Validate(set,setDraft,refs);
        int weaponId=catalog.Items.Any(i=>i.Id==17182)?17182:catalog.Items.First(i=>i.ClassId==2&&catalog.Tooltips.ItemSpells.ContainsKey(i.Id)).Id;
        var weapon=await ContentEditing.LoadAsync("Proc",weaponId);
        Assert(weapon.Initial.Count>=2,"Real weapon proc fixture");
        var procDraft=weapon.Initial.SkipLast(1).Select((r,n)=>n==0?r with{Cooldown=12345}:r).Append(new ContentRow(0,0,weapon.Initial[^1].SpellId,Trigger:2,Cooldown:23456,Slot:weapon.Initial[^1].Slot)).ToArray();ContentEditing.Validate(weapon,procDraft,refs);
        bool invalid=false;try{ContentEditing.Validate(set,[set.Initial[0] with{Pieces=0}],refs);}catch(InvalidOperationException){invalid=true;}Assert(invalid,"Invalid piece threshold rejected");
        invalid=false;try{ContentEditing.Validate(weapon,[weapon.Initial[0] with{Cooldown=-2}],refs);}catch(InvalidOperationException){invalid=true;}Assert(invalid,"Invalid cooldown rejected");
        using var db=await DatabaseConnection.OpenAsync(CatalogRepository.Database);await ContentEditing.InitializeAsync(db);
        using(var gate=new MySqlCommand("SELECT GET_LOCK('anocore_studio_forge',8)",db))Assert(Convert.ToInt32(await gate.ExecuteScalarAsync())==1,"Content verification lock");
        var results=new List<object>();
        try{
            foreach(var (session,draft,name) in new[]{(set,setDraft,"Dreadnaught verification"),(weapon,procDraft,"Weapon verification")}){
                string? opId=null;
                using(var tx=await db.BeginTransactionAsync(System.Data.IsolationLevel.Serializable)){
                    try{
                        opId=await ContentEditing.SaveCoreAsync(db,tx,session,draft,name);var op=await ContentEditing.ReadAsync(db,tx,opId);
                        var savedScope=JsonSerializer.Deserialize<ContentScope>(op.Scope)!;
                        Assert(op.Active&&JsonSerializer.Deserialize<ItemSnapshot>(op.Before)!.ContentRows!.SequenceEqual(session.Initial),"Immutable semantic before snapshot");
                        Assert(JsonSerializer.Deserialize<ItemSnapshot>(op.After)!.ContentRows!.SequenceEqual(draft),"Immutable semantic modified snapshot");
                        string table=session.Scope.Kind=="Set"?"item_set_spell":"item_x_item_effect",dbc=session.Scope.Kind=="Set"?"ItemSetSpell":"ItemXItemEffect",parent=session.Scope.Kind=="Set"?"ItemSetID":"ItemID";
                        async Task<Dictionary<int,Dictionary<string,string>>> Effective(){var rows=session.Native[table].ToDictionary(p=>p.Key,p=>new Dictionary<string,string>(p.Value,StringComparer.OrdinalIgnoreCase));await TooltipData.Overlay(db,table,dbc,rows,tx);return rows.Where(p=>TooltipData.Number(p.Value,parent)==session.Scope.EntityId).ToDictionary();}
                        var effective=await Effective();Assert(effective.Count==draft.Length,"Added and removed rows become effective");
                        Assert(session.Initial.Where(r=>draft.All(d=>d.Id!=r.Id)).All(r=>!effective.ContainsKey(r.Id)),"Removed native records are disabled");
                        if(session.Scope.Kind=="Set")Assert(effective.Values.Any(r=>TooltipData.Number(r,"Threshold")==newThreshold)&&effective.Values.Any(r=>TooltipData.Number(r,"SpellID")==28846),"Set thresholds and spells saved");
                        else{
                            var after=JsonSerializer.Deserialize<ItemSnapshot>(op.After)!;var clones=after.Tables["item_effect"].Where(r=>!session.Scope.EffectIds.Contains(int.Parse(r["ID"]!))).ToList();Assert(clones.Count==2&&clones.Any(r=>r["CoolDownMSec"]=="12345")&&clones.Any(r=>r["CoolDownMSec"]=="23456"),"Modified and added assignments each get a separate effect");
                            foreach(int effect in session.Scope.EffectIds)Assert(JsonSerializer.Serialize(session.Expected.Tables["item_effect"].Where(r=>r["ID"]==effect.ToString()))==JsonSerializer.Serialize(after.Tables["item_effect"].Where(r=>r["ID"]==effect.ToString())),"Existing shared effect payloads are preserved");
                        }
                        bool stale=false;try{await ContentEditing.SaveCoreAsync(db,tx,session,draft,name);}catch(InvalidOperationException ex) when(ex.Message.StartsWith("Conflict:")){stale=true;}Assert(stale,"Stale content save rejected");
                        bool activeRemoval=false;try{await ContentEditing.SetOperationHiddenCoreAsync(db,tx,op,true);}catch(InvalidOperationException){activeRemoval=true;}Assert(activeRemoval,"Active content history cannot be removed");
                        await ContentEditing.ToggleCoreAsync(db,tx,op);var inactive=await ContentEditing.ReadAsync(db,tx,opId);Assert(!inactive.Active,"Content edit reverted");
                        var restored=await Effective();Assert(restored.Keys.Order().SequenceEqual(session.Initial.Select(r=>r.Id).Order()),"Native assignments return after revert");
                        if(session.Scope.Kind=="Set")foreach(var row in session.Initial)Assert(TooltipData.Number(restored[row.Id],"SpellID")==row.SpellId&&TooltipData.Number(restored[row.Id],"Threshold")==row.Pieces,"Original set values restored");
                        await ContentEditing.SetOperationHiddenCoreAsync(db,tx,inactive,true);var hidden=await ContentEditing.ReadAsync(db,tx,opId);Assert(hidden.Hidden&&!hidden.Active,"Inactive content history can be removed");await ContentEditing.SetOperationHiddenCoreAsync(db,tx,hidden,false);inactive=await ContentEditing.ReadAsync(db,tx,opId);Assert(!inactive.Hidden,"Removed content history can be restored");
                        await ContentEditing.ToggleCoreAsync(db,tx,inactive);var reapplied=await ContentEditing.ReadAsync(db,tx,opId);Assert(reapplied.Active&&reapplied.Before==op.Before&&reapplied.After==op.After,"Reapply preserves both immutable versions");Assert((await Effective()).Keys.Order().SequenceEqual(effective.Keys.Order()),"Reapply restores the same record IDs");
                        foreach(string audit in new[]{"foreverclassic_hotfixes.anocore_studio_changes","anocore_studio_backup.changes"}){using var check=new MySqlCommand($"SELECT COUNT(*) FROM {audit} WHERE Provenance=@source",db,tx);check.Parameters.AddWithValue("@source","AnoWoW Item Editor; operation "+opId);Assert(Convert.ToInt32(await check.ExecuteScalarAsync())==5,"Save/revert/reapply/removal/restoration audited and mirrored together");}
                        results.Add(new{session.Scope.Kind,session.Scope.EntityId,before=session.Initial.Count,after=draft.Length,checks="Save, native removal, shared effect preservation, stale conflict, revert, reapply, immutable versions and mirrored audit; all writes rolled back"});
                    }finally{await tx.RollbackAsync();}
                }
                Assert(HistoryActions.Canonical(session.Expected)==HistoryActions.Canonical(await ContentEditing.CaptureAsync(db,null,session.Scope)),"Content verification leaves game data unchanged");
                using var checkRollback=new MySqlCommand($"SELECT COUNT(*) FROM {ContentEditing.Operations} WHERE OperationId=@id",db);checkRollback.Parameters.AddWithValue("@id",opId);Assert(Convert.ToInt32(await checkRollback.ExecuteScalarAsync())==0,"Test operation rolled back");
            }
        }finally{using var release=new MySqlCommand("SELECT RELEASE_LOCK('anocore_studio_forge')",db);await release.ExecuteScalarAsync();}
        foreach(var (session,draft) in new[]{(set,setDraft),(weapon,procDraft)}){
            var editor=new ContentEditorWindow(session.Scope.Kind,catalog,refs){Owner=this};await editor.SelectAsync(new(session.Scope.EntityId,session.Scope.Kind=="Set"?"Dreadnaught's Battlegear":catalog.Items.First(i=>i.Id==weaponId).Name));editor.Draft.Clear();foreach(var row in draft)editor.Draft.Add(row);editor.Show();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);editor.UpdateLayout();var content=(FrameworkElement)editor.Content;
            var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(content);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create($"artifacts/{session.Scope.Kind.ToLowerInvariant()}-editor-preview.png"))png.Save(output);
            editor.Draft.Clear();foreach(var row in editor.Session!.Initial)editor.Draft.Add(row);editor.Close();
        }
        await File.WriteAllTextAsync("artifacts/content-editor-verification.json",JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
    }
}
