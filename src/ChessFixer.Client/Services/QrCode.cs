using System.Text;
using QRCoder;

namespace ChessFixer.Client.Services;

/// <summary>QR codes drawn as SVG (sharp at any size, no image library needed in the browser).</summary>
public static class QrCode
{
    /// <summary>The QR code of the text as an SVG element (black on white, with the usual quiet zone).</summary>
    public static string Svg(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var matrix = data.ModuleMatrix; // includes the quiet zone
        var size = matrix.Count;

        var path = new StringBuilder();
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (matrix[y][x]) path.Append('M').Append(x).Append(' ').Append(y).Append("h1v1h-1z");
            }
        }
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {size} {size}\" shape-rendering=\"crispEdges\" " +
               $"role=\"img\"><rect width=\"{size}\" height=\"{size}\" fill=\"#fff\"/><path d=\"{path}\" fill=\"#000\"/></svg>";
    }
}
