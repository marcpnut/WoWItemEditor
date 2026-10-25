using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace WoWItemEditor;
internal sealed class EffectExplanationWindow : Window
{
    internal EffectExplanationWindow(string title,string before,string after)
    {
        Title=title;Width=1250;Height=800;MinWidth=950;MinHeight=600;Background=new SolidColorBrush(Color.FromRgb(35,36,40));WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new Thickness(20),Background=Background};Content=root;var heading=DesignerWindow.Text(title,22);heading.Foreground=Brushes.LightSkyBlue;heading.Margin=new Thickness(0,0,0,12);DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new());root.Children.Add(grid);
        foreach(var (label,text,col) in new[]{("SAVED EFFECT",before,0),("DRAFT EFFECT",after,1)}){var pane=new DockPanel{Margin=new Thickness(0,0,12,0)};Grid.SetColumn(pane,col);grid.Children.Add(pane);var caption=DesignerWindow.Text(label,13);caption.Margin=new Thickness(0,0,0,8);caption.Foreground=Brushes.LightSkyBlue;DockPanel.SetDock(caption,Dock.Top);pane.Children.Add(caption);pane.Children.Add(new TextBox{Text=text,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});}
    }
}
