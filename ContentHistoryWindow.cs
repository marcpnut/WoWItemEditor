using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
namespace AnoWowItemEditor;

internal sealed class ContentHistoryWindow : Window
{
    private readonly DataGrid list=new();
    private readonly TextBox search=new(),detail=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly ComboBox state=new(){DisplayMemberPath="Label"};
    private readonly CheckBox removed=new(){Content="Show removed entries",Foreground=Brushes.LightGray};
    private readonly Button toggle=new(){Content="Revert / reapply",IsEnabled=false},hide=new(){Content="Remove from history",IsEnabled=false};
    private readonly TextBlock feedback=new(){TextWrapping=TextWrapping.Wrap};
    private List<ContentOperation> operations=[];
    private readonly ReferenceCatalog references;
    private bool busy;
    internal bool Changed {get;private set;}
    internal ContentHistoryWindow(ReferenceCatalog references)
    {
        this.references=references;Title="Item, set-bonus & weapon-proc edits";Width=1200;Height=800;MinWidth=900;MinHeight=600;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(35,36,40));
        var layout=new DockPanel{Margin=new Thickness(20)};Content=layout;
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);layout.Children.Add(top);top.Children.Add(new TextBlock{Text=Title,FontSize=24,Margin=new Thickness(0,0,0,12),Foreground=Brushes.LightSkyBlue});
        var filters=new WrapPanel{Margin=new Thickness(0,0,0,12)};top.Children.Add(filters);search.Width=350;search.Margin=new Thickness(0,0,12,0);search.ToolTip="Search name, entity ID or operation ID";filters.Children.Add(search);state.Width=150;state.Margin=new Thickness(0,0,12,0);state.ItemsSource=new[]{new FilterChoice(-1,"All states"),new(1,"Active"),new(0,"Inactive")};state.SelectedIndex=0;filters.Children.Add(state);filters.Children.Add(removed);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,12)};top.Children.Add(actions);toggle.Margin=hide.Margin=new Thickness(0,0,12,0);actions.Children.Add(toggle);actions.Children.Add(hide);var refresh=new Button{Content="Refresh"};refresh.Click+=async(_,_)=>await Load();actions.Children.Add(refresh);
        feedback.Margin=new Thickness(0,12,0,0);DockPanel.SetDock(feedback,Dock.Bottom);layout.Children.Add(feedback);
        var body=new Grid();body.ColumnDefinitions.Add(new());body.ColumnDefinitions.Add(new(){Width=new GridLength(14)});body.ColumnDefinitions.Add(new());layout.Children.Add(body);
        list.Columns.Add(SpellPicker.Column("Name","Name",2));list.Columns.Add(SpellPicker.Column("Type","Kind"));list.Columns.Add(SpellPicker.Column("ID","EntityId"));list.Columns.Add(SpellPicker.Column("Active","Active"));list.Columns.Add(SpellPicker.Column("Saved","RecordedUtc",1.5));body.Children.Add(list);Grid.SetColumn(detail,2);body.Children.Add(detail);
        search.TextChanged+=(_,_)=>Filter();state.SelectionChanged+=(_,_)=>Filter();removed.Checked+=(_,_)=>Filter();removed.Unchecked+=(_,_)=>Filter();list.SelectionChanged+=(_,_)=>Selection();
        toggle.Click+=async(_,_)=>await Toggle();hide.Click+=async(_,_)=>await Hide();Loaded+=async(_,_)=>await Load();Closing+=(_,e)=>e.Cancel=busy;
    }
    private async Task Load(){try{operations=await ContentEditing.ListAsync();Filter();}catch(Exception ex){feedback.Text=ex.Message;}}
    private void Filter(){string? selected=(list.SelectedItem as ContentOperation)?.Id;int active=(state.SelectedItem as FilterChoice)?.Id??-1;string query=search.Text.Trim();list.ItemsSource=operations.Where(o=>(!o.Hidden||removed.IsChecked==true||o.Active)&&(active<0||o.Active==(active==1))&&(query.Length==0||o.Name.Contains(query,StringComparison.OrdinalIgnoreCase)||o.EntityId.ToString()==query||o.Id.Equals(query,StringComparison.OrdinalIgnoreCase))).ToList();list.SelectedItem=((List<ContentOperation>)list.ItemsSource).FirstOrDefault(o=>o.Id==selected);if(list.SelectedItem is null)list.SelectedIndex=0;Selection();}
    private void Selection(){if(list.SelectedItem is not ContentOperation op){detail.Text="No matching edits.";toggle.IsEnabled=hide.IsEnabled=false;return;}toggle.Content=op.Active?"Revert…":"Reapply…";hide.Content=op.Hidden?"Restore to history":"Remove from history";toggle.IsEnabled=!busy;hide.IsEnabled=!busy&&!op.Active;detail.Text=$"{op.Name} · {op.Kind} {op.EntityId}\n{(op.Active?"ACTIVE":"INACTIVE")}{(op.Hidden?" · REMOVED":"")} · Version {op.Version}\nSaved UTC: {op.RecordedUtc}\nOperation: {op.Id}\n\nORIGINAL · BEFORE THIS EDIT\n{Summary(op.Before,op.Kind)}\n\nRETAINED MODIFIED VERSION\n{Summary(op.After,op.Kind)}\n\nSpell descriptions use the current reference catalog. Full raw snapshots remain in the audit database.";detail.ScrollToHome();}
    private string Summary(string json,string kind){var snapshot=JsonSerializer.Deserialize<ItemSnapshot>(json)!;if(kind is "Item" or "Clone")return ItemEditing.SnapshotSummary(snapshot);return snapshot.ContentRows is {Length:>0} rows?string.Join("\n\n",rows.Select(r=>$"{references.Data.SpellName(r.SpellId)} · Spell {r.SpellId}\n"+(kind=="Set"?$"{r.Pieces} pieces · Spec {r.Specialization} · Trait {r.Trait}":$"{ReferenceCatalog.Trigger(r.Trigger)} · Cooldown {r.Cooldown} ms · Category cooldown {r.CategoryCooldown} ms · Charges {r.Charges} · Slot {r.Slot} · Category {r.Category} · Spec {r.Specialization} · Condition {r.Condition}")+"\n"+references.Data.SpellDescription(r.SpellId))):"No assignments.";}
    private async Task Toggle(){if(busy||list.SelectedItem is not ContentOperation op)return;var review=new ContentReviewWindow(op.Kind,op.Name,Summary(op.Active?op.After:op.Before,op.Kind),Summary(op.Active?op.Before:op.After,op.Kind)){Owner=this};if(review.ShowDialog()!=true)return;busy=true;list.IsEnabled=false;Selection();try{await ContentEditing.ToggleAsync(op);Changed=true;await Load();feedback.Text="Saved. Reload the server and verify in game.";}catch(Exception ex){feedback.Text=ex.Message;}finally{busy=false;list.IsEnabled=true;Selection();}}
    private new async Task Hide(){if(busy||list.SelectedItem is not ContentOperation op||op.Active)return;busy=true;Selection();try{await ContentEditing.SetOperationHiddenAsync(op,!op.Hidden);await Load();feedback.Text=op.Hidden?"Restored to history.":"Removed from the list. Stored payloads and audit events are retained.";}catch(Exception ex){feedback.Text=ex.Message;}finally{busy=false;Selection();}}
}
