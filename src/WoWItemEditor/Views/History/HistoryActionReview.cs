using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
namespace WoWItemEditor;

internal sealed class HistoryActionReview : Window
{
    internal HistoryActionReview(HistoryPlan plan)
    {
        Title=(plan.Reapply?"Review reapply":"Review revert")+$" · {plan.Name}";Width=740;Height=560;MinWidth=550;MinHeight=440;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=new SolidColorBrush(Color.FromRgb(35,36,40));Foreground=Brushes.White;
        var layout=new DockPanel{Margin=new Thickness(24)};Content=layout;
        var title=new TextBlock{Text=$"{(plan.Reapply?"Reapply":"Revert")} {plan.Name}",FontSize=22,Foreground=new SolidColorBrush(Color.FromRgb(112,184,255)),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)};DockPanel.SetDock(title,Dock.Top);layout.Children.Add(title);
        var description=new TextBlock{Text=plan.Review,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)};DockPanel.SetDock(description,Dock.Top);layout.Children.Add(description);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)};DockPanel.SetDock(actions,Dock.Bottom);layout.Children.Add(actions);
        var cancel=new Button{Content="Cancel",IsCancel=true,Margin=new Thickness(0,0,12,0)};cancel.Click+=(_,_)=>DialogResult=false;actions.Children.Add(cancel);
        var apply=new Button{Content=plan.Reapply?"Reapply override":"Revert override",BorderBrush=Brushes.SkyBlue};apply.Click+=(_,_)=>DialogResult=true;actions.Children.Add(apply);
        var grid=new DataGrid{AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,RowHeight=30};
        grid.Columns.Add(new DataGridTextColumn{Header="Field",Binding=new Binding("Field"),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        grid.Columns.Add(new DataGridTextColumn{Header="Current",Binding=new Binding("Original"),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        grid.Columns.Add(new DataGridTextColumn{Header="After action",Binding=new Binding("Modified"),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        var textStyle=new Style(typeof(TextBlock));textStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty,Brushes.LightGray));
        foreach(DataGridTextColumn column in grid.Columns)column.ElementStyle=textStyle;
        grid.ItemsSource=HistoryActions.ReviewFields(plan);layout.Children.Add(grid);
    }
}
