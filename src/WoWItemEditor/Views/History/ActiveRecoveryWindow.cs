using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace WoWItemEditor;
internal sealed class ActiveRecoveryWindow : Window
{
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray,Margin=new Thickness(0,12,0,12)};
    private readonly TextBox notes=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly DataGrid rows=new(){IsReadOnly=true,AutoGenerateColumns=false};
    private readonly Button scan=new(){Content="Scan / review active changes",Margin=new Thickness(0,0,12,0)},apply=new(){Content="Apply reviewed recovery",IsEnabled=false};
    private ActiveRecoveryPlan? plan;
    private bool busy;
    internal bool Applied {get;private set;}
    internal ActiveRecoveryWindow()
    {
        Title="Recover all active changes";Width=1050;Height=740;MinWidth=800;MinHeight=550;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(35,36,40));
        var root=new DockPanel{Margin=new Thickness(20)};Content=root;
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock{Text=Title,FontSize=24,Foreground=Brushes.LightSkyBlue});
        top.Children.Add(new TextBlock{Text="Restore retained item, set, weapon-proc and drop changes after a server upgrade. This includes every active history entry, regardless of filters. Inactive entries stay inactive. Review the scan before applying; affected live definitions will be replaced.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.White,Margin=new Thickness(0,12,0,12)});
        var buttons=new StackPanel{Orientation=Orientation.Horizontal};buttons.Children.Add(scan);buttons.Children.Add(apply);top.Children.Add(buttons);top.Children.Add(status);
        DockPanel.SetDock(notes,Dock.Bottom);notes.Height=180;notes.Margin=new Thickness(0,12,0,0);root.Children.Add(notes);
        rows.Columns.Add(SpellPicker.Column("Name","Name",2));rows.Columns.Add(SpellPicker.Column("Type","Kind"));rows.Columns.Add(SpellPicker.Column("ID","EntityId"));rows.Columns.Add(SpellPicker.Column("Status","Status"));root.Children.Add(rows);
        scan.Click+=async(_,_)=>await Execute(false);apply.Click+=async(_,_)=>await Execute(true);Closing+=(_,e)=>e.Cancel=busy;
    }
    private async Task Execute(bool write)
    {
        if(busy||write&&plan is null)return;busy=true;scan.IsEnabled=apply.IsEnabled=false;
        void Progress(string text)=>Dispatcher.Invoke(()=>status.Text=text);
        try
        {
            var result=await Task.Run(async()=>write?await ActiveChangeRecovery.Apply(plan!,Progress):await ActiveChangeRecovery.Prepare(Progress));
            rows.ItemsSource=result.Rows;notes.Text=string.Join("\n\n",result.Notes);
            if(write){Applied=true;plan=null;status.Text=$"Recovered {result.Rows.Count} active changes. History remains active. Restart the worldserver and verify in game. A backup of the previous live state and history was saved.";}
            else{plan=result;status.Text=$"Ready: {result.Rows.Count} active changes checked. The scan was rolled back; no game data was changed. Review the entries and notes, then apply.";}
        }
        catch(Exception ex){plan=null;status.Text="Recovery blocked. No recovery changes were committed. Scan again after resolving the issue.";notes.Text=ex.Message;}
        finally{busy=false;scan.IsEnabled=true;apply.IsEnabled=plan is not null;}
    }
}
