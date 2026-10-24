using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace AnoWowItemEditor;

internal sealed class BonusValueWindow : Window
{
    internal BonusDraft? Result {get;private set;}
    internal BonusValueWindow(BonusBlueprint source,TooltipData data,BonusDraft? existing=null)
    {
        Title=$"Edit set-specific bonus · {data.SpellName(source.Root)} · {source.Root}";Width=920;Height=660;MinWidth=700;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(35,36,40));
        var layout=new DockPanel{Margin=new Thickness(20),Background=Background};Content=layout;var heading=new TextBlock{Text=Title,FontSize=21,Foreground=Brushes.LightSkyBlue,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(heading,Dock.Top);layout.Children.Add(heading);
        var help=new TextBlock{Text="Change the fixed effect amounts below. Saving the set creates private copies of this bonus's spells. Original spells and other sets keep their existing values. Scaling effects are shown read-only.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(help,Dock.Top);layout.Children.Add(help);
        var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);layout.Children.Add(bottom);var feedback=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSkyBlue,Margin=new Thickness(0,12,0,12)};bottom.Children.Add(feedback);var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};bottom.Children.Add(buttons);var cancel=new Button{Content="Cancel",IsCancel=true,Margin=new Thickness(0,0,12,0)};cancel.Click+=(_,_)=>DialogResult=false;buttons.Children.Add(cancel);var apply=new Button{Content="Apply values to set draft"};buttons.Children.Add(apply);
        var fields=new StackPanel();layout.Children.Add(new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=fields});var boxes=new Dictionary<int,TextBox>();
        foreach(var row in source.Rows["spell_effect"].OrderBy(r=>TooltipData.Number(r,"SpellID")).ThenBy(r=>TooltipData.Number(r,"EffectIndex"))){
            int id=TooltipData.Number(row,"ID"),spell=TooltipData.Number(row,"SpellID");var label=new TextBlock{Text=$"{data.SpellName(spell)} · Spell {spell} · Effect {TooltipData.Number(row,"EffectIndex")+1} · Difficulty {TooltipData.Number(row,"DifficultyID")}\nEffect type {TooltipData.Number(row,"Effect")} · Aura {TooltipData.Number(row,"EffectAura")}",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,6)};fields.Children.Add(label);var box=new TextBox{Text=existing?.Values.GetValueOrDefault(id)??row.GetValueOrDefault("EffectBasePointsF")??"0",IsReadOnly=!BonusSpells.Editable(row),Width=180,HorizontalAlignment=HorizontalAlignment.Left};boxes[id]=box;fields.Children.Add(box);
        }
        void Preview(){try{var values=new Dictionary<int,string>();foreach(var row in source.Rows["spell_effect"]){int id=TooltipData.Number(row,"ID");if(BonusSpells.Editable(row)&&!OverrideEntry.Equivalent(boxes[id].Text,row.GetValueOrDefault("EffectBasePointsF")))values[id]=boxes[id].Text;}var draft=new BonusDraft(source,values);BonusSpells.Validate(draft);feedback.Text="DRAFT: "+draft.Description(data);apply.IsEnabled=values.Count>0;Result=draft;}catch(Exception ex){feedback.Text=ex.Message;apply.IsEnabled=false;}}
        foreach(var box in boxes.Values)box.TextChanged+=(_,_)=>Preview();apply.Click+=(_,_)=>{Preview();if(apply.IsEnabled)DialogResult=true;};Preview();
    }
}
