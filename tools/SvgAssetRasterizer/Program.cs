using SkiaSharp;
using Svg.Skia;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: SvgAssetRasterizer <svg-path> <assets-dir>");
    return 1;
}

string svgPath = Path.GetFullPath(args[0]);
string assetsDir = Path.GetFullPath(args[1]);

if (!File.Exists(svgPath))
{
    Console.Error.WriteLine($"SVG not found: {svgPath}");
    return 2;
}

if (!Directory.Exists(assetsDir))
{
    Console.Error.WriteLine($"Assets directory not found: {assetsDir}");
    return 3;
}

var targets = new (string Name, int Width, int Height)[]
{
    ("Square150x150Logo.scale-200.png", 300, 300),
    ("Square44x44Logo.scale-200.png", 88, 88),
    ("Square44x44Logo.targetsize-24_altform-unplated.png", 24, 24),
    ("StoreLogo.png", 50, 50),
    ("LockScreenLogo.scale-200.png", 48, 48),
    ("Wide310x150Logo.scale-200.png", 620, 300),
    ("SplashScreen.scale-200.png", 1240, 600),
};

var svg = new SKSvg();
using var input = File.OpenRead(svgPath);
var picture = svg.Load(input);
if (picture is null || picture.CullRect.Width <= 0 || picture.CullRect.Height <= 0)
{
    Console.Error.WriteLine($"Failed to load SVG: {svgPath}");
    return 4;
}

SKRect bounds = picture.CullRect;

foreach (var target in targets)
{
    using var bitmap = new SKBitmap(target.Width, target.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(SKColors.Transparent);

    float scale = Math.Min(target.Width / bounds.Width, target.Height / bounds.Height);
    float scaledWidth = bounds.Width * scale;
    float scaledHeight = bounds.Height * scale;
    float offsetX = (target.Width - scaledWidth) / 2f;
    float offsetY = (target.Height - scaledHeight) / 2f;

    canvas.Translate(offsetX, offsetY);
    canvas.Translate(-bounds.Left * scale, -bounds.Top * scale);
    canvas.Scale(scale);
    canvas.DrawPicture(picture);
    canvas.Flush();

    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    using var stream = File.Open(Path.Combine(assetsDir, target.Name), FileMode.Create, FileAccess.Write);
    data.SaveTo(stream);
}

return 0;
