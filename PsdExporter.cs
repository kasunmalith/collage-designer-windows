using System.Buffers.Binary;
using System.IO;
using System.Text;
using SkiaSharp;

namespace CollageDesigner;

public static class PsdExporter
{
    sealed record Layer(string Name,int Left,int Top,int Width,int Height,int PhotoIndex,SKRect Target);

    public static void Export(ProjectData data,IReadOnlyList<LayoutTemplate> layouts,string path,IProgress<int>? progress=null)
    {
        int width=data.PixelWidth,height=data.PixelHeight;
        if(width<1||height<1||width>30000||height>30000||(long)width*height>80_000_000)
            throw new InvalidOperationException("Layered PSD export supports canvases up to 80 megapixels and 30,000 pixels per side.");
        if(layouts.Count==0)throw new InvalidOperationException("Import photos before exporting a PSD.");
        var slots=LayoutResizeEngine.Slots(data,layouts[Math.Clamp(data.LayoutIndex,0,layouts.Count-1)]);
        if(data.Margin<0||data.Gap<0||width<=2*data.Margin||height<=2*data.Margin)
            throw new InvalidOperationException("Canvas margin or photo gap is invalid.");
        var layers=new List<Layer>();
        var drawOrder=data.LayoutCollection=="RandomSpace"?LayerStackEngine.Order(data,slots.Count):Enumerable.Range(0,slots.Count).ToList();
        foreach(int i in drawOrder.AsEnumerable().Reverse())
        {
            if(i>=data.Assignments.Count)continue;
            int photoIndex=data.Assignments[i];
            if(photoIndex<0||photoIndex>=data.Photos.Count)continue;
            var rect=Rendering.SlotRect(slots[i],width,height,data.Margin,data.Gap);
            int left=(int)Math.Floor(rect.Left),top=(int)Math.Floor(rect.Top);
            int right=(int)Math.Ceiling(rect.Right),bottom=(int)Math.Ceiling(rect.Bottom);
            if(left<0||top<0||right>width||bottom>height||right<=left||bottom<=top)
                throw new InvalidOperationException("Photo frames do not fit this canvas. Adjust gap, margin, or layout.");
            layers.Add(new Layer(Path.GetFileNameWithoutExtension(data.Photos[photoIndex].Path),left,top,right-left,bottom-top,photoIndex,new SKRect(rect.Left-left,rect.Top-top,rect.Right-left,rect.Bottom-top)));
        }
        layers.Add(new Layer("Background",0,0,width,height,-1,new SKRect(0,0,width,height)));
        long pixels=(long)width*height+layers.Sum(layer=>(long)layer.Width*layer.Height);
        // PSD version 1 uses 32-bit section lengths. Leave room for records and resources.
        if(pixels*4+1_000_000L>1_800_000_000L)
            throw new InvalidOperationException("The layered PSD would exceed the safe PSD file size. Reduce the canvas size or photo count.");

        string temporary=path+".writing";
        try
        {
            using(var file=new FileStream(temporary,FileMode.Create,FileAccess.ReadWrite,FileShare.None))
            {
                WriteSignature(file,"8BPS");WriteU16(file,1);file.Write(new byte[6]);WriteU16(file,4);
                WriteU32(file,(uint)height);WriteU32(file,(uint)width);WriteU16(file,8);WriteU16(file,3);
                WriteU32(file,0); // Color mode data.
                WriteResolutionResource(file,data.Dpi);
                long maskSizePosition=file.Position;WriteU32(file,0);
                long maskStart=file.Position,layerSizePosition=file.Position;WriteU32(file,0);
                long layerStart=file.Position;WriteI16(file,checked((short)layers.Count));
                foreach(var layer in layers)WriteLayerRecord(file,layer);
                for(int i=0;i<layers.Count;i++)
                {
                    var layer=layers[i];
                    using var bitmap=RenderLayer(data,layer);
                    WritePlanes(file,bitmap,layer.Width,layer.Height);
                    progress?.Report((i+1)*75/layers.Count);
                }
                long layerEnd=file.Position;
                PatchU32(file,layerSizePosition,checked((uint)(layerEnd-layerStart)));
                WriteU32(file,0); // Global layer mask info.
                PatchU32(file,maskSizePosition,checked((uint)(file.Position-maskStart)));
                WriteU16(file,0); // Raw merged image compression.
                using var merged=RenderMerged(data,slots,width,height);
                WriteRawPlanes(file,merged,width,height);
                progress?.Report(100);
                file.Flush(true);
            }
            File.Move(temporary,path,true);
        }
        catch{if(File.Exists(temporary))File.Delete(temporary);throw;}
    }

    static SKBitmap RenderLayer(ProjectData data,Layer layer)
    {
        var bitmap=new SKBitmap(new SKImageInfo(layer.Width,layer.Height,SKColorType.Rgba8888,SKAlphaType.Premul));
        try
        {
            using var canvas=new SKCanvas(bitmap);canvas.Clear(SKColors.Transparent);
            if(layer.PhotoIndex<0)canvas.Clear(SKColor.Parse(data.Background));
            else
            {
                using var original=Rendering.LoadUpright(data.Photos[layer.PhotoIndex].Path);
                Rendering.DrawPhoto(canvas,original,layer.Target,data.Photos[layer.PhotoIndex].Transform);
            }
            canvas.Flush();return bitmap;
        }
        catch{bitmap.Dispose();throw;}
    }

    static SKBitmap RenderMerged(ProjectData data,IReadOnlyList<LayoutSlot> slots,int width,int height)
    {
        var bitmap=new SKBitmap(new SKImageInfo(width,height,SKColorType.Rgba8888,SKAlphaType.Premul));
        try
        {
            using var canvas=new SKCanvas(bitmap);canvas.Clear(SKColor.Parse(data.Background));
            var drawOrder=data.LayoutCollection=="RandomSpace"?LayerStackEngine.Order(data,slots.Count):Enumerable.Range(0,slots.Count).ToList();
            foreach(int i in drawOrder)
            {
                if(i>=data.Assignments.Count)continue;
                int photoIndex=data.Assignments[i];if(photoIndex<0||photoIndex>=data.Photos.Count)continue;
                using var original=Rendering.LoadUpright(data.Photos[photoIndex].Path);
                Rendering.DrawPhoto(canvas,original,Rendering.SlotRect(slots[i],width,height,data.Margin,data.Gap),data.Photos[photoIndex].Transform);
            }
            canvas.Flush();return bitmap;
        }
        catch{bitmap.Dispose();throw;}
    }

    static void WriteLayerRecord(Stream stream,Layer layer)
    {
        WriteU32(stream,(uint)layer.Top);WriteU32(stream,(uint)layer.Left);
        WriteU32(stream,(uint)(layer.Top+layer.Height));WriteU32(stream,(uint)(layer.Left+layer.Width));
        WriteU16(stream,4);
        uint channelSize=checked((uint)(2L+(long)layer.Width*layer.Height));
        foreach(short id in new short[]{0,1,2,-1}){WriteI16(stream,id);WriteU32(stream,channelSize);}
        WriteSignature(stream,"8BIM");WriteSignature(stream,"norm");
        stream.WriteByte(255);stream.WriteByte(0);stream.WriteByte(0);stream.WriteByte(0);
        string name=layer.Name.Length>200?layer.Name[..200]:layer.Name;
        byte[] ascii=Encoding.ASCII.GetBytes(name.Select(c=>c<=127?c:'_').ToArray());
        int pascalLength=1+ascii.Length,pascalPadding=(4-pascalLength%4)%4;
        uint unicodeBytes=checked((uint)(4+name.Length*2));
        WriteU32(stream,checked((uint)(8+pascalLength+pascalPadding+12+unicodeBytes)));
        WriteU32(stream,0);WriteU32(stream,0); // Mask data and blending ranges.
        stream.WriteByte((byte)ascii.Length);stream.Write(ascii);
        for(int i=0;i<pascalPadding;i++)stream.WriteByte(0);
        WriteSignature(stream,"8BIM");WriteSignature(stream,"luni");WriteU32(stream,unicodeBytes);
        WriteU32(stream,(uint)name.Length);
        foreach(char c in name)WriteU16(stream,c);
    }

    static void WritePlanes(Stream stream,SKBitmap bitmap,int width,int height)
    {
        byte[] pixels=bitmap.Bytes;
        for(int channel=0;channel<4;channel++)
        {
            WriteU16(stream,0);
            WritePlane(stream,pixels,bitmap.RowBytes,width,height,channel);
        }
    }

    static void WriteRawPlanes(Stream stream,SKBitmap bitmap,int width,int height)
    {
        byte[] pixels=bitmap.Bytes;
        for(int channel=0;channel<4;channel++)WritePlane(stream,pixels,bitmap.RowBytes,width,height,channel);
    }

    static void WritePlane(Stream stream,byte[] pixels,int rowBytes,int width,int height,int channel)
    {
        byte[] row=new byte[width];
        for(int y=0;y<height;y++)
        {
            int offset=y*rowBytes+channel;
            for(int x=0;x<width;x++)
            {
                int pixel=offset+x*4,alpha=pixels[pixel-channel+3],component=pixels[pixel];
                row[x]=(byte)(channel==3||alpha==255?component:alpha==0?0:Math.Min(255,(component*255+alpha/2)/alpha));
            }
            stream.Write(row);
        }
    }

    static void WriteResolutionResource(Stream stream,int dpi)
    {
        WriteU32(stream,28);WriteSignature(stream,"8BIM");WriteU16(stream,1005);
        stream.WriteByte(0);stream.WriteByte(0);WriteU32(stream,16);
        WriteU32(stream,(uint)dpi<<16);WriteU16(stream,1);WriteU16(stream,1);
        WriteU32(stream,(uint)dpi<<16);WriteU16(stream,1);WriteU16(stream,1);
    }

    static void PatchU32(Stream stream,long position,uint value)
    {
        long end=stream.Position;stream.Position=position;WriteU32(stream,value);stream.Position=end;
    }
    static void WriteSignature(Stream stream,string value)=>stream.Write(Encoding.ASCII.GetBytes(value));
    static void WriteI16(Stream stream,short value)=>WriteU16(stream,unchecked((ushort)value));
    static void WriteU16(Stream stream,int value){Span<byte> bytes=stackalloc byte[2];BinaryPrimitives.WriteUInt16BigEndian(bytes,checked((ushort)value));stream.Write(bytes);}
    static void WriteU32(Stream stream,uint value){Span<byte> bytes=stackalloc byte[4];BinaryPrimitives.WriteUInt32BigEndian(bytes,value);stream.Write(bytes);}
}
