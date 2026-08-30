using BulkImagePipeline;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;


// usage
CreateWatermark("watermark.png", "© ThanhSE");
using var wm = Image.Load("watermark.png");
var processor = new BulkImagePipelineProcessor(bytes => AddWatermark(bytes, wm));
await processor.ProcessAsync("test.zip", "output.zip");

static byte[] AddWatermark(byte[] input, Image watermark)
{
    using var image = Image.Load(input);
    var position = new Point(image.Width - watermark.Width - 10,
                             image.Height - watermark.Height - 10);
    image.Mutate(ctx => ctx.DrawImage(watermark, position, opacity: 0.5f));

    using var ms = new MemoryStream();
    image.Save(ms, image.Metadata.DecodedImageFormat!);
    return ms.ToArray();
}

static void CreateWatermark(string outputPath, string text)
{
    Font font = SystemFonts.CreateFont("Arial", 48, FontStyle.Bold);
    const float maxWidth = 10_000f;

    const int padding = 20;
    RichTextOptions options = new(font)
    {
        Origin = new PointF(padding, padding)
    };

    // Measure with the same options that will be used for drawing
    TextBlock block = new(text, options);
    TextMetrics metrics = block.Measure(maxWidth);

    using Image<Rgba32> image = new(
        (int)MathF.Ceiling(metrics.Advance.Width) + padding * 2,
        (int)MathF.Ceiling(metrics.Advance.Height) + padding * 2); // transparent by default

    image.Mutate(ctx => ctx.Paint(canvas =>
    {
        canvas.DrawText(block, new PointF(padding, padding), maxWidth,
                        Brushes.Solid(Color.White.WithAlpha(0.6f)), pen: null);
    }));

    image.Save(outputPath);
}
