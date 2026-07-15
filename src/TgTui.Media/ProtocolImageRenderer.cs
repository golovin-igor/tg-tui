using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;

namespace TgTui.Media;

/// <summary>
/// Renders images using terminal graphics protocols (Kitty, iTerm2, Sixel).
/// Kitty and iTerm2 encode PNG payload; Sixel falls back to half-block until a full encoder ships.
/// </summary>
public static class ProtocolImageRenderer
{
    /// <summary>Kitty recommends chunks of at most 4096 bytes of base64 payload.</summary>
    private const int KittyChunkSize = 4096;

    public static string RenderFile(string path, int maxCellWidth, GraphicsCapability capability)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return HalfBlockImageRenderer.UnavailablePlaceholder;

        return capability switch
        {
            GraphicsCapability.Kitty => RenderKitty(path, maxCellWidth),
            GraphicsCapability.ITerm2 => RenderITerm2(path, maxCellWidth),
            GraphicsCapability.Sixel => RenderSixel(path, maxCellWidth),
            _ => HalfBlockImageRenderer.RenderFile(path, maxCellWidth)
        };
    }

    /// <summary>
    /// Kitty graphics protocol (APC): transmit and display PNG.
    /// Spec: https://sw.kovidgoyal.net/kitty/graphics-protocol/
    /// Sequence: ESC _ G keys=values ; base64 ESC \
    /// </summary>
    private static string RenderKitty(string path, int maxCellWidth)
    {
        try
        {
            var pngBytes = LoadAsPngBytes(path);
            var b64 = Convert.ToBase64String(pngBytes);
            var sb = new StringBuilder(b64.Length + 64);
            var columns = maxCellWidth > 0 ? maxCellWidth : 0;
            var offset = 0;
            var first = true;

            while (offset < b64.Length)
            {
                var length = Math.Min(KittyChunkSize, b64.Length - offset);
                var more = offset + length < b64.Length ? 1 : 0;

                if (first)
                {
                    sb.Append("\u001b_Ga=T,f=100");
                    if (columns > 0)
                    {
                        sb.Append(",c=");
                        sb.Append(columns);
                    }

                    sb.Append(",m=");
                    sb.Append(more);
                    sb.Append(';');
                    first = false;
                }
                else
                {
                    sb.Append("\u001b_Gm=");
                    sb.Append(more);
                    sb.Append(';');
                }

                sb.Append(b64, offset, length);
                sb.Append("\u001b\\");
                offset += length;
            }

            return sb.ToString();
        }
        catch
        {
            return HalfBlockImageRenderer.RenderFile(path, maxCellWidth);
        }
    }

    /// <summary>
    /// iTerm2 inline image protocol (OSC 1337).
    /// Spec: https://iterm2.com/documentation-images.html
    /// </summary>
    private static string RenderITerm2(string path, int maxCellWidth)
    {
        try
        {
            var pngBytes = LoadAsPngBytes(path);
            var b64 = Convert.ToBase64String(pngBytes);
            var sb = new StringBuilder(b64.Length + 96);
            sb.Append("\u001b]1337;File=inline=1;size=");
            sb.Append(pngBytes.Length);
            if (maxCellWidth > 0)
            {
                sb.Append(";width=");
                sb.Append(maxCellWidth);
            }

            sb.Append(':');
            sb.Append(b64);
            sb.Append('\u0007');
            return sb.ToString();
        }
        catch
        {
            return HalfBlockImageRenderer.RenderFile(path, maxCellWidth);
        }
    }

    /// <summary>
    /// TODO: implement a real Sixel encoder for terminals without Kitty/iTerm2.
    /// Until then, fall back to half-block so previews still render.
    /// </summary>
    private static string RenderSixel(string path, int maxCellWidth) =>
        HalfBlockImageRenderer.RenderFile(path, maxCellWidth);

    private static byte[] LoadAsPngBytes(string path)
    {
        using var image = Image.Load(path);
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }
}
