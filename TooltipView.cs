using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace AnoWowItemEditor;

internal static class TooltipView
{
    internal static Brush Brush(string color)=>(Brush)new BrushConverter().ConvertFromString(color)!;
    internal static void Fill(StackPanel panel,IEnumerable<TooltipLine> lines,IEnumerable<TooltipLine>? other=null)
    {
        panel.Children.Clear();var previous=other?.Select(l=>l.Text+"|"+l.Right).ToHashSet();
        foreach(var line in lines){var row=new Grid{Margin=new Thickness(0,0,0,3)};row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});if(line.Right is not null)row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            if(previous is not null&&line.Text.Length>0&&!previous.Contains(line.Text+"|"+line.Right)){row.Background=Brush("#203526");row.ToolTip="Changed in this draft";}
            var label=new TextBlock{Text=line.Text,Foreground=Brush(line.Color),TextWrapping=TextWrapping.Wrap,FontSize=line.Size,FontWeight=line.Bold?FontWeights.SemiBold:FontWeights.Normal,Margin=new Thickness(line.Indent,0,0,0)};row.Children.Add(label);
            if(line.Right is not null){var right=new TextBlock{Text=line.Right,Foreground=Brush(line.Color),FontSize=line.Size,Margin=new Thickness(12,0,0,0),HorizontalAlignment=HorizontalAlignment.Right};Grid.SetColumn(right,1);row.Children.Add(right);}panel.Children.Add(row);
        }
    }
}
