using System.Text;
using SkiaSharp;

namespace TgTui.Media;

/// <summary>
/// Encodes raster images as DEC sixel graphics (DCS q ... ST).
/// </summary>
public static class SixelImageRenderer
{
    private const int MaxPaletteColors = 64;
    private const int MaxBands = 40;

    public static string RenderFile(string path, int maxCellWidth)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return HalfBlockImageRenderer.UnavailablePlaceholder;

        try
        {
            using var bitmap = SKBitmap.Decode(path);
            if (bitmap is null)
                return HalfBlockImageRenderer.UnavailablePlaceholder;

            return Render(bitmap, maxCellWidth);
        }
        catch
        {
            return HalfBlockImageRenderer.UnavailablePlaceholder;
        }
    }

    private static string Render(SKBitmap source, int maxCellWidth)
    {
        if (maxCellWidth < 1)
            maxCellWidth = 1;

        using var bitmap = FitToWidth(source, maxCellWidth, maxBands: MaxBands);
        var width = bitmap.Width;
        var height = bitmap.Height;
        var palette = BuildPalette(bitmap, MaxPaletteColors);
        if (palette.Count == 0)
            return HalfBlockImageRenderer.UnavailablePlaceholder;

        var colorToIndex = new Dictionary<uint, int>(palette.Count);
        for (var i = 0; i < palette.Count; i++)
            colorToIndex[Quantize(palette[i])] = i;

        var sb = new StringBuilder(width * ((height + 5) / 6) * 8 + palette.Count * 24);
        sb.Append("\u001bPq");

        for (var colorIndex = 0; colorIndex < palette.Count; colorIndex++)
        {
            var color = palette[colorIndex];
            sb.Append('#');
            sb.Append(colorIndex);
            sb.Append(";2;");
            sb.Append(color.Red);
            sb.Append(';');
            sb.Append(color.Green);
            sb.Append(';');
            sb.Append(color.Blue);

            for (var y = 0; y < height; y += 6)
            {
                var bandHeight = Math.Min(6, height - y);
                for (var x = 0; x < width; x++)
                {
                    var value = 0;
                    for (var bit = 0; bit < bandHeight; bit++)
                    {
                        if (colorToIndex.TryGetValue(Quantize(bitmap.GetPixel(x, y + bit)), out var idx)
                            && idx == colorIndex)
                            value |= 1 << bit;
                    }

                    sb.Append(SixelChar(value));
                }

                sb.Append('$');
            }
        }

        sb.Append("\u001b\\");
        return sb.ToString();
    }

    private static List<SKColor> BuildPalette(SKBitmap bitmap, int maxColors)
    {
        var counts = new Dictionary<uint, (SKColor Color, int Count)>();
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Alpha < 16)
                    continue;

                var key = Quantize(pixel);
                if (counts.TryGetValue(key, out var existing))
                    counts[key] = (existing.Color, existing.Count + 1);
                else
                    counts[key] = (pixel, 1);
            }
        }

        return counts.Values
            .OrderByDescending(entry => entry.Count)
            .Take(maxColors)
            .Select(entry => entry.Color)
            .ToList();
    }

    private static uint Quantize(SKColor color) =>
        (uint)((color.Red & 0xF8) << 16 | (color.Green & 0xF8) << 8 | (color.Blue & 0xF8));

    private static char SixelChar(int value) =>
        (char)('?' + Math.Clamp(value, 0, 63));

    private static SKBitmap FitToWidth(SKBitmap source, int maxCellWidth, int maxBands)
    {
        var maxHeight = Math.Max(6, maxBands * 6);
        var targetWidth = Math.Min(maxCellWidth, source.Width);
        var scale = targetWidth / (double)source.Width;
        var targetHeight = Math.Max(6, (int)Math.Round(source.Height * scale));
        if (targetHeight > maxHeight)
        {
            scale = maxHeight / (double)source.Height;
            targetWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
            targetHeight = maxHeight;
        }

        if (source.Width == targetWidth && source.Height == targetHeight)
            return source.Copy() ?? throw new InvalidOperationException("Failed to copy bitmap.");

        var sampling = new SKSamplingOptions(SKCubicResampler.Mitchell);
        var resized = source.Resize(new SKImageInfo(targetWidth, targetHeight), sampling);
        return resized ?? throw new InvalidOperationException("Failed to resize bitmap.");
    }
}
