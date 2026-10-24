using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
namespace AnoWowItemEditor;

internal sealed class EditableItemField(string key,string value) : INotifyPropertyChanged
{
    public string Key {get;}=key;
    private string text=value;
    public string Value {get=>text;set{if(text==value)return;text=value;PropertyChanged?.Invoke(this,new(nameof(Value)));}}
    public event PropertyChangedEventHandler? PropertyChanged;
}
internal sealed class ItemEditorWindow : Window
{
    private readonly CatalogResult catalog;
    private readonly ItemKindFilter browseKinds;
    private readonly ListBox items=new(){DisplayMemberPath="Label"};
    private readonly TextBox search=new(),level=new(){Text="60",Width=55};
    private readonly TextBlock heading=new(){FontSize=22,Foreground=Brushes.LightSkyBlue,Margin=new Thickness(0,0,0,12),TextTrimming=TextTrimming.CharacterEllipsis};
    private readonly TextBlock feedback=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)};
    private readonly Dictionary<string,TextBox> boxes=[];
    private readonly Dictionary<string,ComboBox> combos=[];
    private readonly List<(ComboBox Type,TextBox Amount)> stats=[];
    private readonly HashSet<int> statErrors=[];
    private readonly DataGrid advanced=new();
    private readonly StackPanel beforePreview=new(),afterPreview=new();
    private readonly CheckBox showOriginal=new(){Content="Show original",Margin=new Thickness(18,0,0,0),VerticalAlignment=VerticalAlignment.Center,IsEnabled=false};
    private TextBlock beforeTitle=null!;
    internal bool ShowingOriginal=>showOriginal.IsChecked==true;
    internal CatalogItem? ComparisonItem=>ShowingOriginal?session?.Native:session?.Current;
    internal void ShowOriginal(bool value){showOriginal.IsChecked=value&&session?.Native is not null;}
    private readonly TabControl tabs=new();
    private readonly Button save=new(){Content="Review & save",IsEnabled=false};
    private readonly List<ContentChoice> choices;
    private List<EditableItemField> raw=[];
    private ItemEditSession? session;
    private readonly bool duplicateMode;
    private readonly CatalogItem? setDraft;
    internal CatalogItem? AcceptedDraft {get;private set;}
    private CloneBlueprint? cloneSource;
    private Dictionary<string,string> fields=[];
    private bool updating,busy,selecting;
    private int previewLevel=60,request;
    internal bool Saved {get;private set;}
    internal ItemEditSession? Session=>session;
    internal CatalogItem Draft=>new(fields,true);
    internal ItemEditorWindow(CatalogResult catalog,int? initialId=null,bool duplicate=false,CatalogItem? draft=null)
    {
        this.catalog=catalog;browseKinds=new(catalog.Tooltips);duplicateMode=duplicate;setDraft=draft;if(draft is not null)save.Content="Apply to set draft";Title=duplicate?"Duplicate item":"Item editor";Width=1410;Height=870;MinWidth=1160;MinHeight=740;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(35,36,40));
        choices=catalog.Items.Select(i=>new ContentChoice(i.Id,i.Name)).ToList();
        var layout=new DockPanel{Margin=new Thickness(20),Background=Background};Content=layout;heading.Text=Title;DockPanel.SetDock(heading,Dock.Top);layout.Children.Add(heading);DockPanel.SetDock(feedback,Dock.Bottom);layout.Children.Add(feedback);
        var body=new Grid();body.ColumnDefinitions.Add(new(){Width=new GridLength(230)});body.ColumnDefinitions.Add(new(){Width=new GridLength(14)});body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});layout.Children.Add(body);
        var left=new DockPanel();body.Children.Add(left);var label=new TextBlock{Text="Find item name or ID",Margin=new Thickness(0,0,0,8)};DockPanel.SetDock(label,Dock.Top);left.Children.Add(label);search.Margin=new Thickness(0,0,0,10);DockPanel.SetDock(search,Dock.Top);left.Children.Add(search);items.Background=new SolidColorBrush(Color.FromRgb(40,42,47));items.Foreground=Brushes.LightGray;items.ItemsSource=choices;DockPanel.SetDock(browseKinds,Dock.Top);left.Children.Add(browseKinds);browseKinds.Changed+=FilterBrowse;left.Children.Add(items);
        var editor=new DockPanel();Grid.SetColumn(editor,2);body.Children.Add(editor);var actions=new WrapPanel{Margin=new Thickness(0,0,0,10)};DockPanel.SetDock(actions,Dock.Top);editor.Children.Add(actions);actions.Children.Add(save);var reset=new Button{Content="Reload / discard draft",Margin=new Thickness(8,0,0,0)};actions.Children.Add(reset);var copy=new Button{Content="Duplicate as new item",Margin=new Thickness(8,0,0,0)};actions.Children.Add(copy);if(setDraft is not null)copy.Visibility=Visibility.Collapsed;copy.Click+=(_,_)=>{if(session is null||busy)return;var w=new ItemEditorWindow(catalog,session.Current.Id,true){Owner=this};w.ShowDialog();if(w.Saved)Saved=true;};var balance=new Button{Content="Check draft balance",Margin=new Thickness(8,0,0,0)};actions.Children.Add(balance);balance.Click+=(_,_)=>{if(session is null)return;var panel=new BudgetPanel();panel.Update(Draft,catalog,previewLevel);new Window{Title="Draft budget & balance",Width=1000,Height=760,MinWidth=800,MinHeight=650,Background=Background,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=panel}.ShowDialog();};
        var dropLocations=new Button{Content="Edit drop locations…",Margin=new Thickness(8,0,0,0)};actions.Children.Add(dropLocations);dropLocations.Visibility=setDraft is not null||duplicateMode?Visibility.Collapsed:Visibility.Visible;dropLocations.Click+=async(_,_)=>{if(session is null||busy)return;if(ItemEditing.Changes(session,Draft).Count>0){feedback.Text="Save or discard the item draft before editing its drop locations.";return;}try{busy=true;var locations=new DropLocationWindow(session.Current,persist:true){Owner=this};locations.ShowDialog();if(locations.Saved){Saved=true;await SelectAsync(session.Current.Id);feedback.Text="Drop locations saved with revert/reapply history.";}}catch(Exception ex){feedback.Text=ex.Message;}finally{busy=false;}};
        var previewControls=new WrapPanel{Margin=new Thickness(0,0,0,10)};DockPanel.SetDock(previewControls,Dock.Top);editor.Children.Add(previewControls);previewControls.Children.Add(new TextBlock{Text="Preview level",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)});previewControls.Children.Add(level);var applyLevel=new Button{Content="Apply level",Margin=new Thickness(8,0,0,0)};previewControls.Children.Add(applyLevel);previewControls.Children.Add(showOriginal);showOriginal.Checked+=(_,_)=>Render();showOriginal.Unchecked+=(_,_)=>Render();tabs.Height=250;DockPanel.SetDock(tabs,Dock.Top);editor.Children.Add(tabs);tabs.SelectionChanged+=(_,e)=>{if(ReferenceEquals(e.Source,tabs)&&tabs.SelectedIndex==1)RefreshStats();};
        var common=new WrapPanel();tabs.Items.Add(new TabItem{Header="Item fields",Content=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=common}});
        void Field(string key,string title,int width=170){var field=new StackPanel{Width=width,Margin=new Thickness(0,0,10,10)};field.Children.Add(new TextBlock{Text=title,FontSize=11,Margin=new Thickness(0,0,0,5)});var box=new TextBox{ToolTip=key};boxes[key]=box;box.TextChanged+=(_,_)=>{if(!updating&&session is not null){fields[key]=box.Text;Changed();}};field.Children.Add(box);common.Children.Add(field);}
        void Choice(string key,string title,IEnumerable<FilterChoice> values,int width=170){var field=new StackPanel{Width=width,Margin=new Thickness(0,0,10,10)};field.Children.Add(new TextBlock{Text=title,FontSize=11,Margin=new Thickness(0,0,0,5)});var combo=new ComboBox{DisplayMemberPath="Label",ItemsSource=values.ToList()};combos[key]=combo;combo.SelectionChanged+=(_,_)=>{if(!updating&&session is not null&&combo.SelectedItem is FilterChoice value){fields[key]=value.Id.ToString();if(key=="ClassID")RefreshSubclassChoices(true);Changed();}};field.Children.Add(combo);common.Children.Add(field);}
        Field("Display","Item name",350);Field("Description","Flavor text",350);
        Choice("OverallQualityID","Rarity",CatalogItem.Qualities.Select((n,id)=>new FilterChoice(id,n)));Choice("InventoryType","Equipment slot",CatalogItem.Slots.Select(p=>new FilterChoice(p.Key,p.Value)));
        Field("ItemLevel","Item level");Field("RequiredLevel","Required level");Choice("ClassID","Item class",CatalogItem.Classes.Select(p=>new FilterChoice(p.Key,p.Value)));Choice("SubclassID","Subclass",Array.Empty<FilterChoice>());
        Choice("AllowableClass","Allowed classes",new[]{new FilterChoice(-1,"All classes")}.Concat(catalog.Tooltips.Classes.OrderBy(p=>p.Key).Select(p=>new FilterChoice(1<<(p.Key-1),p.Value+" only"))),350);
        Choice("Bonding","Binding",new[]{new FilterChoice(0,"None"),new(1,"Bind on pickup"),new(2,"Bind on equip"),new(3,"Bind on use"),new(4,"Quest item"),new(5,"Quest item (5)")});
        Choice("ItemSet","Equipment set",new[]{new FilterChoice(0,"No set")}.Concat(catalog.Tooltips.Sets.Values.OrderBy(s=>s.Name).Select(s=>new FilterChoice(s.Id,$"{s.Name} · {s.Id}"))),350);
        foreach(var (key,title) in new[]{("ItemDelay","Weapon speed (ms)"),("DmgVariance","Damage variance"),("RequiredSkill","Required skill ID"),("RequiredSkillRank","Required skill rank"),("SellPrice","Sell price (copper)"),("BuyPrice","Buy price (copper)"),("Stackable","Stack size"),("MaxCount","Max count / unique"),("SocketType1","Socket 1 type"),("SocketType2","Socket 2 type"),("SocketType3","Socket 3 type"),("SocketMatchEnchantmentId","Socket bonus enchant ID")})Field(key,title);
        var statPanel=new StackPanel();tabs.Items.Add(new TabItem{Header="Stats",Content=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=statPanel}});statPanel.Children.Add(new TextBlock{Text="Stat type and amount changes update the draft immediately. Review & save persists them. Changing item level or rarity recalculates the resulting stats. Armor and weapon damage follow this build's level/quality tables.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,12)});
        for(int n=1;n<=10;n++){var row=new DockPanel{Margin=new Thickness(0,0,0,8)};row.Children.Add(new TextBlock{Text=n.ToString(),Width=24,VerticalAlignment=VerticalAlignment.Center});var type=new ComboBox{Width=245,DisplayMemberPath="Label",ItemsSource=new[]{new FilterChoice(-1,"No stat")}.Concat(ItemTooltip.StatNames.Select(p=>new FilterChoice(p.Key,p.Value))).ToList()};row.Children.Add(type);var amount=new TextBox{Width=100,Margin=new Thickness(8,0,0,0)};row.Children.Add(amount);stats.Add((type,amount));int index=n;type.SelectionChanged+=(_,_)=>UpdateStatRow(index);amount.TextChanged+=(_,_)=>UpdateStatRow(index);statPanel.Children.Add(row);}
        var applyStats=new Button{Content="Validate stat values",HorizontalAlignment=HorizontalAlignment.Left};applyStats.Click+=(_,_)=>ApplyStats();statPanel.Children.Add(applyStats);
        var rawPanel=new DockPanel();var rawHelp=new TextBlock{Text="Stored server fields. Allocation fields are not final stat amounts. Item ID and build provenance are read-only.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,10)};DockPanel.SetDock(rawHelp,Dock.Top);rawPanel.Children.Add(rawHelp);advanced.IsReadOnly=false;advanced.RowHeight=30;advanced.Columns.Add(SpellPicker.Column("Field","Key",1.4));advanced.Columns[0].IsReadOnly=true;var valueColumn=SpellPicker.Column("Stored value","Value",1);valueColumn.Binding=new Binding("Value"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged};advanced.Columns.Add(valueColumn);rawPanel.Children.Add(advanced);tabs.Items.Add(new TabItem{Header="Advanced fields",Content=rawPanel});
        var previews=new Grid();previews.ColumnDefinitions.Add(new());previews.ColumnDefinitions.Add(new(){Width=new GridLength(14)});previews.ColumnDefinitions.Add(new());editor.Children.Add(previews);AddPreview(previews,beforePreview,"CURRENT · BEFORE SAVE",0);AddPreview(previews,afterPreview,"DRAFT · AFTER SAVE",2);
        save.Click+=async(_,_)=>await Save();reset.Click+=async(_,_)=>{if(session is not null&&Discard())await SelectAsync(session.Current.Id);};applyLevel.Click+=(_,_)=>{if(!int.TryParse(level.Text,out int n)||n is <1 or >60){feedback.Text="Enter a preview level from 1 to 60.";return;}previewLevel=n;RefreshStats();Changed();};
        search.TextChanged+=(_,_)=>FilterBrowse();
        items.SelectionChanged+=async(_,_)=>{if(selecting||items.SelectedItem is not ContentChoice selected)return;if(!Discard()){selecting=true;items.SelectedItem=items.Items.Cast<ContentChoice>().FirstOrDefault(c=>c.Id==session?.Current.Id);selecting=false;return;}await SelectAsync(selected.Id);};
        Closing+=(_,e)=>{if(busy||!Discard())e.Cancel=true;};Loaded+=async(_,_)=>{if(initialId.HasValue)await SelectAsync(initialId.Value);};tabs.IsEnabled=false;FilterBrowse();
    }
    private void FilterBrowse(){selecting=true;int? id=session?.Current.Id;items.ItemsSource=catalog.Items.Where(i=>browseKinds.Matches(i)&&(search.Text.Trim().Length==0||i.Id.ToString()==search.Text.Trim()||i.Name.Contains(search.Text.Trim(),StringComparison.OrdinalIgnoreCase))).Select(i=>new ContentChoice(i.Id,i.Name+" · "+i.SubtypeName)).ToList();items.SelectedItem=items.Items.Cast<ContentChoice>().FirstOrDefault(c=>c.Id==id);selecting=false;}
    private bool Discard()=>session is null||ItemEditing.Changes(session,Draft).Count==0||MessageBox.Show(this,"Discard the unsaved item draft?","Unsaved changes",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
    internal async Task SelectAsync(int id)
    {
        int token=++request;session=null;tabs.IsEnabled=save.IsEnabled=false;feedback.Text="Loading current item fields…";
        try{var loadedClone=duplicateMode?await ItemCloning.LoadAsync(id):null;var loaded=loadedClone?.Item??await ItemEditing.LoadAsync(id);if(token!=request)return;cloneSource=loadedClone;session=loaded;fields=new(setDraft?.Id==id?setDraft.Fields:loaded.Current.Fields,StringComparer.OrdinalIgnoreCase);if(duplicateMode)fields["Display"]=loaded.Current.Name+" Copy";heading.Text=Title=duplicateMode?$"New item from {loaded.Current.Name} · source {id}":$"Item editor · {loaded.Current.Name} · {id}";selecting=true;items.SelectedItem=items.Items.Cast<ContentChoice>().FirstOrDefault(c=>c.Id==id);if(items.SelectedItem is not null)items.ScrollIntoView(items.SelectedItem);selecting=false;if(setDraft is not null){items.IsEnabled=search.IsEnabled=false;combos["ItemSet"].IsEnabled=false;heading.Text=Title="Set piece draft · "+Draft.Name;}showOriginal.IsEnabled=loaded.Native is not null;showOriginal.ToolTip=loaded.Native is null?"No installed original definition is available.":"Compare with the original installed item; the draft stays unchanged.";if(loaded.Native is null)showOriginal.IsChecked=false;RefreshFields();RefreshStats();tabs.IsEnabled=true;Changed();}catch(Exception ex){feedback.Text=ex.Message;}
    }
    private void RefreshFields()
    {
        if(session is null)return;updating=true;RefreshSubclassChoices(false);foreach(var (key,box) in boxes)box.Text=fields.GetValueOrDefault(key)??"0";foreach(var (key,combo) in combos){int n=Draft.Number(key);var values=combo.Items.Cast<FilterChoice>().ToList();if(values.All(v=>v.Id!=n)){values.Add(new(n,key=="AllowableClass"?string.Join(", ",catalog.Tooltips.Classes.Where(p=>(n&(1<<(p.Key-1)))!=0).Select(p=>p.Value))+$" · mask {n}":$"Unknown {n}"));combo.ItemsSource=values;}combo.SelectedItem=values.First(v=>v.Id==n);}
        raw=session.Columns.Where(c=>c.Name is not ("ID" or "VerifiedBuild")).Select(c=>new EditableItemField(c.Name,fields.GetValueOrDefault(c.Name)??c.Default??"")).ToList();foreach(var row in raw)row.PropertyChanged+=(_,_)=>{if(updating)return;fields[row.Key]=row.Value;if(row.Key is "ClassID" or "SubclassID")RefreshSubclassChoices(row.Key=="ClassID");updating=true;if(boxes.TryGetValue(row.Key,out var box))box.Text=row.Value;if(combos.TryGetValue(row.Key,out var combo))combo.SelectedItem=combo.Items.Cast<FilterChoice>().FirstOrDefault(c=>c.Id==Draft.Number(row.Key));updating=false;Changed();};advanced.ItemsSource=raw;updating=false;
    }
    private void RefreshStats(){if(session is null)return;statErrors.Clear();var values=catalog.Tooltips.Calculations.Evaluate(Draft,previewLevel);updating=true;for(int n=1;n<=10;n++){var (type,amount)=stats[n-1];int code=Draft.Number("StatModifierBonusStat"+n);if(type.Items.Cast<FilterChoice>().All(c=>c.Id!=code)){var valuesList=type.Items.Cast<FilterChoice>().ToList();valuesList.Add(new(code,$"Stat {code}"));type.ItemsSource=valuesList;}type.SelectedItem=type.Items.Cast<FilterChoice>().First(c=>c.Id==code);amount.Text=Draft.Number("StatPercentEditor"+n)==0?"0":values.Stats.TryGetValue(n,out int value)?value.ToString():"[scales]";}updating=false;}
    private void UpdateStatRow(int index)
    {
        if(updating||session is null)return;
        try{var (type,amount)=stats[index-1];int code=(type.SelectedItem as FilterChoice)?.Id??-1;string suffix=index.ToString();var copy=new Dictionary<string,string>(fields,StringComparer.OrdinalIgnoreCase);var original=catalog.Tooltips.Calculations.Evaluate(Draft,previewLevel);copy["StatModifierBonusStat"+suffix]=code.ToString();
            if(code<0)copy["StatPercentEditor"+suffix]="0";
            else{if(!int.TryParse(amount.Text,out int desired))throw new InvalidOperationException("Enter a whole stat amount.");if(!original.Stats.TryGetValue(index,out int current)||current!=desired)copy["StatPercentEditor"+suffix]=catalog.Tooltips.Calculations.AllocationFor(new(copy),index,desired,previewLevel).ToString();}
            fields=copy;statErrors.Remove(index);amount.BorderBrush=Brushes.SlateGray;Changed();
        }catch(Exception ex){statErrors.Add(index);stats[index-1].Amount.BorderBrush=Brushes.IndianRed;save.IsEnabled=false;feedback.Text=$"Stat {index}: {ex.Message}";}
    }
    internal void EditStatForVerification(int index,int code,string amount){stats[index-1].Type.SelectedItem=stats[index-1].Type.Items.Cast<FilterChoice>().Single(c=>c.Id==code);stats[index-1].Amount.Text=amount;}
    private void ApplyStats(){if(session is null)return;try{var copy=new Dictionary<string,string>(fields,StringComparer.OrdinalIgnoreCase);var calculated=catalog.Tooltips.Calculations.Evaluate(Draft,previewLevel);for(int n=1;n<=10;n++){var (type,amount)=stats[n-1];int code=(type.SelectedItem as FilterChoice)?.Id??-1;string suffix=n.ToString();if(amount.Text=="[scales]"&&code==Draft.Number("StatModifierBonusStat"+suffix))continue;if(!int.TryParse(amount.Text,out int value))throw new InvalidOperationException($"Stat {n}: enter a whole number.");if(code==Draft.Number("StatModifierBonusStat"+suffix)&&calculated.Stats.TryGetValue(n,out int original)&&original==value)continue;copy["StatModifierBonusStat"+suffix]=code.ToString();copy["StatPercentEditor"+suffix]=(code<0?0:catalog.Tooltips.Calculations.AllocationFor(new(copy),n,value,previewLevel)).ToString();}fields=copy;RefreshFields();RefreshStats();Changed();feedback.Text="Stat values applied to the draft.";}catch(Exception ex){feedback.Text=ex.Message;}}
    internal void SetDraftValue(string key,string value){fields[key]=value;RefreshFields();RefreshStats();Changed();}
    internal CatalogItem AcceptSetDraft(){if(setDraft is null||session is null)throw new InvalidOperationException("This is not a set-piece draft.");var accepted=new CatalogItem(new(fields,StringComparer.OrdinalIgnoreCase));ItemEditing.Validate(session,accepted);AcceptedDraft=accepted;return accepted;}
    internal void ResetDraft(){if(session is null)return;fields=new(session.Current.Fields,StringComparer.OrdinalIgnoreCase);RefreshFields();RefreshStats();Changed();}
    private void Changed(){if(session is null)return;try{ItemEditing.Validate(session,Draft);save.IsEnabled=!busy&&statErrors.Count==0&&(duplicateMode||setDraft is not null||ItemEditing.Changes(session,Draft).Count>0);updating=true;foreach(var row in raw)if(fields.TryGetValue(row.Key,out var stored))row.Value=stored;updating=false;feedback.Text=$"Preview level {previewLevel} · {ItemEditing.Changes(session,Draft).Count} changed fields · Armor/damage use item level and quality tables."+(session.ManagedDrops?" Managed drops: level changes recalculate world eligibility and retain explicit boss selections.":"");Render();}catch(Exception ex){save.IsEnabled=false;feedback.Text=ex.Message;Render();}}
    private void RefreshSubclassChoices(bool chooseValid)
    {
        bool wasUpdating=updating;updating=true;
        int id=Draft.Number("SubclassID"),itemClass=Draft.ClassId;
        var values=catalog.Tooltips.Subclasses.Where(p=>p.Key.Item1==itemClass).OrderBy(p=>p.Key.Item2).Select(p=>new FilterChoice(p.Key.Item2,string.IsNullOrWhiteSpace(p.Value)?$"Subclass {p.Key.Item2}":$"{p.Value} · {p.Key.Item2}")).ToList();
        if(values.All(v=>v.Id!=id)){
            if(chooseValid&&values.Count>0){id=values[0].Id;fields["SubclassID"]=id.ToString();}
            else values.Add(new(id,$"Unknown subclass {id}"));
        }
        combos["SubclassID"].ItemsSource=values;combos["SubclassID"].SelectedItem=values.First(v=>v.Id==id);updating=wasUpdating;
    }
    internal string SubclassLabel=>((FilterChoice?)combos["SubclassID"].SelectedItem)?.Label??"";
    private void Render()
    {
        if(session is null)return;
        beforeTitle.Text=ShowingOriginal?"ORIGINAL · INSTALLED DEFINITION":"CURRENT · BEFORE SAVE";
        var before=ShowingOriginal?session.Native??session.Current:session.Current;
        var beforeData=ShowingOriginal?catalog.OriginalTooltips:catalog.Tooltips;
        var lookup=ShowingOriginal?new Dictionary<int,CatalogItem>(catalog.Originals):catalog.Items.ToDictionary(i=>i.Id);
        lookup[before.Id]=before;
        var after=catalog.Items.ToDictionary(i=>i.Id);after[Draft.Id]=Draft;
        TooltipView.Fill(beforePreview,ItemTooltip.Build(before,beforeData,lookup,previewLevel));
        TooltipView.Fill(afterPreview,ItemTooltip.Build(Draft,catalog.Tooltips,after,previewLevel));
    }
    private void AddPreview(Grid grid,StackPanel panel,string title,int row){var dock=new DockPanel();Grid.SetColumn(dock,row);grid.Children.Add(dock);var label=new TextBlock{Text=title,Foreground=Brushes.LightSkyBlue,Margin=new Thickness(0,0,0,6)};if(row==0)beforeTitle=label;DockPanel.SetDock(label,Dock.Top);dock.Children.Add(label);dock.Children.Add(new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=new Border{Background=new SolidColorBrush(Color.FromRgb(16,17,27)),BorderBrush=Brushes.SlateGray,BorderThickness=new Thickness(1),Padding=new Thickness(12),Child=panel}});}
    private async Task Save(){if(session is null||busy)return;try{if(statErrors.Count>0)throw new InvalidOperationException("Correct the invalid stat amount before saving.");var draft=new CatalogItem(new(fields,StringComparer.OrdinalIgnoreCase));ItemEditing.Validate(session,draft);if(setDraft is not null){AcceptSetDraft();fields=new(session.Current.Fields,StringComparer.OrdinalIgnoreCase);DialogResult=true;return;}var changes=ItemEditing.Changes(session,draft);if(changes.Count==0&&!duplicateMode)return;string dropReview=session.ManagedDrops&&new[]{"ItemLevel","RequiredLevel"}.Any(k=>!OverrideEntry.Equivalent(session.Current.Text(k),draft.Text(k)))?$"\n\nManaged drops will be revalidated. World drops use eligible outdoor creatures at levels {WorldDropCatalog.Band(draft).Min}–{WorldDropCatalog.Band(draft).Max}; explicit boss selections and per-piece chance are retained.":"";var review=new ContentReviewWindow(duplicateMode?"new item":"item",duplicateMode?$"{draft.Name} · Copy of {session.Current.Id} · New ID allocated on save; source preserved":draft.Name,string.Join("\n",changes.Select(f=>$"{f.Field}: {f.Original}")),string.Join("\n",changes.Select(f=>$"{f.Field}: {f.Modified}"))+dropReview){Owner=this};if(review.ShowDialog()!=true)return;busy=true;tabs.IsEnabled=items.IsEnabled=search.IsEnabled=save.IsEnabled=false;if(duplicateMode){if(cloneSource is null)throw new InvalidOperationException("Reload the clone source.");var created=await ItemCloning.SaveAsync(cloneSource,draft);Saved=true;fields=new(session.Current.Fields,StringComparer.OrdinalIgnoreCase);MessageBox.Show(this,$"Created {draft.Name} · Item {created.Id}. The source item is unchanged. Reload the server before using the new item.","Item created");busy=false;DialogResult=true;return;}await ItemEditing.SaveAsync(session,draft);Saved=true;await SelectAsync(draft.Id);feedback.Text="Saved with revert/reapply history. Reload the server and verify in game.";}catch(Exception ex){feedback.Text=ex.Message;}finally{busy=false;tabs.IsEnabled=session is not null;items.IsEnabled=search.IsEnabled=true;save.IsEnabled=session is not null&&ItemEditing.Changes(session,Draft).Count>0;}}
}
