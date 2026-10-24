using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace AnoWowItemEditor;

internal sealed class BonusValueWindow : Window
{
    internal BonusDraft? Result {get;private set;}
    private readonly Dictionary<int,TextBox> boxes=[];
    internal void SetVerificationValue(int id,string value)=>boxes[id].Text=value;
    internal bool CanApply=>apply.IsEnabled;
    private readonly Button apply=new(){Content="Apply values to set draft"};
    internal BonusValueWindow(BonusBlueprint source,TooltipData data,BonusDraft? existing=null)
    {
        Title=$"Edit set-specific bonus · {data.SpellName(source.Root)} · {source.Root}";Width=1100;Height=780;MinWidth=850;MinHeight=600;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(35,36,40));
        var layout=new DockPanel{Margin=new Thickness(20),Background=Background};Content=layout;
        var heading=new TextBlock{Text=Title,FontSize=21,Foreground=Brushes.LightSkyBlue,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(heading,Dock.Top);layout.Children.Add(heading);
        var help=new TextBlock{Text="Each value changes one effect of this set's private bonus. Read its meaning and unit before editing. Supported whole-number amounts have conservative editor limits; unsupported, scripted and scaling effects stay read-only. These checks do not certify custom server code as crash-free.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(help,Dock.Top);layout.Children.Add(help);
        var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);layout.Children.Add(bottom);var feedback=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSkyBlue,Margin=new Thickness(0,12,0,12)};bottom.Children.Add(feedback);var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};bottom.Children.Add(buttons);var cancel=new Button{Content="Cancel",IsCancel=true,Margin=new Thickness(0,0,12,0)};cancel.Click+=(_,_)=>DialogResult=false;buttons.Children.Add(cancel);buttons.Children.Add(apply);
        var fields=new StackPanel();layout.Children.Add(new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=fields});
        var original=new BonusDraft(source,new()).Description(data);
        var previews=new Grid{Margin=new Thickness(0,0,0,14)};previews.ColumnDefinitions.Add(new());previews.ColumnDefinitions.Add(new());fields.Children.Add(previews);var before=new StackPanel();var after=new StackPanel();previews.Children.Add(DesignerWindow.Preview(before,"CURRENT BONUS"));var right=DesignerWindow.Preview(after,"BONUS WITH DRAFT VALUES");Grid.SetColumn(right,1);previews.Children.Add(right);TooltipView.Fill(before,[new(data.SpellName(source.Root),ItemTooltip.Gold,Size:18),new(original,ItemTooltip.Green)]);
        foreach(var row in source.Rows["spell_effect"].OrderBy(r=>TooltipData.Number(r,"SpellID")).ThenBy(r=>TooltipData.Number(r,"EffectIndex"))){
            int id=TooltipData.Number(row,"ID"),spell=TooltipData.Number(row,"SpellID");var rule=BonusValueRules.For(row);
            fields.Children.Add(new TextBlock{Text=$"{data.SpellName(spell)} · Effect {TooltipData.Number(row,"EffectIndex")+1} · {SpellTypeNames.Effect(TooltipData.Number(row,"Effect"))} · {SpellTypeNames.Aura(TooltipData.Number(row,"EffectAura"))}\n{rule.Meaning}",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,6),Foreground=Brushes.LightSkyBlue});
            var line=new WrapPanel();fields.Children.Add(line);line.Children.Add(new TextBlock{Text=$"Current: {row.GetValueOrDefault("EffectBasePointsF")??"0"}  →  Draft:",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0)});
            var box=new TextBox{Text=existing?.Values.GetValueOrDefault(id)??row.GetValueOrDefault("EffectBasePointsF")??"0",IsReadOnly=!rule.Supported,Width=150,ToolTip=rule.Limits};boxes[id]=box;line.Children.Add(box);line.Children.Add(new TextBlock{Text=rule.Unit,Margin=new Thickness(10,0,0,0),VerticalAlignment=VerticalAlignment.Center});
            fields.Children.Add(new TextBlock{Text=rule.Limits,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray,Margin=new Thickness(0,5,0,0)});
        }
        void Preview(){try{var values=new Dictionary<int,string>();foreach(var row in source.Rows["spell_effect"]){int id=TooltipData.Number(row,"ID");if(BonusSpells.Editable(row)&&!OverrideEntry.Equivalent(boxes[id].Text,row.GetValueOrDefault("EffectBasePointsF")))values[id]=boxes[id].Text;}var draft=new BonusDraft(source,values);BonusSpells.Validate(draft);TooltipView.Fill(after,[new(data.SpellName(source.Root),ItemTooltip.Gold,Size:18),new(draft.Description(data),ItemTooltip.Green)]);feedback.Text=values.Count>0?$"{values.Count} effect value(s) changed. Apply updates the set draft; review and save the set to persist.":"Change a supported value to preview its result.";apply.IsEnabled=values.Count>0;Result=draft;}catch(Exception ex){feedback.Text=ex.Message;TooltipView.Fill(after,[new("Invalid draft · correct the value to preview", "#FF6666")]);apply.IsEnabled=false;Result=null;}}
        foreach(var box in boxes.Values)box.TextChanged+=(_,_)=>Preview();apply.Click+=(_,_)=>{Preview();if(apply.IsEnabled)DialogResult=true;};Preview();
    }
}
