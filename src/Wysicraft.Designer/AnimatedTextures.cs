using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Wysicraft.Core;
using Wysicraft.Packaging;
namespace Wysicraft.Designer;

// Animated textures (Minecraft's .png.mcmeta, e.g. prismarine): frames stacked in the PNG, played on the canvas,
// in Preview and in the game. Images and skins show one frame at a time instead of the whole strip.
public partial class MainWindow
{
    readonly Dictionary<string,string?> minecraftMetas=[];

    // The animation for a texture, from a .png.mcmeta beside it in the project or in the loaded Minecraft JAR.
    TextureAnimation? AnimationOf(string resource,byte[] png)
    {
        string? meta=null;
        if(TextureAssets.TryGet(project,resource+".mcmeta",out var bytes))meta=Encoding.UTF8.GetString(bytes);
        else if(!minecraftMetas.TryGetValue(resource,out meta)){try{meta=minecraftAssets.TextureMeta(resource);}catch{meta=null;}minecraftMetas[resource]=meta;}
        if(meta==null)return null;
        try{var size=ProjectStore.TextureSize(png);return TextureAnimation.Parse(meta,size.Width,size.Height);}catch{return null;}
    }
    static Int32Rect FramePixels(TextureAnimation a,int index)=>new(a.Column(index)*a.FrameWidth,a.Row(index)*a.FrameHeight,a.FrameWidth,a.FrameHeight);

    // An Image control for a texture; animated textures cycle their frames.
    Image TextureImage(string resource,byte[] png)
    {
        var image=new Image {Stretch=Stretch.Fill};SetImageTexture(image,resource,png);return image;
    }
    void SetImageTexture(Image image,string resource,byte[] png)
    {
        image.BeginAnimation(Image.SourceProperty,null);image.Tag=resource;
        BitmapSource source=DecodeTexture(png);var a=AnimationOf(resource,png);
        if(a==null){image.Source=source;return;}
        var frames=new Dictionary<int,BitmapSource>();BitmapSource Frame(int i){if(!frames.TryGetValue(i,out var f)){f=new CroppedBitmap(source,FramePixels(a,i));f.Freeze();frames[i]=f;}return f;}
        var animation=new ObjectAnimationUsingKeyFrames {Duration=TimeSpan.FromMilliseconds(a.TotalTicks*50.0),RepeatBehavior=RepeatBehavior.Forever};
        long at=0;foreach(var frame in a.Frames){animation.KeyFrames.Add(new DiscreteObjectKeyFrame(Frame(frame.Index),KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(at*50.0))));at+=frame.Ticks;}
        image.Source=Frame(a.Frames[0].Index);
        if(a.Frames.Count>1)image.BeginAnimation(Image.SourceProperty,animation);
    }
    // Preview: swap an Image's texture only when it actually changed, so a running animation isn't restarted every refresh.
    internal void UpdateImageTexture(Image image,string resource)
    {
        if(Equals(image.Tag,resource))return;
        if(TryTexture(resource,out var png))SetImageTexture(image,resource,png);
    }
    // A brush for skins; animated textures show one frame at a time.
    ImageBrush TextureBrush(string resource,byte[] png)
    {
        BitmapSource source=DecodeTexture(png);var brush=new ImageBrush(source) {Stretch=Stretch.Fill};
        var a=AnimationOf(resource,png);if(a==null)return brush;
        Rect Relative(int i){var p=FramePixels(a,i);return new Rect((double)p.X/source.PixelWidth,(double)p.Y/source.PixelHeight,(double)p.Width/source.PixelWidth,(double)p.Height/source.PixelHeight);}
        brush.ViewboxUnits=BrushMappingMode.RelativeToBoundingBox;brush.Viewbox=Relative(a.Frames[0].Index);
        if(a.Frames.Count>1){
            var animation=new RectAnimationUsingKeyFrames {Duration=TimeSpan.FromMilliseconds(a.TotalTicks*50.0),RepeatBehavior=RepeatBehavior.Forever};
            long at=0;foreach(var frame in a.Frames){animation.KeyFrames.Add(new DiscreteRectKeyFrame(Relative(frame.Index),KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(at*50.0))));at+=frame.Ticks;}
            brush.BeginAnimation(ImageBrush.ViewboxProperty,animation);
        }
        return brush;
    }
    // Asset list thumbnail and default Image size: the first frame of an animated texture, not the whole strip.
    ImageSource AssetThumbnail(string path,byte[] png)
    {
        BitmapSource source=DecodeTexture(png);
        if(SheetFrames(path) is var (fw,fh,_) && fw<=source.PixelWidth && fh<=source.PixelHeight)return new CroppedBitmap(source,new Int32Rect(0,0,fw,fh)); // a sprite sheet: its first frame
        if(!project.Assets.TryGetValue(path+".mcmeta",out var meta))return source;
        var a=TextureAnimation.Parse(Encoding.UTF8.GetString(meta),source.PixelWidth,source.PixelHeight);
        return a==null?source:new CroppedBitmap(source,FramePixels(a,a.Frames[0].Index));
    }
}
