using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using DrawingIcon = System.Drawing.Icon;

internal static class LucideIconRenderer
{
    private static readonly int[] Sizes = [24, 30, 32, 36, 48, 64];
    private static readonly string[] Names =
        ["battery", "battery-low", "battery-medium", "battery-full", "battery-charging"];

    internal static void Export(string directory)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Assets", "Lucide");
        foreach (var name in Names)
        {
            var svg = XDocument.Load(Path.Combine(source, $"{name}.svg"));
            var frames = Sizes.Select(size => (size, EncodePng(Render(svg, size, name == "battery-charging")))).ToArray();
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)frames.Length);
                var offset = 6 + frames.Length * 16;
                foreach (var (size, png) in frames)
                {
                    writer.Write((byte)size);
                    writer.Write((byte)size);
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)32);
                    writer.Write((uint)png.Length);
                    writer.Write((uint)offset);
                    offset += png.Length;
                }
                foreach (var (_, png) in frames)
                    writer.Write(png);
            }
            stream.Position = 0;
            using var icon = new DrawingIcon(stream, 30, 30);
            using var output = File.Create(Path.Combine(directory, $"lucide-{name}.ico"));
            icon.Save(output);
        }
    }

    private static BitmapSource Render(XDocument svg, int size, bool charging)
    {
        var root = svg.Root ?? throw new InvalidDataException("SVG root is missing.");
        if ((string?)root.Attribute("viewBox") != "0 0 24 24")
            throw new InvalidDataException("Unexpected Lucide SVG viewBox.");
        var brush = new SolidColorBrush(charging
            ? System.Windows.Media.Color.FromRgb(46, 152, 185)
            : System.Windows.Media.Color.FromRgb(79, 108, 110));
        brush.Freeze();
        var pen = new System.Windows.Media.Pen(brush, 2)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new ScaleTransform(size / 24d, size / 24d));
            foreach (var element in root.Elements())
            {
                switch (element.Name.LocalName)
                {
                    case "path":
                        context.DrawGeometry(null, pen, Geometry.Parse((string?)element.Attribute("d") ?? ""));
                        break;
                    case "rect":
                        double Read(string key) => double.Parse((string?)element.Attribute(key) ?? "0", CultureInfo.InvariantCulture);
                        var radius = Read("rx");
                        context.DrawRoundedRectangle(null, pen,
                            new System.Windows.Rect(Read("x"), Read("y"), Read("width"), Read("height")),
                            radius, radius);
                        break;
                    default:
                        throw new InvalidDataException($"Unsupported Lucide SVG element: {element.Name.LocalName}.");
                }
            }
            context.Pop();
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
