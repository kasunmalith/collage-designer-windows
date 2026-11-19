using SkiaSharp;
using System.IO;
namespace CollageDesigner;
public static class Rendering
{
    public static SKRect SlotRect(LayoutSlot s,int width,int height,int margin,int gap)
    {
        float iw=width-2*margin,ih=height-2*margin;
        float l=margin+(float)s.X*iw+(s.X>0?gap/2f:0), t=margin+(float)s.Y*ih+(s.Y>0?gap/2f:0);
        float r=margin+(float)(s.X+s.Width)*iw-(s.X+s.Width<.999999?gap/2f:0);
        float b=margin+(float)(s.Y+s.Height)*ih-(s.Y+s.Height<.999999?gap/2f:0);
        return new SKRect(l,t,r,b);
    }
    public static SKBitmap LoadUpright(string path,int maxSide=0)
    {
        using var stream=File.OpenRead(path); using var codec=SKCodec.Create(stream) ?? throw new InvalidDataException("This JPG cannot be decoded.");
        var origin=codec.EncodedOrigin; int ow=codec.Info.Width,oh=codec.Info.Height;
        double factor=maxSide>0?Math.Min(1.0,(double)maxSide/Math.Max(ow,oh)):1;
        var scaled=codec.GetScaledDimensions((float)factor);
        var info=new SKImageInfo(Math.Max(1,scaled.Width),Math.Max(1,scaled.Height),SKColorType.Rgba8888,SKAlphaType.Premul);
        using var raw=new SKBitmap(info); var result=codec.GetPixels(info,raw.GetPixels());
        if(result!=SKCodecResult.Success && result!=SKCodecResult.IncompleteInput)throw new InvalidDataException($"Could not decode JPG ({result}).");
        bool swap=origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var upright=new SKBitmap(swap?raw.Height:raw.Width,swap?raw.Width:raw.Height,true);
        using(var canvas=new SKCanvas(upright))
        {
            switch(origin)
            {
                case SKEncodedOrigin.TopRight: canvas.Translate(upright.Width,0);canvas.Scale(-1,1);break;
                case SKEncodedOrigin.BottomRight:canvas.Translate(upright.Width,upright.Height);canvas.RotateDegrees(180);break;
                case SKEncodedOrigin.BottomLeft:canvas.Translate(0,upright.Height);canvas.Scale(1,-1);break;
                case SKEncodedOrigin.LeftTop:canvas.RotateDegrees(90);canvas.Scale(-1,1);break;
                case SKEncodedOrigin.RightTop:canvas.Translate(upright.Width,0);canvas.RotateDegrees(90);break;
                case SKEncodedOrigin.RightBottom:canvas.Translate(upright.Width,upright.Height);canvas.RotateDegrees(-90);canvas.Scale(-1,1);break;
                case SKEncodedOrigin.LeftBottom:canvas.Translate(0,upright.Height);canvas.RotateDegrees(-90);break;
            }
            canvas.DrawBitmap(raw,0,0);
        }
        return upright;
    }
    public static (int Width,int Height) ReadSize(string path)
    {
        using var f=File.OpenRead(path);using var codec=SKCodec.Create(f)??throw new InvalidDataException("Unsupported or corrupt JPG.");
        bool swap=codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        return swap?(codec.Info.Height,codec.Info.Width):(codec.Info.Width,codec.Info.Height);
    }
    public static byte[] MakePreview(string path,int maxSide=1000)
    {
        using var bitmap=LoadUpright(path,maxSide);using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(SKEncodedImageFormat.Jpeg,86);return data.ToArray();
    }
    public static void DrawPhoto(SKCanvas canvas,SKBitmap bitmap,SKRect frame,PhotoTransform transform)
    {
        if(frame.Width<=0||frame.Height<=0)return;
        double cover=Math.Max(frame.Width/bitmap.Width,frame.Height/bitmap.Height);
        double scale=cover*Math.Clamp(transform.Scale,1,5);
        double dw=bitmap.Width*scale,dh=bitmap.Height*scale;
        double overX=Math.Max(0,dw-frame.Width),overY=Math.Max(0,dh-frame.Height);
        double left=frame.Left-overX/2+Math.Clamp(transform.PanX,-1,1)*overX/2;
        double top=frame.Top-overY/2+Math.Clamp(transform.PanY,-1,1)*overY/2;
        canvas.Save();canvas.ClipRect(frame);
        using var paint=new SKPaint{FilterQuality=SKFilterQuality.High,IsAntialias=true};
        canvas.DrawBitmap(bitmap,new SKRect((float)left,(float)top,(float)(left+dw),(float)(top+dh)),paint);canvas.Restore();
    }
    public static byte[] Render(ProjectData data,IReadOnlyList<LayoutTemplate> layouts,bool preview,int previewSide=1100,IProgress<int>? progress=null)
    {
        int w=data.PixelWidth,h=data.PixelHeight;
        if(w<1||h<1||w>30000||h>30000||(long)w*h>220_000_000)throw new InvalidOperationException("Canvas size exceeds the supported 220 megapixel limit.");
        var layout=layouts[Math.Clamp(data.LayoutIndex,0,layouts.Count-1)];
        var slots=LayoutResizeEngine.Slots(data,layout);
        if(data.Margin<0||data.Gap<0||w<=2*data.Margin||h<=2*data.Margin||slots.Any(s=>{var frame=SlotRect(s,w,h,data.Margin,data.Gap);return frame.Width<1||frame.Height<1||frame.Left<-.5||frame.Top<-.5||frame.Right>w+.5||frame.Bottom>h+.5;}))throw new InvalidOperationException("Photo frames, gap, or outer margin do not fit inside this canvas. Reduce them or increase the canvas size.");
        double ratio=preview?Math.Min(1,(double)previewSide/Math.Max(w,h)):1;
        int rw=Math.Max(1,(int)Math.Round(w*ratio)),rh=Math.Max(1,(int)Math.Round(h*ratio));
        using var surface=SKSurface.Create(new SKImageInfo(rw,rh,SKColorType.Rgba8888,SKAlphaType.Premul))??throw new OutOfMemoryException();
        var canvas=surface.Canvas;canvas.Clear(SKColor.Parse(data.Background));
        var drawOrder=data.LayoutCollection=="RandomSpace"?LayerStackEngine.Order(data,slots.Count):Enumerable.Range(0,slots.Count).ToList();
        if(preview&&data.PreviewFrontSlots.Count>0){foreach(int slot in data.PreviewFrontSlots.Where(i=>i>=0&&i<slots.Count).Distinct()){drawOrder.Remove(slot);drawOrder.Add(slot);}}
        else if(preview&&data.PreviewFrontSlot>=0&&data.PreviewFrontSlot<slots.Count){drawOrder.Remove(data.PreviewFrontSlot);drawOrder.Add(data.PreviewFrontSlot);}
        foreach(int i in drawOrder)
        {
            if(i>=data.Assignments.Count)break;int pi=data.Assignments[i];if(pi<0||pi>=data.Photos.Count)continue;
            var photo=data.Photos[pi];if(!File.Exists(photo.Path))continue;
            using var bitmap=preview && photo.Preview!=null?SKBitmap.Decode(photo.Preview):LoadUpright(photo.Path);
            var rect=SlotRect(slots[i],w,h,data.Margin,data.Gap);
            var scaled=new SKRect((float)(rect.Left*ratio),(float)(rect.Top*ratio),(float)(rect.Right*ratio),(float)(rect.Bottom*ratio));
            DrawPhoto(canvas,bitmap,scaled,photo.Transform);progress?.Report((i+1)*100/slots.Count);
        }
        canvas.Flush();using var img=surface.Snapshot();using var bytes=img.Encode(preview?SKEncodedImageFormat.Png:(data.JpegQuality<0?SKEncodedImageFormat.Png:SKEncodedImageFormat.Jpeg),preview?100:Math.Clamp(data.JpegQuality,1,100));return bytes.ToArray();
    }
}
