using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
namespace WoWItemEditor;

internal sealed class ItemModelView : Grid
{
    private readonly Viewport3D viewport=new(){ClipToBounds=true};
    private readonly ModelVisual3D visual=new();
    private readonly PerspectiveCamera camera=new(){FieldOfView=38};
    private readonly AxisAngleRotation3D rx=new(new Vector3D(1,0,0),0),ry=new(new Vector3D(0,1,0),0),rz=new(new Vector3D(0,0,1),0);
    private readonly Slider x=new(){Minimum=-180,Maximum=180},y=new(){Minimum=-180,Maximum=180},z=new(){Minimum=-180,Maximum=180};
    private readonly ComboBox part=new(){DisplayMemberPath="Label",MinWidth=125},skin=new(){DisplayMemberPath="Label",MinWidth=125};
    private readonly TextBlock title=DesignerWindow.Text("Select an item",16),info=DesignerWindow.Text("Open 3D model to load its textured appearance.",11),message=DesignerWindow.Text("Select an item to view its model.",13);
    private readonly TextBlock numbers=DesignerWindow.Text("",11);
    private readonly Button retry=new(){Content="Reload model"};
    private CatalogItem? item;
    private StaticItemMesh? customScene;
    private ItemModelAppearance? appearance;
    private int request;
    private bool binding,dragging;
    private readonly bool compact;
    private Point last;
    private double distance=4,zoom=1;
    private static readonly Dictionary<string,Task<StaticItemMesh>> cache=[];
    internal Task Loading {get;private set;}=Task.CompletedTask;
    internal int LoadedItem {get;private set;}
    internal int TriangleCount {get;private set;}
    internal int TextureCount {get;private set;}
    internal string Feedback=>info.Text;
    internal double ViewportHeight=>viewport.ActualHeight;
    internal (double X,double Y,double Z) Angles=>(rx.Angle,ry.Angle,rz.Angle);
    internal ItemModelView(bool compact=false,bool enlarged=false,bool portrait=false)
    {
        this.compact=compact;Background=new SolidColorBrush(Color.FromRgb(23,27,37));RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new());RowDefinitions.Add(new(){Height=GridLength.Auto});
        var top=new StackPanel{Margin=new Thickness(10)};Children.Add(top);title.TextWrapping=TextWrapping.Wrap;top.Children.Add(title);if(portrait)top.Visibility=Visibility.Collapsed;var choices=new WrapPanel{Margin=new Thickness(0,8,0,0)};top.Children.Add(choices);if(compact)choices.Visibility=Visibility.Collapsed;choices.Children.Add(part);skin.Margin=new Thickness(8,0,0,0);choices.Children.Add(skin);
        var stage=new Grid{ClipToBounds=true,MinHeight=80,Background=Brushes.Transparent,Cursor=Cursors.Hand};SetRow(stage,1);Children.Add(stage);stage.Children.Add(viewport);message.HorizontalAlignment=HorizontalAlignment.Center;message.VerticalAlignment=VerticalAlignment.Center;message.TextAlignment=TextAlignment.Center;message.Margin=new Thickness(20);stage.Children.Add(message);
        viewport.SizeChanged+=(_,_)=>Camera();viewport.Camera=camera;viewport.Children.Add(visual);var lights=new Model3DGroup();lights.Children.Add(new AmbientLight(Color.FromRgb(135,135,135)));lights.Children.Add(new DirectionalLight(Color.FromRgb(205,205,205),new Vector3D(-1,-1,-2)));lights.Children.Add(new DirectionalLight(Color.FromRgb(75,80,95),new Vector3D(1,0,1)));viewport.Children.Add(new ModelVisual3D{Content=lights});
        var transform=new Transform3DGroup();transform.Children.Add(new RotateTransform3D(rx));transform.Children.Add(new RotateTransform3D(ry));transform.Children.Add(new RotateTransform3D(rz));visual.Transform=transform;
        var controls=new StackPanel{Margin=new Thickness(portrait?5:10)};SetRow(controls,2);Children.Add(controls);if(!portrait)controls.Children.Add(DesignerWindow.Text("Drag: X/Y · Shift-drag: free rotation · Right-drag: Z · Wheel: zoom",11));
        var compactAxes=new Grid();if(compact){for(int n=0;n<3;n++)compactAxes.ColumnDefinitions.Add(new());controls.Children.Add(compactAxes);}int axisIndex=0;foreach(var (label,slider,rotation) in new[]{("X",x,rx),("Y",y,ry),("Z",z,rz)}){var row=new DockPanel{Margin=new Thickness(0,5,0,0)};var text=DesignerWindow.Text(label,12);text.Width=24;DockPanel.SetDock(text,Dock.Left);row.Children.Add(text);row.Children.Add(slider);if(compact){SetColumn(row,axisIndex++);compactAxes.Children.Add(row);}else controls.Children.Add(row);slider.ValueChanged+=(_,_)=>{rotation.Angle=slider.Value;numbers.Text=$"X {x.Value:0}° · Y {y.Value:0}° · Z {z.Value:0}°";};}controls.Children.Add(numbers);if(compact)numbers.Visibility=Visibility.Collapsed;
        var actions=new WrapPanel{Margin=portrait?new Thickness(0,4,0,0):new Thickness(0,8,0,8)};controls.Children.Add(actions);DesignerWindow.Button("Reset view",actions).Click+=(_,_)=>Reset();actions.Children.Add(retry);retry.Click+=(_,_)=>{if(item is not null)Bind(item,true);};DesignerWindow.Button("Client folder…",actions).Click+=(_,_)=>ChooseClient();var larger=DesignerWindow.Button("Larger view…",actions);larger.Visibility=enlarged?Visibility.Collapsed:Visibility.Visible;larger.Click+=(_,_)=>{if(item is null&&customScene is null)return;var view=new ItemModelView(enlarged:true);var window=new Window{Title="3D preview · "+(item?.Name??title.Text),Width=1000,Height=900,MinWidth=700,MinHeight=650,Content=view,Owner=Window.GetWindow(this),WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Background};window.Show();if(customScene is not null)view.ShowScene(customScene,title.Text);else view.Bind(item);};controls.Children.Add(info);if(compact){info.Visibility=Visibility.Collapsed;var buttons=actions.Children.OfType<Button>().ToArray();for(int n=0;n<buttons.Length;n++){buttons[n].Padding=new Thickness(6,4,6,4);buttons[n].Margin=new Thickness(0,0,6,0);}buttons[1].Visibility=Visibility.Collapsed;buttons[2].Visibility=Visibility.Collapsed;var options=new Button{Content="Options…",Padding=new Thickness(6,4,6,4)};var menu=new ContextMenu();var reload=new MenuItem{Header="Reload model"};reload.Click+=(_,_)=>{if(item is not null)Bind(item,true);};menu.Items.Add(reload);var client=new MenuItem{Header="Choose client folder…"};client.Click+=(_,_)=>ChooseClient();menu.Items.Add(client);options.Click+=(_,_)=>{menu.PlacementTarget=options;menu.IsOpen=true;};actions.Children.Add(options);}
        part.SelectionChanged+=(_,_)=>{if(!binding)Loading=Render();};skin.SelectionChanged+=(_,_)=>{if(!binding)Loading=Render();};
        void UpdateCursor()=>stage.Cursor=dragging&&(Keyboard.Modifiers&ModifierKeys.Shift)!=0?GrabCursor:Cursors.Hand;
        ProcessInputEventHandler cursorInput=(_,_)=>UpdateCursor();
        stage.Loaded+=(_,_)=>InputManager.Current.PostProcessInput+=cursorInput;
        stage.Unloaded+=(_,_)=>InputManager.Current.PostProcessInput-=cursorInput;
        stage.MouseDown+=(_,e)=>{if(e.ChangedButton is not (MouseButton.Left or MouseButton.Right))return;dragging=true;last=e.GetPosition(stage);stage.CaptureMouse();UpdateCursor();e.Handled=true;};
        stage.MouseMove+=(_,e)=>{UpdateCursor();if(!dragging)return;var next=e.GetPosition(stage);Drag(last,next,stage.RenderSize,(Keyboard.Modifiers&ModifierKeys.Shift)!=0,e.RightButton==MouseButtonState.Pressed);last=next;e.Handled=true;};
        stage.MouseUp+=(_,_)=>{dragging=false;stage.ReleaseMouseCapture();UpdateCursor();};stage.LostMouseCapture+=(_,_)=>{dragging=false;UpdateCursor();};stage.MouseWheel+=(_,e)=>{zoom=Math.Clamp(zoom*Math.Pow(.88,e.Delta/120d),.25,5);Camera();e.Handled=true;};
        Reset();
    }
    // Screen-space virtual trackball; keep the sliders synchronized with its orientation.
    internal void Drag(Point from,Point to,Size size,bool free,bool roll=false)
    {
        if(free){
            Vector3D Project(Point p){double radius=Math.Max(1,Math.Min(size.Width,size.Height)/2);double a=(p.X-size.Width/2)/radius,b=(size.Height/2-p.Y)/radius;double r=a*a+b*b;var v=new Vector3D(a,b,r<=.5?Math.Sqrt(1-r):.5/Math.Sqrt(r));v.Normalize();return v;}
            var start=Project(from);var end=Project(to);var axis=Vector3D.CrossProduct(start,end);if(axis.LengthSquared<1e-12)return;axis.Normalize();double angle=Vector3D.AngleBetween(start,end);
            var matrix=new Transform3DGroup{Children={new RotateTransform3D(rx),new RotateTransform3D(ry),new RotateTransform3D(rz)}}.Value;matrix.Rotate(new Quaternion(axis,angle));
            double pitch=Math.Asin(Math.Clamp(-matrix.M13,-1,1));double a,c;if(Math.Abs(Math.Cos(pitch))>1e-6){a=Math.Atan2(matrix.M23,matrix.M33);c=Math.Atan2(matrix.M12,matrix.M11);}else{a=Math.Atan2(-matrix.M32,matrix.M22);c=0;}Rotate(a*180/Math.PI,pitch*180/Math.PI,c*180/Math.PI);
        }else if(roll)z.Value=Wrap(z.Value+(to.X-from.X)*.7);else{x.Value=Wrap(x.Value+(to.Y-from.Y)*.7);y.Value=Wrap(y.Value+(to.X-from.X)*.7);}
    }
    private static readonly Cursor GrabCursor=CreateGrabCursor();
    private static Cursor CreateGrabCursor()
    {
        string[] pixels=["........................","........................","........................",".......##.##.##.........","......#WW#WW#WW##.......","......#WW#WW#WW#W#......","......#WWWWWWWWWW#......","......#WWWWWWWWWW#......","......#WWWWWWWWWW#......","...###WWWWWWWWWWW#......","..#WWW#WWWWWWWWWW#......","..#WWWWWWWWWWWWWW#......","...#WWWWWWWWWWWWW#......","....#WWWWWWWWWWWW#......",".....#WWWWWWWWWWW#......","......#WWWWWWWWW#.......",".......#WWWWWWWW#.......",".......#WWWWWWWW#.......",".......##########.......","........................","........................","........................","........................","........................"];
        using var stream=new MemoryStream();using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true)){writer.Write((ushort)0);writer.Write((ushort)2);writer.Write((ushort)1);writer.Write((byte)24);writer.Write((byte)24);writer.Write((ushort)0);writer.Write((ushort)12);writer.Write((ushort)10);writer.Write(40+24*24*4+24*4);writer.Write(22);writer.Write(40);writer.Write(24);writer.Write(48);writer.Write((ushort)1);writer.Write((ushort)32);for(int i=0;i<6;i++)writer.Write(0);for(int row=23;row>=0;row--)for(int col=0;col<24;col++){char pixel=pixels[row][col];byte value=pixel=='W'?(byte)245:(byte)30;writer.Write(value);writer.Write(value);writer.Write(value);writer.Write(pixel=='.'?(byte)0:(byte)255);}for(int row=23;row>=0;row--){for(int group=0;group<3;group++){byte mask=0;for(int bit=0;bit<8;bit++)if(pixels[row][group*8+bit]=='.')mask|=(byte)(128>>bit);writer.Write(mask);}writer.Write((byte)0);}}stream.Position=0;return new Cursor(stream);
    }
    private static double Wrap(double angle)=>((angle+540)%360)-180;
    private void Camera(){double fitted=distance*zoom*Math.Max(1,viewport.ActualWidth/Math.Max(1,viewport.ActualHeight));camera.Position=new Point3D(0,0,fitted);camera.LookDirection=new Vector3D(0,0,-fitted);camera.UpDirection=new Vector3D(0,1,0);camera.NearPlaneDistance=Math.Max(.001,distance/1000);camera.FarPlaneDistance=distance*100;}
    internal void Rotate(double a,double b,double c){x.Value=a;y.Value=b;z.Value=c;}
    private void Reset(){Rotate(-12,customScene is null?90:-90,0);zoom=1;Camera();}
    internal void Bind(CatalogItem? selected,bool force=false)
    {
        if(!force&&selected?.Id==item?.Id&&LoadedItem==selected?.Id)return;part.Visibility=skin.Visibility=Visibility.Visible;customScene=null;item=selected;Loading=Load(selected,force);
    }
    internal void ShowScene(StaticItemMesh scene,string name)
    {
        bool first=customScene is null;request++;item=null;appearance=null;customScene=scene;if(first)Reset();title.Text=name;title.Foreground=Brushes.LightSkyBlue;part.Visibility=skin.Visibility=Visibility.Collapsed;visual.Content=null;message.Visibility=Visibility.Collapsed;
        var bounds=scene.Model.Bounds;var wrapper=new Model3DGroup();wrapper.Children.Add(scene.Model);wrapper.Transform=new TranslateTransform3D(-bounds.X-bounds.SizeX/2,-bounds.Y-bounds.SizeY/2,-bounds.Z-bounds.SizeZ/2);visual.Content=wrapper;distance=Math.Max(.1,Math.Sqrt(bounds.SizeX*bounds.SizeX+bounds.SizeY*bounds.SizeY+bounds.SizeZ*bounds.SizeZ)/2)*3.5;Camera();TriangleCount=scene.Triangles;TextureCount=scene.Textures;info.Text=scene.Note;LoadedItem=-1;
    }
    internal void ShowLoading(string text){request++;visual.Content=null;message.Text=text;message.Visibility=Visibility.Visible;}
    private async Task Load(CatalogItem? selected,bool force)
    {
        int token=++request;LoadedItem=TriangleCount=TextureCount=0;visual.Content=null;appearance=null;message.Visibility=Visibility.Visible;title.Text=selected?.Name??"Select an item";title.Foreground=selected?.NameBrush??Brushes.LightSkyBlue;if(selected is null){message.Text="Select an item to view its model.";return;}message.Text="Loading textured item…";info.Text="Reading client assets; missing files may be downloaded into the viewer cache…";retry.IsEnabled=false;
        try{var resolved=await ItemModelAssets.Shared.Resolve(selected.Id,null,force);if(token!=request)return;appearance=resolved;binding=true;part.ItemsSource=(selected.Slot==3&&resolved.Parts.Length==2?new[]{new FilterChoice(-1,"Both shoulders")}:Array.Empty<FilterChoice>()).Concat(resolved.Parts.Select((p,n)=>new FilterChoice(n,$"Part / variant {n+1}"))).ToArray();part.SelectedIndex=0;skin.ItemsSource=new[]{new FilterChoice(0,"Appearance skin")}.Concat(resolved.Textures.Skip(1).Select((id,n)=>new FilterChoice(id,$"Skin variant {n+2}"))).ToArray();skin.SelectedIndex=0;binding=false;Reset();if(force)foreach(string key in cache.Keys.Where(k=>resolved.Models.Any(id=>k.Contains("/"+id+"/"))).ToArray())cache.Remove(key);await Render(token);}
        catch(Exception ex){if(token==request){message.Text="3D preview unavailable"+(compact?"\n"+ex.Message:"");info.Text=ex.Message+" The item data is unchanged.";}}finally{if(token==request)retry.IsEnabled=true;}
    }
    private Task Render()=>Render(++request);
    private async Task Render(int token)
    {
        if(appearance is null||item is null)return;visual.Content=null;message.Visibility=Visibility.Visible;message.Text="Loading textured item…";
        try{
            int chosen=(part.SelectedItem as FilterChoice)?.Id??0,texture=(skin.SelectedItem as FilterChoice)?.Id??0;var parts=chosen<0?appearance.Parts:new[]{appearance.Parts[Math.Clamp(chosen,0,appearance.Parts.Length-1)]};var meshes=new List<StaticItemMesh>();
            foreach(var component in parts){string identity=ItemModelAssets.Shared.BuildIdentity;string key=identity+"/"+component.Model+"/"+(texture==0?component.Textures.FirstOrDefault():texture);if(!cache.TryGetValue(key,out var task)){task=Task.Run(()=>StaticItemModel.Load(ItemModelAssets.Shared,component,texture>0?texture:null,identity));cache[key]=task;if(cache.Count>16)cache.Remove(cache.Keys.First());}try{meshes.Add(await task);}catch{cache.Remove(key);throw;}if(token!=request)return;}
            var group=new Model3DGroup();double offset=0;foreach(var mesh in meshes){var bounds=mesh.Model.Bounds;var wrapper=new Model3DGroup();wrapper.Children.Add(mesh.Model);wrapper.Transform=new TranslateTransform3D(-bounds.X-bounds.SizeX/2,-bounds.Y-bounds.SizeY/2,-bounds.Z-bounds.SizeZ/2+offset);group.Children.Add(wrapper);offset+=Math.Max(bounds.SizeX,Math.Max(bounds.SizeY,bounds.SizeZ))*1.2;}
            if(meshes.Count>1)group.Transform=new TranslateTransform3D(0,0,-group.Bounds.Z-group.Bounds.SizeZ/2);var box=group.Bounds;double radius=Math.Sqrt(box.SizeX*box.SizeX+box.SizeY*box.SizeY+box.SizeZ*box.SizeZ)/2;if(!double.IsFinite(radius)||radius<=0)throw new InvalidDataException("Model bounds are invalid.");distance=radius*3.5;Camera();visual.Content=group;message.Visibility=Visibility.Collapsed;LoadedItem=item.Id;TriangleCount=meshes.Sum(m=>m.Triangles);TextureCount=meshes.Sum(m=>m.Textures);info.Text=$"{appearance.Detail}\nTextured static preview · {TriangleCount:N0} triangles. "+string.Join(" ",meshes.Select(m=>m.Note).Where(n=>n.Length>0).Distinct());
        }catch(Exception ex){if(token==request){LoadedItem=0;message.Text="3D preview unavailable"+(compact?"\n"+ex.Message:"");info.Text=ex.Message+" Missing textures are not replaced with a plain model.";}}
    }
    private void ChooseClient()
    {
        var settings=new SettingsWindow{Owner=Window.GetWindow(this)};if(settings.ShowDialog()==true)info.Text="Settings saved. Restart WoWItemEditor to use the selected client folder.";
    }
}
