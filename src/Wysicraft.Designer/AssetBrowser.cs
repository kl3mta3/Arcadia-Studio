using System.IO;
using AvalonDock.Layout;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Wysicraft.Core;
using Validation = Wysicraft.Core.Validation;
using Wysicraft.Models;
using Wysicraft.Packaging;
namespace Wysicraft.Designer;

/// <summary>One thing on a browser toolbar: a button, or a button that drops a short menu.</summary>
sealed record BarItem(string Label,System.Action? Run,(string Label,System.Action Run)[]? Menu);

public partial class MainWindow
{
    sealed class BrowserEntry {
        public string Id {get;init;}="";
        public string Name {get;init;}="";
        public string Detail {get;init;}="";
        public Func<ImageSource?>? Load {get;init;}
        ImageSource? image;bool loaded;
        public ImageSource? Image {get {if(!loaded){loaded=true;try{image=Load?.Invoke();}catch{}}return image;}}
    }
    readonly MinecraftAssets minecraftAssets=new();
    readonly Dictionary<string,ImageSource?> itemImages=[];
    readonly ListBox assetList=new(),itemList=new();
    readonly TextBox assetSearch=new(),itemSearch=new();
    readonly TextBlock assetInfo=new(){TextWrapping=TextWrapping.Wrap},itemInfo=new(){TextWrapping=TextWrapping.Wrap};
    List<(string Id,string Name)> registeredItems=[];
    bool browsersReady;
    void InitializeAssetBrowsers() {
        Setup(AssetsHost,assetList,assetSearch,assetInfo,"Search project images",[
            new("+ Add",null,[("Import PNGs…",ImportBrowserImages),("Import sounds…",ImportSounds),("Add image to screen",()=>CreateBrowserElement(false))]),
            new("+ Create",null,[("New pixel art…",()=>PixelEditorForAsset(true)),("Music maker…",()=>OpenMusicMaker()),("Sound effect maker…",()=>OpenSoundEffectMaker()),("Particle maker…",()=>OpenParticleMaker())]),
            new("Assign selected",()=>AssignBrowserAsset(),null),
            new("Replace",ReplaceBrowserAsset,null),
            new("Find uses",FindBrowserUses,null),
            new("Delete",DeleteBrowserAsset,null)]);
        Setup(ItemsHost,itemList,itemSearch,itemInfo,"Search item name or namespace:id",[
            new("+ Add",null,[("Load Minecraft / mod JAR…",LoadItemJar),("Read test items",LoadTestItems),("Add item to screen",()=>CreateBrowserElement(true))]),
            new("Assign selected",AssignBrowserItem,null)]);
        itemInfo.Text="Load local Minecraft assets for texture previews. Read test items after launching Minecraft to include registered mod items. 3D models render fully in-game.";
        assetSearch.TextChanged+=(_,_)=>RefreshAssetBrowser();itemSearch.TextChanged+=(_,_)=>RefreshItemBrowser();
        assetList.SelectionChanged+=(_,_)=>{if(assetList.SelectedItem is BrowserEntry entry)assetInfo.Text=entry.Id+"\n"+AssetUses(entry.Id).Count+" known reference(s).";};
        assetList.MouseDoubleClick+=(_,_)=>{renameTimer?.Stop();Guard(()=>AssignBrowserAsset());};itemList.MouseDoubleClick+=(_,_)=>Guard(AssignBrowserItem);
        // Rename the way Explorer does: F2, or click the name of the asset that is already selected and wait a moment
        // (a double-click instead still assigns it).
        assetList.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.F2&&assetList.SelectedItem is BrowserEntry entry){e.Handled=true;BeginAssetRename(entry);}};
        assetList.PreviewMouseLeftButtonDown+=(_,e)=>{renameTimer?.Stop();renameCandidate=null;
            if(e.ClickCount!=1||e.OriginalSource is not TextBlock label)return;
            for(var d=(DependencyObject)label;d!=null;d=VisualTreeHelper.GetParent(d))if(d is ListBoxItem item){if(item.IsSelected&&item.DataContext is BrowserEntry entry&&label.Text==entry.Name)renameCandidate=entry;break;}};
        assetList.PreviewMouseLeftButtonUp+=(_,_)=>{var entry=renameCandidate;renameCandidate=null;if(entry==null||assetList.SelectedItem!=entry)return;
            renameTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(DoubleClickTime())};
            renameTimer.Tick+=(_,_)=>{renameTimer?.Stop();if(assetList.SelectedItem==entry)BeginAssetRename(entry);};renameTimer.Start();};
        Drag(assetList,"wysicraft.asset");Drag(itemList,"wysicraft.item");
        // Right-click an asset: it's selected first (a ListBox doesn't do that itself), then the menu fits its kind.
        assetList.PreviewMouseRightButtonDown+=(_,e)=>{for(var d=e.OriginalSource as DependencyObject;d!=null;d=VisualTreeHelper.GetParent(d))if(d is ListBoxItem item){item.IsSelected=true;break;}};
        var assetMenu=new ContextMenu();assetList.ContextMenu=assetMenu;
        assetMenu.Opened+=(_,_)=>{
            assetMenu.Items.Clear();
            if(assetList.SelectedItem is not BrowserEntry entry){assetMenu.Items.Add(new MenuItem{Header="Select an asset first",IsEnabled=false});return;}
            MenuItem Item(string header,Action run){var i=new MenuItem{Header=header};i.Click+=(_,_)=>Guard(run);assetMenu.Items.Add(i);return i;}
            if(entry.Id.EndsWith(".png")){
                Item("Edit pixels…",()=>PixelEditorForAsset(false)).ToolTip="Edit this picture. Animated pictures and sprite sheets open as frames.";
                Item("Add to screen",()=>CreateBrowserElement(false)).ToolTip="Adds it as an Image (or a Sprite for a sprite sheet) on the current screen";
                Item("Assign to selected control",AssignBrowserAsset);
            }
            else if(SoundAssets.IsSound(entry.Id))AddSoundMenuItems(assetMenu,entry.Id);
            else Item("Assign to selected control",AssignBrowserAsset);
            assetMenu.Items.Add(new Separator());
            if(entry.Id.EndsWith(".png",StringComparison.OrdinalIgnoreCase)||SoundAssets.IsSound(entry.Id)){
                Item("Rename…",()=>BeginAssetRename(entry)).ToolTip="Change its name (the extension stays). Controls, actions and particle effects that use it follow; scripts that mention it are listed in Output. Also F2, or click the name again.";
                Item("Replace…",ReplaceBrowserAsset).ToolTip=SoundAssets.IsSound(entry.Id)?"Swap in another sound file. Its name stays, so everything that plays it plays the new one.":"Swap in another PNG. Its name stays, so everything that shows it shows the new one.";
            }
            Item("Show in Explorer",()=>ShowAssetInExplorer(entry.Id)).ToolTip="Projects keep their files inside the .arcadia file, so this saves a copy in an Arcadia Studio folder and shows it. To change the project's file, edit the copy and use Replace.";
            Item("Save a copy as…",()=>SaveAssetCopy(entry.Id));
            assetMenu.Items.Add(new Separator());
            Item("Delete",DeleteBrowserAsset).ToolTip="Removes it from the project. Anything still using it will report a missing asset on Validate.";
        };
        AssetsHost.AllowDrop=true;AssetsHost.Background??=Brushes.Transparent;
        AssetsHost.DragOver+=(_,e)=>{if(DroppedFiles(e).Length>0){e.Effects=DroppedFiles(e).Any(f=>IsPng(f)||IsSoundFile(f)||f.EndsWith(".mcmeta"))?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;}};
        AssetsHost.Drop+=(_,e)=>Guard(()=>DropFilesOnAssets(e));
        // PNG files dropped on the canvas are imported and placed as Image controls where they land.
        Surface.DragOver+=(_,e)=>{if(DroppedFiles(e).Length>0){e.Effects=DroppedFiles(e).Any(IsPng)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;}};
        Surface.Drop+=(_,args)=>Guard(()=>{var pngs=DroppedFiles(args).Where(IsPng).ToArray();if(pngs.Length==0)return;args.Handled=true;var at=args.GetPosition(Surface);var added=ImportImageFiles(pngs);var placed=new List<string>();
            for(int i=0;i<added.Count;i++){CreateBrowserElement(false,at.X/Zoom+i*8,at.Y/Zoom+i*8,added[i]);placed.AddRange(selected);}selected.Clear();selected.UnionWith(placed);Draw();RefreshInspector();});
        Surface.Drop+=(_,args)=>Guard(()=>{var point=args.GetPosition(Surface);if(args.Data.GetData("wysicraft.asset") is string path){if(SoundAssets.IsSound(path)){AddControl("sound",0,0);var sound=ui.Elements.First(e=>selected.Contains(e.Id));sound.Sound=SoundAssets.Resource(path);RefreshInspector();RefreshLayers();}else CreateBrowserElement(false,point.X/Zoom,point.Y/Zoom,path);args.Handled=true;}else if(args.Data.GetData("wysicraft.item") is string id){CreateBrowserElement(true,point.X/Zoom,point.Y/Zoom,id);args.Handled=true;}});
        browsersReady=true;
        Loaded+=(_,_)=>{if(DockSmoke)return;var home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);var jar=Path.Combine(home,".gradle","caches","neoformruntime","artifacts","minecraft_1.21.1_client.jar");if(!File.Exists(jar))jar=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),".minecraft","versions","1.21.1","1.21.1.jar");if(File.Exists(jar))try{minecraftAssets.LoadJar(jar);itemImages.Clear();minecraftTextures.Clear();minecraftMetas.Clear();RefreshItemBrowser();Draw();}catch(Exception ex){itemInfo.Text=ex.Message;}};
        void Setup(DockPanel host,ListBox list,TextBox search,TextBlock info,string hint,BarItem[] actions) {
            var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);host.Children.Add(top);top.Children.Add(new TextBlock {Text=hint,Margin=new Thickness(4)});top.Children.Add(search);
            var buttons=new WrapPanel();top.Children.Add(buttons);
            foreach(var entry in actions){
                var button=new Button{Content=entry.Menu==null?entry.Label:entry.Label+" ▾"};
                if(entry.Menu==null){var run=entry.Run!;button.Click+=(_,_)=>Guard(run);}
                else{
                    // A short menu hung under the button, so the bar stays five controls wide however much it can do.
                    var menu=new ContextMenu{PlacementTarget=button,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom};
                    foreach(var (text,run) in entry.Menu){var mi=new MenuItem{Header=text};mi.Click+=(_,_)=>Guard(run);menu.Items.Add(mi);}
                    button.Click+=(_,_)=>{menu.PlacementTarget=button;menu.IsOpen=true;};
                }
                buttons.Children.Add(button);
            }
            info.Margin=new Thickness(4);DockPanel.SetDock(info,Dock.Bottom);host.Children.Add(info);
            list.ItemTemplate=BrowserTemplate();ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);VirtualizingPanel.SetIsVirtualizing(list,true);VirtualizingPanel.SetVirtualizationMode(list,VirtualizationMode.Recycling);host.Children.Add(list);
        }
        void Drag(ListBox list,string format) {
            Point start=default;list.PreviewMouseLeftButtonDown+=(_,e)=>start=e.GetPosition(list);
            list.PreviewMouseMove+=(_,e)=>{var p=e.GetPosition(list);if(e.LeftButton==MouseButtonState.Pressed && (Math.Abs(p.X-start.X)>SystemParameters.MinimumHorizontalDragDistance || Math.Abs(p.Y-start.Y)>SystemParameters.MinimumVerticalDragDistance) && list.SelectedItem is BrowserEntry entry)DragDrop.DoDragDrop(list,new DataObject(format,entry.Id),DragDropEffects.Copy);};
        }
    }
    static DataTemplate BrowserTemplate() {
        var root=new FrameworkElementFactory(typeof(StackPanel));root.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);root.SetValue(FrameworkElement.MarginProperty,new Thickness(3));
        var image=new FrameworkElementFactory(typeof(Image));image.SetValue(FrameworkElement.WidthProperty,40d);image.SetValue(FrameworkElement.HeightProperty,40d);image.SetValue(RenderOptions.BitmapScalingModeProperty,BitmapScalingMode.NearestNeighbor);image.SetBinding(Image.SourceProperty,new Binding("Image"));root.AppendChild(image);
        var labels=new FrameworkElementFactory(typeof(StackPanel));labels.SetValue(FrameworkElement.MarginProperty,new Thickness(8,0,0,0));root.AppendChild(labels);
        foreach(var field in new[]{"Name","Detail"}) {var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new Binding(field));if(field=="Detail")text.SetValue(TextBlock.FontSizeProperty,10d);labels.AppendChild(text);}
        return new DataTemplate {VisualTree=root};
    }
    string AssetResource(string path) {
        if(path.StartsWith("assets/textures/"))return TextureAssets.Resource(project.Manifest.Id,path[16..]);
        var parts=path.Split('/',3);return parts[1]+":"+parts[2];
    }
    void RefreshAssetBrowser() {
        if(!browsersReady)return;var id=(assetList.SelectedItem as BrowserEntry)?.Id;var query=assetSearch.Text.Trim();
        assetList.ItemsSource=project.Assets.Where(p=>p.Key.EndsWith(".png",StringComparison.OrdinalIgnoreCase)&&p.Key.Contains(query,StringComparison.OrdinalIgnoreCase)).OrderBy(p=>p.Key).Select(p=>new BrowserEntry {Id=p.Key,Name=Path.GetFileName(p.Key),Detail=AssetResource(p.Key)+(project.Assets.ContainsKey(p.Key+".mcmeta")?"  • animated":SheetFrames(p.Key) is var (fw,fh,n)?$"  • sprite sheet, {n} frames of {fw}×{fh}":""),Load=()=>AssetThumbnail(p.Key,p.Value)})
            // Sounds are listed too, with the ID actions and Sound controls use.
            .Concat(SoundAssets.All(project).Where(k=>k.Contains(query,StringComparison.OrdinalIgnoreCase)).Select(k=>new BrowserEntry {Id=k,Name="♪ "+Path.GetFileName(k),Detail=SoundAssets.Resource(k)+(project.Assets.ContainsKey(k+SoundAssets.SongSuffix)?"  • song (Music maker)":project.Assets.ContainsKey(k+SoundAssets.EffectSuffix)?"  • sound effect":"")+(k.EndsWith(".ogg")?"":"  • not for Minecraft")}))
            .ToArray();
        assetList.SelectedItem=assetList.Items.Cast<BrowserEntry>().FirstOrDefault(e=>e.Id==id);
    }
    void RefreshItemBrowser() {
        var query=itemSearch.Text.Trim();var items=registeredItems.Count>0?registeredItems:minecraftAssets.Items.ToList();
        itemList.ItemsSource=items.Where(p=>p.Id.Contains(query,StringComparison.OrdinalIgnoreCase)||p.Name.Contains(query,StringComparison.OrdinalIgnoreCase)).Select(p=>new BrowserEntry {Id=p.Id,Name=p.Name,Detail=p.Id,Load=()=>ItemImage(p.Id)}).ToArray();
        itemInfo.Text=$"{itemList.Items.Count} items • "+(registeredItems.Count>0?"Registered test items":"Asset-derived IDs; use Read test items for the exact registry")+". Texture previews; full models render in Minecraft.";
    }
    ImageSource? ItemImage(string id) {
        if(itemImages.TryGetValue(id,out var cached))return cached;
        try {if(minecraftAssets.CubeFaces(id) is {} faces)return itemImages[id]=IsoCube(faces.Top,faces.Left,faces.Right);
            var bytes=minecraftAssets.Texture(id);ImageSource? image=bytes==null?null:DecodeTexture(bytes);if(image is BitmapSource b && b.PixelHeight>b.PixelWidth)image=new CroppedBitmap(b,new Int32Rect(0,0,b.PixelWidth,b.PixelWidth));return itemImages[id]=image;}catch{return itemImages[id]=null;}
    }
    // Project images first, then Minecraft's own textures (e.g. minecraft:textures/block/bricks.png) from the loaded game JAR.
    readonly Dictionary<string,byte[]?> minecraftTextures=[];
    bool TryTexture(string resource,out byte[] bytes) {
        if(TextureAssets.TryGet(project,resource,out bytes))return true;
        // Misses are remembered too: without that, every redraw searched the loaded game JAR again for the same name.
        if(!minecraftTextures.TryGetValue(resource,out var found)){try{found=minecraftAssets.TextureFile(resource);}catch{found=null;}minecraftTextures[resource]=found;}
        bytes=found??[];return found!=null;
    }
    // Draws a block the way Minecraft's inventory shows it: a small isometric cube (top, front-left and right
    // faces, the sides shaded) inside a 16×16 icon, so the canvas and Preview match the game.
    ImageSource IsoCube(byte[] top,byte[] left,byte[] right) {
        // Faces are enlarged 8× pixel-for-pixel first, so the skewed drawing keeps crisp Minecraft pixels.
        BitmapSource Face(byte[] bytes) {
            BitmapSource b=(BitmapSource)DecodeTexture(bytes)!;if(b.PixelHeight>b.PixelWidth)b=new CroppedBitmap(b,new Int32Rect(0,0,b.PixelWidth,b.PixelWidth));
            b=new FormatConvertedBitmap(b,PixelFormats.Bgra32,null,0);int w=b.PixelWidth,h=b.PixelHeight,f=Math.Max(1,128/Math.Max(1,w));
            var src=new byte[w*h*4];b.CopyPixels(src,w*4,0);var dst=new byte[w*f*h*f*4];
            for(int y=0;y<h*f;y++)for(int x=0;x<w*f;x++)Buffer.BlockCopy(src,((y/f)*w+x/f)*4,dst,(y*w*f+x)*4,4);
            return BitmapSource.Create(w*f,h*f,96,96,PixelFormats.Bgra32,null,dst,w*f*4);
        }
        const int size=128;double k=size/16d;
        // Hexagon of a 10-pixel cube at Minecraft's GUI angle (30° tilt, 45° turn), centered in 16×16.
        Point P(double x,double y)=>new(x*k,y*k);
        Point ul=P(.93,3.67),t=P(8,.13),c=P(8,7.2),ll=P(.93,12.33),b=P(8,15.87),ur=P(15.07,3.67),lr=P(15.07,12.33);
        var visual=new DrawingVisual();RenderOptions.SetBitmapScalingMode(visual,BitmapScalingMode.NearestNeighbor);
        using(var dc=visual.RenderOpen()) {
            void Draw(BitmapSource face,Point origin,Point u,Point v,double shade) {
                dc.PushTransform(new MatrixTransform(u.X-origin.X,u.Y-origin.Y,v.X-origin.X,v.Y-origin.Y,origin.X,origin.Y));
                dc.DrawImage(face,new Rect(0,0,1,1));
                if(shade>0)dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(shade*255),0,0,0)),null,new Rect(0,0,1,1));
                dc.Pop();
            }
            Draw(Face(top),ul,t,c,0);Draw(Face(left),ul,c,ll,.2);Draw(Face(right),c,ur,b,.4);
        }
        var bitmap=new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();return bitmap;
    }
    void ImportBrowserImages() {
        var dialog=new OpenFileDialog {Filter="PNG images|*.png",Multiselect=true};if(dialog.ShowDialog()!=true)return;
        ImportImageFiles(dialog.FileNames);
    }
    // Imports PNG files into the project (Import PNGs, or files dropped on Assets or the canvas) and returns their asset paths.
    List<string> ImportImageFiles(IEnumerable<string> paths) {
        var files=paths.Select(p=>(Path:p,Bytes:File.ReadAllBytes(p))).ToArray();
        foreach(var file in files){try{ProjectStore.TextureSize(file.Bytes);DecodeTexture(file.Bytes);}catch(Exception ex){throw new InvalidOperationException(Path.GetFileName(file.Path)+" is not a PNG image Arcadia Studio can use: "+ex.Message);}}
        var added=new List<string>();if(files.Length==0)return added;
        Change();foreach(var file in files){string stem=System.Text.RegularExpressions.Regex.Replace(Path.GetFileNameWithoutExtension(file.Path).ToLowerInvariant(),"[^a-z0-9_-]","_");string path=TextureAssets.Path(project.Manifest.Id,stem+".png");int n=1;while(project.Assets.TryGetValue(path,out var existing)&&!existing.SequenceEqual(file.Bytes))path=TextureAssets.Path(project.Manifest.Id,stem+"_"+(n++)+".png");project.Assets[path]=file.Bytes;added.Add(path);
            // An animation file saved next to the PNG (name.png.mcmeta, as in resource packs) comes along automatically.
            if(File.Exists(file.Path+".mcmeta")){var meta=File.ReadAllBytes(file.Path+".mcmeta");if(meta.Length<=65536)project.Assets[path+".mcmeta"]=meta;}}
        RefreshAssetBrowser();assetList.SelectedItem=assetList.Items.Cast<BrowserEntry>().FirstOrDefault(e=>e.Id==added[^1]);
        Log(added.Count==1?"Imported "+Path.GetFileName(added[0])+" into Assets.":$"Imported {added.Count} images into Assets.");
        return added;
    }
    static string[] DroppedFiles(DragEventArgs e)=>e.Data.GetDataPresent(DataFormats.FileDrop)&&e.Data.GetData(DataFormats.FileDrop) is string[] files?files:[];
    static bool IsPng(string path)=>path.EndsWith(".png",StringComparison.OrdinalIgnoreCase)&&File.Exists(path);
    void DropFilesOnAssets(DragEventArgs e) {
        var files=DroppedFiles(e);if(files.Length==0)return;e.Handled=true;
        var pngs=files.Where(IsPng).ToArray();var audio=files.Where(IsSoundFile).ToArray();var skipped=files.Count(f=>!IsPng(f) && !IsSoundFile(f) && !f.EndsWith(".png.mcmeta",StringComparison.OrdinalIgnoreCase));
        if(pngs.Length>0)ImportImageFiles(pngs);
        if(audio.Length>0)ImportSoundFiles(audio);
        // An animation file dropped on its own joins the project image with the same name.
        foreach(var meta in files.Where(f=>f.EndsWith(".png.mcmeta",StringComparison.OrdinalIgnoreCase)&&!files.Contains(f[..^7])&&File.Exists(f))) {
            string png=Path.GetFileName(meta[..^7]).ToLowerInvariant();var target=project.Assets.Keys.FirstOrDefault(k=>k.EndsWith("/"+png));
            if(target==null){Log("No project image named "+png+" for "+Path.GetFileName(meta)+". Import the PNG first.");continue;}
            Change();project.Assets[target+".mcmeta"]=File.ReadAllBytes(meta);Log("Animated "+png+".");RefreshAssetBrowser();Draw();
        }
        if(skipped>0)Log(skipped==1?"Skipped 1 file: Assets takes PNG images and sounds.":$"Skipped {skipped} files: Assets takes PNG images and sounds.");
    }
    BrowserEntry ChosenAsset()=>assetList.SelectedItem as BrowserEntry??throw new InvalidOperationException("Select an image in Assets first.");
    void AssignBrowserAsset() {var entry=ChosenAsset();
        if(SoundAssets.IsSound(entry.Id)){var players=ui.Elements.Where(e=>selected.Contains(e.Id)&&e.Type=="sound").ToArray();if(players.Length==0){AddControl("sound",0,0);players=ui.Elements.Where(e=>selected.Contains(e.Id)).ToArray();}else Change();foreach(var e in players)e.Sound=SoundAssets.Resource(entry.Id);RefreshInspector();RefreshLayers();return;}
        var targets=ui.Elements.Where(e=>selected.Contains(e.Id) && e.Type is "button" or "panel" or "scroll_panel" or "image" or "texture_region" or "label" or "progress" or "sprite" or "shape").ToArray();if(targets.Length==0)throw new InvalidOperationException("Select a button, image, label or panel on the designer first.");Change();foreach(var e in targets){e.Texture=AssetResource(entry.Id);if(e.Type is not ("image" or "texture_region" or "sprite"))e.FillEnabled=true;
            if(e.Type=="sprite" && SheetFrames(entry.Id) is var (fw,fh,n)){e.FrameWidth=fw;e.FrameHeight=fh;if(string.IsNullOrWhiteSpace(e.Clips)){e.Clips=$"play: 0-{n-1} @8";e.Value="play";}}}Draw();RefreshInspector();}
    void AssignBrowserItem() {var item=itemList.SelectedItem as BrowserEntry??throw new InvalidOperationException("Select a Minecraft item first.");var targets=ui.Elements.Where(e=>selected.Contains(e.Id)&&e.Type=="item").ToArray();if(targets.Length==0)throw new InvalidOperationException("Select an Item control, or use Add item.");Change();foreach(var e in targets)e.Item=item.Id;Draw();RefreshInspector();}
    void CreateBrowserElement(bool item,double x=16,double y=16,string? id=null) {
        id??=item?(itemList.SelectedItem as BrowserEntry)?.Id:ChosenAsset().Id;if(id==null)throw new InvalidOperationException("Select an item first.");
        // A sprite sheet from the pixel editor goes in as a Sprite that plays its frames.
        if(!item && SheetFrames(id) is var (sw,sh,sn)){var sprite=SpriteFor(id,sw,sh,sn,x,y);Change();ui.Elements.Add(sprite);selected.Clear();selected.Add(sprite.Id);Draw();RefreshInspector();return;}
        var element=new Element {Id=Unique(item?"item":"image"),Type=item?"item":"image",FillEnabled=false,Bounds=new(){X=Snap(x),Y=Snap(y),Width=item?32:100,Height=item?32:100}};
        if(item)element.Item=id;else {element.Texture=AssetResource(id);var size=AssetThumbnail(id,project.Assets[id]) is System.Windows.Media.Imaging.BitmapSource first?(Width:first.PixelWidth,Height:first.PixelHeight):ProjectStore.TextureSize(project.Assets[id]);element.Bounds.Height=Math.Max(1,100d*size.Height/size.Width);element.Bounds.Height=Math.Min(4096,element.Bounds.Height);}
        Change();element.LayerGroup=isolatedGroup;ui.Elements.Add(element);selected.Clear();selected.Add(element.Id);Draw();RefreshInspector();
    }
    List<string> AssetUses(string path) {
        var uses=new List<string>();if(!project.Assets.TryGetValue(path,out var bytes))return uses;
        bool Matches(string value)=>TextureAssets.TryGet(project,value,out var found)&&ReferenceEquals(found,bytes);
        IEnumerable<Element> All(IEnumerable<Element> elements)=>elements.SelectMany(e=>new[]{e}.Concat(All(e.RowElements)));
        foreach(var screen in project.Screens) {
            foreach(var e in All(screen.Elements)){if(Matches(e.Texture))uses.Add(screen.Id+" / "+e.Id);foreach(var h in e.Events.Values.SelectMany(v=>new[]{v.Client,v.Server}))foreach(var a in h.Actions)if(a.Type=="change_texture"&&Matches(a.Value))uses.Add(screen.Id+" / "+e.Id+" event");}
            foreach(var h in screen.Events.Values.SelectMany(v=>new[]{v.Client,v.Server}))foreach(var a in h.Actions)if(a.Type=="change_texture"&&Matches(a.Value))uses.Add(screen.Id+" screen event");
        }
        foreach(var script in project.Scripts)if(script.Value.Contains(AssetResource(path),StringComparison.Ordinal)||script.Value.Contains(Path.GetFileName(path),StringComparison.Ordinal))uses.Add(script.Key);
        return uses.Distinct().ToList();
    }
    void FindBrowserUses(){var entry=ChosenAsset();var uses=AssetUses(entry.Id);assetInfo.Text=uses.Count==0?"No known references. Dynamically constructed script paths cannot be detected.":string.Join("\n",uses);}
    void ReplaceBrowserAsset(){var entry=ChosenAsset();
        if(SoundAssets.IsSound(entry.Id)){var pick=new OpenFileDialog{Filter=SoundFilter};if(pick.ShowDialog()==true)ReplaceSoundFile(entry.Id,pick.FileName);return;}
        var dialog=new OpenFileDialog{Filter="PNG image|*.png"};if(dialog.ShowDialog()!=true)return;var bytes=File.ReadAllBytes(dialog.FileName);ProjectStore.TextureSize(bytes);DecodeTexture(bytes);Change();project.Assets[entry.Id]=bytes;project.Assets.Remove(entry.Id+TextureAssets.LayersSuffix);RefreshAssetBrowser();Draw();}
    void DeleteBrowserAsset(){var entry=ChosenAsset();
        if(SoundAssets.IsSound(entry.Id)){var id=SoundAssets.Resource(entry.Id);var users=SoundAssets.Uses(project).Where(u=>u.Sound==id).Select(u=>u.Screen+(u.Element.Length>0?" › "+u.Element:"")).ToList();if(users.Count>0)throw new InvalidOperationException("Sound is still used by:\n"+string.Join("\n",users));Change();SoundAssets.RemoveWithSidecars(project,entry.Id);RefreshAssetBrowser();return;}
        var uses=AssetUses(entry.Id);if(uses.Count>0)throw new InvalidOperationException("Image is still used by:\n"+string.Join("\n",uses));Change();project.Assets.Remove(entry.Id);project.Assets.Remove(entry.Id+".mcmeta");project.Assets.Remove(entry.Id+TextureAssets.LayersSuffix);RefreshAssetBrowser();Draw();}
    void VerifyAssetRenameAndReplace(){
        ShowDock("assets");UpdateLayout();
        // An image in use, renamed through the in-place box.
        var png=TextureAssets.Path(project.Manifest.Id,"rename_me.png");var pixels=new byte[]{10,20,30,255};var bmp=BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,pixels,4);var enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(bmp));using(var ms=new MemoryStream()){enc.Save(ms);project.Assets[png]=ms.ToArray();}
        var user=new Element{Id=Unique("image"),Type="image",Texture=AssetResource(png),Bounds=new(){X=0,Y=0,Width=8,Height=8}};ui.Elements.Add(user);RefreshAssetBrowser();
        var entry=assetList.Items.Cast<BrowserEntry>().Single(e=>e.Id==png);assetList.SelectedItem=entry;
        BeginAssetRename(entry);UpdateLayout();
        if(activeRenameBox==null||activeRenameBox.Text!="rename_me")throw new Exception("The rename box did not open with the name alone: "+activeRenameBox?.Text);
        activeRenameBox.Text="renamed";activeRenameBox.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(activeRenameBox)!,0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent});
        var renamed=TextureAssets.Path(project.Manifest.Id,"renamed.png");
        if(!project.Assets.ContainsKey(renamed)||project.Assets.ContainsKey(png)||user.Texture!=AssetResource(renamed))throw new Exception("Renaming the image did not move it or its user");
        history.Undo();user=ui.Elements.Single(e=>e.Id==user.Id);if(!project.Assets.ContainsKey(png)||user.Texture!=AssetResource(png))throw new Exception("Undo did not put the old name back");history.Redo();user=ui.Elements.Single(e=>e.Id==user.Id);
        // A refused name leaves everything as it was, with no Undo step.
        int steps=history.UndoCount;try{CommitAssetRename(renamed,"Bad Name");throw new Exception("A bad name was accepted");}catch(InvalidOperationException){}
        if(history.UndoCount!=steps||!project.Assets.ContainsKey(renamed))throw new Exception("A refused rename changed something");
        // A sound: renamed, then replaced with a .wav, keeping its ID.
        var ogg=SoundAssets.Path(project.Manifest.Id,"old_tone.ogg");project.Assets[ogg]=AudioSmokeOgg();project.Assets[ogg+SoundAssets.EffectSuffix]=[1];
        var speaker=new Element{Id=Unique("sound"),Type="sound",Sound=SoundAssets.Resource(ogg)};ui.Elements.Add(speaker);RefreshAssetBrowser();
        CommitAssetRename(ogg,"tone");var tone=SoundAssets.Path(project.Manifest.Id,"tone.ogg");
        if(!project.Assets.ContainsKey(tone)||!project.Assets.ContainsKey(tone+SoundAssets.EffectSuffix)||speaker.Sound!=SoundAssets.Resource(tone))throw new Exception("Renaming the sound did not move it, its settings or its user");
        var wav=Path.Combine(Path.GetTempPath(),"wysicraft_replace_"+Guid.NewGuid().ToString("N")[..6]+".wav");File.WriteAllBytes(wav,SmokeWav());
        try{ReplaceSoundFile(tone,wav);}finally{File.Delete(wav);}
        var toneWav=SoundAssets.Path(project.Manifest.Id,"tone.wav");
        if(!project.Assets.ContainsKey(toneWav)||project.Assets.ContainsKey(tone)||project.Assets.ContainsKey(tone+SoundAssets.EffectSuffix)||speaker.Sound!=SoundAssets.Resource(toneWav))throw new Exception("Replacing the sound with a .wav lost its ID or kept stale settings");
        ui.Elements.RemoveAll(e=>e.Id==user.Id||e.Id==speaker.Id);project.Assets.Remove(renamed);project.Assets.Remove(toneWav);RefreshAssetBrowser();Draw();
    }
    // A tenth of a second of silence as a 16-bit mono WAV.
    static byte[] SmokeWav(){int n=4410;using var ms=new MemoryStream();using var w=new BinaryWriter(ms);w.Write("RIFF"u8.ToArray());w.Write(36+n*2);w.Write("WAVEfmt "u8.ToArray());w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(44100);w.Write(44100*2);w.Write((short)2);w.Write((short)16);w.Write("data"u8.ToArray());w.Write(n*2);w.Write(new byte[n*2]);w.Flush();return ms.ToArray();}
    static byte[] AudioSmokeOgg()=>SmokeWav(); // content is not decoded until played or replaced
    void LoadItemJar(){var dialog=new OpenFileDialog{Filter="Minecraft client or mod JAR|*.jar",Multiselect=true};if(dialog.ShowDialog()!=true)return;foreach(var path in dialog.FileNames)minecraftAssets.LoadJar(path);itemImages.Clear();minecraftTextures.Clear();RefreshItemBrowser();Draw();}
    void LoadTestItems(){
        string root=Wysicraft.Core.AppFolders.Existing("MinecraftTest","1.21.1");var settings=Wysicraft.Core.AppFolders.Path("minecraft-test.json");
        if(File.Exists(settings)){using var doc=System.Text.Json.JsonDocument.Parse(File.ReadAllText(settings));if(doc.RootElement.TryGetProperty("instance",out var path))root=path.GetString()??root;}
        string catalog=Path.Combine(root,".wysicraft-test","items.json");if(!File.Exists(catalog))throw new InvalidOperationException("Start an editor Minecraft test with this version first, then choose Read test items.");
        if(new FileInfo(catalog).Length>16*1024*1024)throw new InvalidDataException("Item catalog exceeds 16 MB.");
        using var data=System.Text.Json.JsonDocument.Parse(File.ReadAllText(catalog));registeredItems=data.RootElement.EnumerateArray().Select(e=>(Id:e.GetProperty("id").GetString()!,Name:e.GetProperty("name").GetString()!)).Where(e=>Validation.Resource(e.Id)).DistinctBy(e=>e.Id).OrderBy(e=>e.Name).ToList();
        foreach(var jar in Directory.Exists(Path.Combine(root,"mods"))?Directory.EnumerateFiles(Path.Combine(root,"mods"),"*.jar"):[])try{minecraftAssets.LoadJar(jar);}catch(InvalidDataException){}
        itemImages.Clear();minecraftTextures.Clear();RefreshItemBrowser();Draw();
    }
    // ---- Replacing a sound, and renaming ----
    System.Windows.Threading.DispatcherTimer? renameTimer;BrowserEntry? renameCandidate;
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetDoubleClickTime();
    static double DoubleClickTime(){try{return Math.Clamp(GetDoubleClickTime(),200,1500);}catch{return 500;}}
    /// <summary>Swaps a project sound for another file. The sound keeps its ID (the file name without extension), so
    /// every Sound control, play_sound action and playSound call plays the new one. A different extension is fine;
    /// the Music maker or Sound effect maker settings go, because they described the old sound.</summary>
    internal void ReplaceSoundFile(string path,string file){
        var bytes=File.ReadAllBytes(file);if(bytes.Length==0||bytes.Length>ProjectStore.MaxEntry)throw new InvalidOperationException("Sounds are 1 byte to 32 MB.");
        string ext=Path.GetExtension(file).ToLowerInvariant();if(!SoundAssets.Extensions.Contains(ext))throw new InvalidOperationException("Sounds are .ogg, .mp3, .wav, .m4a or .aac.");
        try{DecodeAudio(bytes,ext);}catch(Exception ex){throw new InvalidOperationException(Path.GetFileName(file)+" could not be read as a sound: "+ex.Message);}
        string target=path[..path.LastIndexOf('.')]+ext;bool hadMaker=project.Assets.ContainsKey(path+SoundAssets.SongSuffix)||project.Assets.ContainsKey(path+SoundAssets.EffectSuffix);
        Change();SoundAssets.RemoveWithSidecars(project,path);project.Assets[target]=bytes;
        RefreshAssetBrowser();assetList.SelectedItem=assetList.Items.Cast<BrowserEntry>().FirstOrDefault(e=>e.Id==target);RefreshInspector();
        Log($"Replaced {Path.GetFileName(path)} with {Path.GetFileName(file)}: everything that plays {SoundAssets.Resource(target)} plays the new sound."+(hadMaker?" Its Music maker / Sound effect maker settings were removed, as they described the old sound.":"")
            +(ext!=".ogg"&&project.Manifest.Target!="web"?" Minecraft only plays .ogg; web and desktop exports play it.":""));
    }
    /// <summary>Edits an asset's name in place: a box over the name with the extension beside it, fixed. Enter or
    /// clicking away keeps it, Escape cancels.</summary>
    void BeginAssetRename(BrowserEntry entry){
        renameTimer?.Stop();
        if(!(entry.Id.EndsWith(".png",StringComparison.OrdinalIgnoreCase)||SoundAssets.IsSound(entry.Id)))return;
        assetList.ScrollIntoView(entry);assetList.UpdateLayout();
        var container=assetList.ItemContainerGenerator.ContainerFromItem(entry) as ListBoxItem;
        TextBlock? name=null;if(container!=null)foreach(var t in Descendants(container).OfType<TextBlock>())if(t.Text==entry.Name){name=t;break;}
        var box=new TextBox{Text=AssetRename.Stem(entry.Id),MinWidth=140,Margin=new Thickness(0),ToolTip="Lowercase letters, numbers, _ and -. Enter keeps it, Escape cancels."};
        var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(box);row.Children.Add(new TextBlock{Text=AssetRename.Extension(entry.Id),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(3,0,4,0),Opacity=0.7});
        var frame=new Border{Child=row,Background=(Brush)FindResource("PanelBrush"),BorderBrush=Brush("#3794FF"),BorderThickness=new Thickness(1),Padding=new Thickness(2)};
        var popup=new System.Windows.Controls.Primitives.Popup{Child=frame,PlacementTarget=(UIElement?)name??(UIElement?)container??assetList,Placement=System.Windows.Controls.Primitives.PlacementMode.Relative,HorizontalOffset=-4,VerticalOffset=-4,StaysOpen=false,AllowsTransparency=true};
        bool finished=false;
        void Finish(bool keep){if(finished)return;finished=true;popup.IsOpen=false;if(keep)Guard(()=>CommitAssetRename(entry.Id,box.Text));assetList.Focus();}
        box.PreviewKeyDown+=(_,k)=>{if(k.Key==Key.Enter){k.Handled=true;Finish(true);}else if(k.Key==Key.Escape){k.Handled=true;Finish(false);}};
        box.LostKeyboardFocus+=(_,_)=>Finish(true);popup.Closed+=(_,_)=>Finish(true);
        popup.IsOpen=true;box.Focus();box.SelectAll();Keyboard.Focus(box);
        activeRenameBox=box;
    }
    TextBox? activeRenameBox;
    static IEnumerable<DependencyObject> Descendants(DependencyObject root){for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var c=VisualTreeHelper.GetChild(root,i);yield return c;foreach(var d in Descendants(c))yield return d;}}
    /// <summary>Renames the asset (checked on a copy first, so a refused name changes nothing and leaves no Undo step).</summary>
    void CommitAssetRename(string path,string stem){
        stem=(stem??"").Trim();if(stem==AssetRename.Stem(path))return;
        AssetRename.Rename(Json.CloneProject(project),path,stem);
        Change();var result=AssetRename.Rename(project,path,stem);
        RefreshAssetBrowser();assetList.SelectedItem=assetList.Items.Cast<BrowserEntry>().FirstOrDefault(e=>e.Id==result.NewPath);Draw();RefreshInspector();
        Log($"Renamed {Path.GetFileName(path)} to {Path.GetFileName(result.NewPath)}"+(result.References>0?$", and updated {result.References} place{(result.References==1?"":"s")} that use it.":"."));
        if(result.ScriptsMentioningOldName.Count>0)Log($"Scripts mention \"{AssetRename.Stem(path)}\" and were not changed (scripts can build names as they run): {string.Join(", ",result.ScriptsMentioningOldName)}. Update them if they refer to this {(SoundAssets.IsSound(result.NewPath)?"sound":"image")}.");
    }
    internal void VerifyAssetBrowser(string output) {
        var pixels=new byte[]{255,120,40,255};var bitmap=BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,pixels,4);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=new MemoryStream();encoder.Save(stream);
        var path=TextureAssets.Path(project.Manifest.Id,"browser_test.png");project.Assets[path]=stream.ToArray();RefreshAssetBrowser();assetList.SelectedItem=assetList.Items.Cast<BrowserEntry>().Single(e=>e.Id==path);
        CreateBrowserElement(false);var created=ui.Elements.Single(e=>selected.Contains(e.Id));if(created.Texture!=AssetResource(path)||AssetUses(path).Count!=1)throw new Exception("Asset assignment/reference detection failed");
        bool blocked=false;try{DeleteBrowserAsset();}catch(InvalidOperationException){blocked=true;}if(!blocked)throw new Exception("Used asset was deletable");
        history.Undo();if(ui.Elements.Any(e=>e.Id==created.Id))throw new Exception("Undo did not remove created image");history.Redo();
        var jar=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".gradle","caches","neoformruntime","artifacts","minecraft_1.21.1_client.jar");minecraftAssets.LoadJar(jar);itemImages.Clear();minecraftTextures.Clear();itemSearch.Text="diamond sword";RefreshItemBrowser();
        // Full blocks draw as the inventory cube; flat items and non-cube blocks keep their texture.
        if(minecraftAssets.CubeFaces("minecraft:bricks")==null || minecraftAssets.CubeFaces("minecraft:furnace")==null || minecraftAssets.CubeFaces("minecraft:diamond_sword")!=null || minecraftAssets.CubeFaces("minecraft:oak_stairs")!=null)throw new Exception("Block cube detection failed");
        if(!TryTexture("minecraft:textures/block/bricks.png",out _) || TryTexture("minecraft:textures/block/not_a_block.png",out _))throw new Exception("Minecraft texture lookup failed");
        // Animated textures: prismarine plays one square frame at a time (bricks are still).
        if(!TryTexture("minecraft:textures/block/prismarine.png",out var prismarine) || AnimationOf("minecraft:textures/block/prismarine.png",prismarine) is not {} prismarineAnimation || prismarineAnimation.Frames.Count<2
           || AnimationOf("minecraft:textures/block/bricks.png",TryTexture("minecraft:textures/block/bricks.png",out var bricks)?bricks:[])!=null)throw new Exception("Prismarine animation not detected");
        {
            var animated=TextureImage("minecraft:textures/block/prismarine.png",prismarine);
            if(!animated.HasAnimatedProperties || animated.Source is not BitmapSource frame || frame.PixelWidth!=frame.PixelHeight)throw new Exception("Prismarine image does not animate one square frame");
            if(TextureBrush("minecraft:textures/block/prismarine.png",prismarine).Viewbox.Height>=1)throw new Exception("Prismarine skin shows the whole strip");
        }
        {var sheet=new DrawingVisual();using(var dc=sheet.RenderOpen()){int i=0;foreach(var id in new[]{"minecraft:bricks","minecraft:furnace","minecraft:oak_log","minecraft:grass_block","minecraft:diamond_sword"}){dc.DrawImage(ItemImage(id)!,new Rect(i++*128,0,128,128));}}var png=new RenderTargetBitmap(640,128,96,96,PixelFormats.Pbgra32);png.Render(sheet);var enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(png));using var file=File.Create(output+".items.png");enc.Save(file);}
        itemList.SelectedItem=itemList.Items.Cast<BrowserEntry>().Single(e=>e.Id=="minecraft:diamond_sword");if(((BrowserEntry)itemList.SelectedItem).Image==null)throw new Exception("Minecraft texture preview missing");CreateBrowserElement(true);if(ui.Elements.Single(e=>selected.Contains(e.Id)).Item!="minecraft:diamond_sword")throw new Exception("Item assignment failed");
        foreach(string id in new[]{"assets","items"}){ShowDock(id);var pane=Workspace.Layout.Descendents().OfType<AvalonDock.Layout.LayoutAnchorable>().First(p=>p.ContentId==id);pane.Float();Flush();pane.Dock();Flush();}
        // AvalonDock shows floating windows asynchronously; let that finish before the test moves on or closes the app.
        void Flush()=>Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        // The image and item created above can sit over sample buttons; remove them before the button hit test.
        ui.Elements.RemoveAll(e=>e.Id==created.Id || e.Type=="item" && e.Item=="minecraft:diamond_sword");selected.Clear();Draw();UpdateLayout();
        VerifyCanvasSelection();ShowDock("items");UpdateLayout();
        VerifyAssetRenameAndReplace();
        dirty=false;File.WriteAllText(output,"PASS: PNG thumbnail/imported bytes, create image, reference guard, undo/redo, real Minecraft item lookup/texture, create item, panel docking and canvas selection, renaming an image and a sound in place (references follow, one Undo, refused names change nothing), replacing a sound with another format");
    }
}
