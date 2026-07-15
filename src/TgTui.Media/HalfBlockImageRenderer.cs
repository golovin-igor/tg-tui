using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

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
            using var image = Image.Load<Rgba32>(path);
            return Render(image, maxCellWidth);
        }
        catch
        {
            return UnavailablePlaceholder;
        }
    }

    private static string Render(Image<Rgba32> image, int maxCellWidth)
    {
        if (maxCellWidth < 1)
            maxCellWidth = 1;

        if (image.Width > maxCellWidth)
        {
            var scale = maxCellWidth / (double)image.Width;
            var newHeight = Math.Max(1, (int)Math.Round(image.Height * scale));
            image.Mutate(x => x.Resize(maxCellWidth, newHeight));
        }

        var width = image.Width;
        var height = image.Height;
        var sb = new StringBuilder(width * ((height + 1) / 2) * 32);

        for (var y = 0; y < height; y += 2)
        {
            if (y > 0)
                sb.Append('\n');

            for (var x = 0; x < width; x++)
            {
                var top = image[x, y];
                var bottom = y + 1 < height ? image[x, y + 1] : top;

                // ▄ = lower half: foreground = bottom pixel, background = top pixel
                AppendTrueColorCell(sb, bottom, top);
                sb.Append('▄');
                sb.Append("\u001b[0m");
            }
        }

        return sb.ToString();
    }

    private static void AppendTrueColorCell(StringBuilder sb, Rgba32 foreground, Rgba32 background)
    {
        sb.Append("\u001b[38;2;");
        sb.Append(foreground.R);
        sb.Append(';');
        sb.Append(foreground.G);
        sb.Append(';');
        sb.Append(foreground.B);
        sb.Append('m');

        sb.Append("\u001b[48;2;");
        sb.Append(background.R);
        sb.Append(';');
        sb.Append(background.G);
        sb.Append(';');
        sb.Append(background.B);
        sb.Append('m');
    }
}
