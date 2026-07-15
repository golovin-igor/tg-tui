using System.Text;
using SkiaSharp;

namespace TgTui.Media;

public static class HalfBlockImageRenderer
{
    public const string UnavailablePlaceholder = "🖼 image unavailable";

    public static string RenderFile(string path, int maxCellWidth)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return UnavailablePlaceholder;

        try
        {
            using var bitmap = SKBitmap.Decode(path);
            if (bitmap is null)
                return UnavailablePlaceholder;

            return Render(bitmap, maxCellWidth);
        }
        catch
        {
            return UnavailablePlaceholder;
        }
    }

    private static string Render(SKBitmap source, int maxCellWidth)
    {
        if (maxCellWidth < 1)
            maxCellWidth = 1;

        using var bitmap = FitToWidth(source, maxCellWidth);
        var width = bitmap.Width;
        var height = bitmap.Height;
        var sb = new StringBuilder(width * ((height + 1) / 2) * 32);

        for (var y = 0; y < height; y += 2)
        {
            if (y > 0)
                sb.Append('\n');

            for (var x = 0; x < width; x++)
            {
                var top = bitmap.GetPixel(x, y);
                var bottom = y + 1 < height ? bitmap.GetPixel(x, y + 1) : top;

                // ▄ = lower half: foreground = bottom pixel, background = top pixel
                AppendTrueColorCell(sb, bottom, top);
                sb.Append('▄');
                sb.Append("\u001b[0m");
            }
        }

        return sb.ToString();
    }

    private static SKBitmap FitToWidth(SKBitmap source, int maxCellWidth)
    {
        if (source.Width <= maxCellWidth)
            return source.Copy() ?? throw new InvalidOperationException("Failed to copy bitmap.");

        var scale = maxCellWidth / (double)source.Width;
        var newHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
        var sampling = new SKSamplingOptions(SKCubicResampler.Mitchell);
        var resized = source.Resize(new SKImageInfo(maxCellWidth, newHeight), sampling);
        return resized ?? throw new InvalidOperationException("Failed to resize bitmap.");
    }

    private static void AppendTrueColorCell(StringBuilder sb, SKColor foreground, SKColor background)
    {
        sb.Append("\u001b[38;2;");
        sb.Append(foreground.Red);
        sb.Append(';');
        sb.Append(foreground.Green);
        sb.Append(';');
        sb.Append(foreground.Blue);
        sb.Append('m');

        sb.Append("\u001b[48;2;");
        sb.Append(background.Red);
        sb.Append(';');
        sb.Append(background.Green);
        sb.Append(';');
        sb.Append(background.Blue);
        sb.Append('m');
    }
}
