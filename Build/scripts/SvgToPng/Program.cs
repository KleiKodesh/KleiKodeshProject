/// <summary>
/// SvgToPng — converts SVG files to PNG at a specified size.
/// Usage: dotnet run -- &lt;outputSize&gt; &lt;svgFile1&gt; [svgFile2 ...]
/// </summary>

using Svg.Skia;
using SkiaSharp;

if (args.Length < 2)
{
    Console.WriteLine("Usage: dotnet run -- <size> <svgFile1> [svgFile2 ...]");
    return 1;
}

if (!int.TryParse(args[0], out int size) || size <= 0)
{
    Console.Error.WriteLine($"Invalid size: {args[0]}");
    return 1;
}

var svgFiles = args.Skip(1)
    .SelectMany(a => a.Contains('*')
        ? Directory.GetFiles(Path.GetDirectoryName(a) ?? ".", Path.GetFileName(a))
        : new[] { a })
    .ToList();

int ok = 0, failed = 0;

foreach (var svgPath in svgFiles)
{
    var fullPath = Path.GetFullPath(svgPath);
    if (!File.Exists(fullPath))
    {
        Console.Error.WriteLine($"NOT FOUND: {fullPath}");
        failed++;
        continue;
    }

    var pngPath = Path.ChangeExtension(fullPath, ".png");

    try
    {
        using var svg = new SKSvg();
        svg.Load(fullPath);
        var picture = svg.Picture ?? throw new Exception("Failed to parse SVG");

        var bounds = picture.CullRect;
        Console.WriteLine($"  Bounds: {bounds.Width}x{bounds.Height} at ({bounds.Left},{bounds.Top})");

        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new Exception($"Empty bounds: {bounds}");

        using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(size / bounds.Width, size / bounds.Height);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);
        canvas.Flush();

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(pngPath);
        data.SaveTo(stream);

        Console.WriteLine($"OK  {size}x{size}  {Path.GetFileName(pngPath)}  ({new FileInfo(pngPath).Length / 1024}KB)");
        ok++;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL: {Path.GetFileName(svgPath)} — {ex.Message}");
        failed++;
    }
}

Console.WriteLine($"\n{ok} converted, {failed} failed.");
return failed > 0 ? 1 : 0;
