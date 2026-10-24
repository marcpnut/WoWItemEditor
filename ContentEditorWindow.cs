using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
namespace AnoWowItemEditor;

internal sealed record ContentChoice(int Id,string Name){public string Label=>$"{Name} · {Id}";}
internal sealed class ContentEditorWindow : Window
{
    private readonly string kind;
    private readonly CatalogResult catalog;
    private readonly ReferenceCatalog references;
    private readonly List<ContentChoice> choices;
    private readonly ListBox entities=new(){DisplayMemberPath="Label"};
    private readonly TextBox search=new(),feedback=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,BorderThickness=new Thickness(0)};
    private readonly DataGrid rows=new(){RowHeight=32};
    private readonly ObservableCollection<ContentRow> draft=[];
    private readonly Dictionary<string,TextBox> fields=[];
    private readonly ComboBox trigger=new(){DisplayMemberPath="Label"};
    private readonly StackPanel currentPreview=new(),draftPreview=new();
    private readonly Button save=new(){Content="Review & save",IsEnabled=false};
    private readonly Button add=new(){Content="Add bonus"},change=new(){Content="Change selected"},remove=new(){Content="Remove selected"};
    private readonly StackPanel controls=new();
    private readonly TextBlock heading=new(){FontSize=22,Margin=new Thickness(0,0,0,10),Foreground=Brushes.LightSkyBlue,TextTrimming=TextTrimming.CharacterEllipsis};
    private ContentSession? session;
    private ContentChoice? choice;
    private int request;
    private bool busy,selecting;
    internal bool Saved {get;private set;}
    internal ContentSession? Session=>session;
    internal ObservableCollection<ContentRow> Draft=>draft;
    internal ContentEditorWindow(string kind,CatalogResult catalog,ReferenceCatalog references,int? initialId=null)
    {
        this.kind=kind;this.catalog=catalog;this.references=references;
        Title=kind=="Set"?"Set-bonus editor":"Weapon-proc editor";Width=1420;Height=930;MinWidth=1120;MinHeight=750;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(35,36,40));
        choices=kind=="Set"?catalog.Tooltips.Sets.Values.Where(s=>s.Members.Length>0).Select(s=>new ContentChoice(s.Id,s.Name)).OrderBy(c=>c.Name).ToList():catalog.Items.Where(i=>i.ClassId==2).Select(i=>new ContentChoice(i.Id,i.Name)).OrderBy(c=>c.Name).ToList();
        var layout=new DockPanel{Margin=new Thickness(20),Background=Background};Content=layout;
        heading.Text=Title;DockPanel.SetDock(heading,Dock.Top);layout.Children.Add(heading);
        feedback.Text="Choose "+(kind=="Set"?"an equipment set. Set membership stays unchanged.":"a weapon. Shared spell definitions stay unchanged.");feedback.Margin=new Thickness(0,12,0,0);feedback.MaxHeight=80;DockPanel.SetDock(feedback,Dock.Bottom);layout.Children.Add(feedback);
        var body=new Grid();body.ColumnDefinitions.Add(new(){Width=new GridLength(245)});body.ColumnDefinitions.Add(new(){Width=new GridLength(16)});body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});layout.Children.Add(body);
        var left=new DockPanel();body.Children.Add(left);var searchLabel=new TextBlock{Text=kind=="Set"?"Find set name or ID":"Find weapon name or ID",Margin=new Thickness(0,0,0,8)};DockPanel.SetDock(searchLabel,Dock.Top);left.Children.Add(searchLabel);DockPanel.SetDock(search,Dock.Top);search.Margin=new Thickness(0,0,0,10);left.Children.Add(search);left.Children.Add(entities);
        entities.Background=new SolidColorBrush(Color.FromRgb(40,42,47));entities.Foreground=Brushes.LightGray;entities.BorderThickness=new Thickness(0);entities.ItemsSource=choices;
        var right=new Grid();Grid.SetColumn(right,2);body.Children.Add(right);right.RowDefinitions.Add(new(){Height=GridLength.Auto});right.RowDefinitions.Add(new(){Height=new GridLength(180)});right.RowDefinitions.Add(new(){Height=GridLength.Auto});right.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var actions=new WrapPanel{Margin=new Thickness(0,0,0,10)};right.Children.Add(actions);
        add.Content=kind=="Set"?"Add bonus":"Add proc";
        foreach(var button in new[]{add,change,remove,save}){button.Margin=new Thickness(0,0,10,0);actions.Children.Add(button);}
        var reset=new Button{Content="Reload / discard draft"};reset.Click+=async(_,_)=>{if(choice is not null&&ConfirmDiscard())await SelectAsync(choice);};actions.Children.Add(reset);
        rows.ItemsSource=draft;var spellColumn=SpellPicker.Column("Spell","SpellId",2);spellColumn.Binding=new Binding("SpellId"){Converter=new SpellLabelConverter(references.Data)};rows.Columns.Add(spellColumn);rows.Columns.Add(SpellPicker.Column("ID","SpellId",.6));if(kind=="Set"){rows.Columns.Add(SpellPicker.Column("Pieces","Pieces",.6));rows.Columns.Add(SpellPicker.Column("Spec ID","Specialization",.6));rows.Columns.Add(SpellPicker.Column("Trait ID","Trait",.6));}else{rows.Columns.Add(SpellPicker.Column("Trigger","TriggerLabel"));rows.Columns.Add(SpellPicker.Column("Cooldown ms","Cooldown"));rows.Columns.Add(SpellPicker.Column("Slot","Slot",.5));}Grid.SetRow(rows,1);right.Children.Add(rows);
        var form=new WrapPanel{Margin=new Thickness(0,12,0,10)};controls.Children.Add(form);Grid.SetRow(controls,2);right.Children.Add(controls);
        foreach(string name in kind=="Set"?new[]{"Pieces","Specialization","Trait"}:new[]{"Cooldown ms","Category cooldown ms","Charges","Slot","Category ID","Specialization","Condition ID"}){
            var field=new StackPanel{Width=name.Length>15?155:120,Margin=new Thickness(0,0,10,6)};field.Children.Add(new TextBlock{Text=name,FontSize=11,Margin=new Thickness(0,0,0,5)});var box=new TextBox();fields[name]=box;field.Children.Add(box);form.Children.Add(field);
        }
        if(kind=="Proc"){
            var field=new StackPanel{Width=175,Margin=new Thickness(0,0,10,6)};field.Children.Add(new TextBlock{Text="Trigger",FontSize=11,Margin=new Thickness(0,0,0,5)});trigger.ItemsSource=new[]{0,1,2,5,6}.Select(n=>new FilterChoice(n,ReferenceCatalog.Trigger(n))).ToList();field.Children.Add(trigger);form.Children.Add(field);
        }
        var update=new Button{Content="Apply row settings to draft",Margin=new Thickness(0,16,0,0)};update.Click+=(_,_)=>UpdateRow();form.Children.Add(update);
        var explanation=new TextBlock{Text=kind=="Set"?"Select a row, change its pieces/spec/trait, then apply. Spec/trait 0 means unrestricted.":"Cooldowns use milliseconds; -1 uses the spell default. This configures item effects; global proc chance/PPM rules are shown in the spell picker and remain unchanged.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray,FontSize=11};controls.Children.Add(explanation);
        var previews=new Grid();previews.ColumnDefinitions.Add(new());previews.ColumnDefinitions.Add(new(){Width=new GridLength(14)});previews.ColumnDefinitions.Add(new());Grid.SetRow(previews,3);right.Children.Add(previews);
        AddPreview(previews,currentPreview,"CURRENT · BEFORE SAVE",0);AddPreview(previews,draftPreview,"DRAFT · AFTER SAVE",2);
        rows.SelectionChanged+=(_,_)=>LoadRow();draft.CollectionChanged+=(_,_)=>{save.IsEnabled=session is not null&&!busy&&ContentEditing.Changed(session,draft);RenderPreviews();};
        add.Click+=(_,_)=>PickSpell(false);change.Click+=(_,_)=>PickSpell(true);remove.Click+=(_,_)=>{if(rows.SelectedItem is ContentRow row)draft.Remove(row);};save.Click+=async(_,_)=>await Save();
        search.TextChanged+=(_,_)=>{entities.ItemsSource=choices.Where(c=>c.Id.ToString()==search.Text.Trim()||c.Name.Contains(search.Text.Trim(),StringComparison.OrdinalIgnoreCase)).ToList();};
        entities.SelectionChanged+=async(_,_)=>{if(selecting||entities.SelectedItem is not ContentChoice target)return;if(!ConfirmDiscard()){selecting=true;entities.SelectedItem=choice;selecting=false;return;}await SelectAsync(target);};
        Closing+=(_,e)=>{if(busy||!ConfirmDiscard())e.Cancel=true;};
        Loaded+=async(_,_)=>{if(initialId.HasValue){selecting=true;entities.SelectedItem=choices.FirstOrDefault(c=>c.Id==initialId);selecting=false;if(entities.SelectedItem is ContentChoice c)await SelectAsync(c);}};
        SetEnabled(false);
    }
    private bool ConfirmDiscard()=>session is null||!ContentEditing.Changed(session,draft)||MessageBox.Show(this,"Discard the unsaved draft?","Unsaved changes",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
    private void SetEnabled(bool enabled){controls.IsEnabled=rows.IsEnabled=add.IsEnabled=change.IsEnabled=remove.IsEnabled=enabled;save.IsEnabled=enabled&&session is not null&&ContentEditing.Changed(session,draft);}
    internal async Task SelectAsync(ContentChoice selected)
    {
        int token=++request;choice=selected;Title=(kind=="Set"?"Set-bonus editor · ":"Weapon-proc editor · ")+selected.Label;session=null;SetEnabled(false);draft.Clear();feedback.Text="Loading current definitions…";
        heading.Text=Title;selecting=true;entities.SelectedItem=choices.FirstOrDefault(c=>c.Id==selected.Id);if(entities.SelectedItem is not null)entities.ScrollIntoView(entities.SelectedItem);selecting=false;
        try{var loaded=await ContentEditing.LoadAsync(kind,selected.Id,kind=="Set"?catalog.Tooltips.Sets[selected.Id].Members.Length:17);if(token!=request)return;session=loaded;foreach(var row in loaded.Initial)draft.Add(row);SetEnabled(true);rows.SelectedIndex=draft.Count>0?0:-1;RenderPreviews();feedback.Text=$"{selected.Label} · Draft only. Review & save writes the changes. Reload the server after saving.";}
        catch(Exception ex){if(token==request)feedback.Text=ex.Message;}
    }
    private void LoadRow()
    {
        if(rows.SelectedItem is not ContentRow row)return;
        if(kind=="Set"){fields["Pieces"].Text=row.Pieces.ToString();fields["Specialization"].Text=row.Specialization.ToString();fields["Trait"].Text=row.Trait.ToString();}
        else{foreach(var (name,value) in new Dictionary<string,int>{["Cooldown ms"]=row.Cooldown,["Category cooldown ms"]=row.CategoryCooldown,["Charges"]=row.Charges,["Slot"]=row.Slot,["Category ID"]=row.Category,["Specialization"]=row.Specialization,["Condition ID"]=row.Condition})fields[name].Text=value.ToString();trigger.SelectedItem=trigger.Items.Cast<FilterChoice>().FirstOrDefault(c=>c.Id==row.Trigger);}
        string spellName=references.Spells.FirstOrDefault(s=>s.SpellId==row.SpellId)?.Name??$"Spell {row.SpellId}";feedback.Text=$"{spellName} · Spell {row.SpellId}\n{references.Data.SpellDescription(row.SpellId)}";
    }
    private void PickSpell(bool replace)
    {
        if(session is null||replace&&rows.SelectedItem is not ContentRow)return;
        var picker=new SpellPicker(references,kind=="Proc"){Owner=this};if(picker.ShowDialog()!=true||picker.Selected is not {} spell)return;
        var existing=replace?(ContentRow)rows.SelectedItem:new ContentRow(0,0,spell.SpellId,Slot:Enumerable.Range(0,5).FirstOrDefault(n=>draft.All(r=>r.Slot!=n),-1));
        var next=existing with{SpellId=spell.SpellId};
        if(kind=="Proc"&&spell.EffectId is int effect&&references.ItemEffects.TryGetValue(effect,out var e)){
            int N(string k)=>TooltipData.Number(e,k);next=next with{EffectId=effect,Trigger=N("TriggerType"),Cooldown=N("CoolDownMSec"),CategoryCooldown=N("CategoryCoolDownMSec"),Charges=N("Charges"),Category=N("SpellCategoryID"),Specialization=N("ChrSpecializationID"),Condition=N("PlayerConditionID")};
            session.EffectiveEffects.TryAdd(effect,new(e,StringComparer.OrdinalIgnoreCase));
        }
        if(replace)draft[draft.IndexOf(existing)]=next;else draft.Add(next);rows.SelectedItem=next;LoadRow();
    }
    private void UpdateRow()
    {
        if(session is null||rows.SelectedItem is not ContentRow row)return;
        try{int N(string key)=>int.Parse(fields[key].Text,CultureInfo.InvariantCulture);
            var next=kind=="Set"?row with{Pieces=N("Pieces"),Specialization=N("Specialization"),Trait=N("Trait")}:row with{Cooldown=N("Cooldown ms"),CategoryCooldown=N("Category cooldown ms"),Charges=N("Charges"),Slot=N("Slot"),Category=N("Category ID"),Specialization=N("Specialization"),Condition=N("Condition ID"),Trigger=(trigger.SelectedItem as FilterChoice)?.Id??-1};
            var candidate=draft.Select(r=>ReferenceEquals(r,row)?next:r).ToArray();ContentEditing.Validate(session,candidate,references);draft[draft.IndexOf(row)]=next;rows.SelectedItem=next;feedback.Text="Row settings applied to the draft. Review & save to commit.";
        }catch(Exception ex){feedback.Text="Row not applied: "+ex.Message;}
    }
    private async Task Save()
    {
        if(session is null||choice is null||busy)return;
        try{ContentRow[] frozen=draft.ToArray();ContentEditing.Validate(session,frozen,references);if(!ContentEditing.Changed(session,frozen))return;
            var review=new ContentReviewWindow(kind,choice.Label,Summary(session.Initial),Summary(frozen)){Owner=this};if(review.ShowDialog()!=true)return;
            busy=true;entities.IsEnabled=search.IsEnabled=false;SetEnabled(false);await ContentEditing.SaveAsync(session,frozen,choice.Name,references);Saved=true;await SelectAsync(choice);feedback.Text="Saved with before/after history. Reload the server and verify in game.";
        }catch(Exception ex){feedback.Text="Could not save: "+ex.Message;}
        finally{busy=false;entities.IsEnabled=search.IsEnabled=true;SetEnabled(session is not null);}
    }
    internal string Summary(IEnumerable<ContentRow> list)=>string.Join("\n\n",list.Select(r=>$"{references.Data.SpellName(r.SpellId)} · Spell {r.SpellId}\n"+(kind=="Set"?$"{r.Pieces} pieces · Spec {r.Specialization} · Trait {r.Trait}":$"{ReferenceCatalog.Trigger(r.Trigger)} · Cooldown {r.Cooldown} ms · Category cooldown {r.CategoryCooldown} ms · Charges {r.Charges} · Slot {r.Slot} · Category {r.Category} · Spec {r.Specialization} · Condition {r.Condition}")+"\n"+references.Data.SpellDescription(r.SpellId)));
    private void AddPreview(Grid grid,StackPanel panel,string title,int column)
    {
        var dock=new DockPanel();Grid.SetColumn(dock,column);grid.Children.Add(dock);var heading=new TextBlock{Text=title,Foreground=Brushes.LightSkyBlue,Margin=new Thickness(0,0,0,8)};DockPanel.SetDock(heading,Dock.Top);dock.Children.Add(heading);
        dock.Children.Add(new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=new Border{Background=new SolidColorBrush(Color.FromRgb(16,17,27)),BorderBrush=Brushes.SlateGray,BorderThickness=new Thickness(1),Padding=new Thickness(14),Child=panel}});
    }
    internal void RenderPreviews()
    {
        currentPreview.Children.Clear();draftPreview.Children.Clear();if(session is null||choice is null)return;
        foreach(var (panel,list) in new[]{(currentPreview,(IEnumerable<ContentRow>)session.Initial),(draftPreview,(IEnumerable<ContentRow>)draft)}){
            var data=PreviewData(catalog.Tooltips,references.Data);
            CatalogItem? item;
            if(kind=="Set"){
                var set=catalog.Tooltips.Sets[choice.Id];data.Sets[choice.Id]=set with{Bonuses=list.Select(r=>new SetBonus(r.Pieces,r.SpellId,r.Specialization)).OrderBy(b=>b.Pieces).ToArray()};item=catalog.Items.FirstOrDefault(i=>set.Members.Contains(i.Id)&&i.Number("ItemSet")==choice.Id);
                panel.Children.Add(new TextBlock{Text=$"{set.Name} (0/{set.Members.Length})",Foreground=Brushes.Gold,FontSize=16,Margin=new Thickness(0,0,0,8)});
                foreach(int member in set.Members)panel.Children.Add(new TextBlock{Text=catalog.Items.FirstOrDefault(i=>i.Id==member)?.Name??$"Item {member}",Foreground=Brushes.Gray,Margin=new Thickness(12,0,0,3),TextWrapping=TextWrapping.Wrap});
                foreach(var bonus in list.OrderBy(r=>r.Pieces))panel.Children.Add(new TextBlock{Text=$"({bonus.Pieces}) Set: {references.Data.SpellDescription(bonus.SpellId)}"+(bonus.Specialization>0?$" [Specialization {bonus.Specialization}]":"")+(bonus.Trait>0?$" [Trait subtree {bonus.Trait}]":""),Foreground=Brushes.Gray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)});
                continue;
            }else{item=catalog.Items.FirstOrDefault(i=>i.Id==choice.Id);data.ItemSpells[choice.Id]=list.Select(r=>new ItemSpellInfo(r.SpellId,r.Trigger,r.Cooldown)).ToList();}
            if(item is not null)foreach(var line in ItemTooltip.Build(item,data,catalog.Items.ToDictionary(i=>i.Id),60))panel.Children.Add(new TextBlock{Text=line.Text+(line.Right is null?"":"    "+line.Right),Foreground=(Brush)new BrushConverter().ConvertFromString(line.Color)!,TextWrapping=TextWrapping.Wrap,FontSize=line.Size,Margin=new Thickness(line.Indent,0,0,4)});
            if(item is null||kind=="Set"&&list.Any(r=>r.Trait>0))panel.Children.Add(new TextBlock{Text=Summary(list),TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray});
        }
    }
    internal static TooltipData PreviewData(TooltipData source,TooltipData spells)
    {
        var copy=new TooltipData{Calculations=source.Calculations};
        foreach(var p in source.Sets)copy.Sets[p.Key]=p.Value;foreach(var p in source.ItemSpells)copy.ItemSpells[p.Key]=p.Value;
        foreach(var p in source.Subclasses)copy.Subclasses[p.Key]=p.Value;foreach(var p in source.Classes)copy.Classes[p.Key]=p.Value;foreach(var p in source.Skills)copy.Skills[p.Key]=p.Value;foreach(var p in source.Factions)copy.Factions[p.Key]=p.Value;foreach(var p in source.Enchantments)copy.Enchantments[p.Key]=p.Value;
        foreach(var p in spells.Spells)copy.Spells[p.Key]=p.Value;foreach(var p in spells.Effects)copy.Effects[p.Key]=p.Value;foreach(var p in spells.Misc)copy.Misc[p.Key]=p.Value;foreach(var p in spells.Durations)copy.Durations[p.Key]=p.Value;return copy;
    }
}

internal sealed class SpellLabelConverter(TooltipData data) : IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>value is int id?data.SpellName(id):"";
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}

internal sealed class ContentReviewWindow : Window
{
    internal ContentReviewWindow(string kind,string name,string before,string after)
    {
        Title="Review "+kind+" changes · "+name;Width=1000;Height=650;MinWidth=760;MinHeight=480;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(35,36,40));
        var layout=new DockPanel{Margin=new Thickness(20)};Content=layout;var info=new TextBlock{Text=name+"\nCompare the current and proposed assignments. Saving records both versions. Reload the server and verify in game.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)};DockPanel.SetDock(info,Dock.Top);layout.Children.Add(info);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,15,0,0)};DockPanel.SetDock(buttons,Dock.Bottom);layout.Children.Add(buttons);var cancel=new Button{Content="Cancel",IsCancel=true,Margin=new Thickness(0,0,12,0)};cancel.Click+=(_,_)=>DialogResult=false;buttons.Children.Add(cancel);var save=new Button{Content="Save changes"};save.Click+=(_,_)=>DialogResult=true;buttons.Children.Add(save);
        var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=new GridLength(16)});grid.ColumnDefinitions.Add(new());layout.Children.Add(grid);
        foreach(var (text,col) in new[]{("CURRENT\n\n"+before,0),("AFTER SAVE\n\n"+after,2)}){var box=new TextBox{Text=text,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetColumn(box,col);grid.Children.Add(box);}
    }
}
