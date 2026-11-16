using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SpotifyToast;

/// <summary>Desenho: cantos arredondados, nota musical, recorte e cor predominante da capa.</summary>
internal static class Fx
{
    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Max(0.5f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>Nota musical desenhada num espaço de 16x16 unidades.</summary>
    public static void FillNote(Graphics g, Brush brush, RectangleF box)
    {
        var st = g.Save();
        g.TranslateTransform(box.X, box.Y);
        g.ScaleTransform(box.Width / 16f, box.Height / 16f);
        using var path = new GraphicsPath(FillMode.Winding);
        path.AddEllipse(1.4f, 10.2f, 5.0f, 4.0f);
        path.AddEllipse(8.9f, 8.7f, 5.0f, 4.0f);
        path.AddRectangle(new RectangleF(5.2f, 4.0f, 1.3f, 8.2f));
        path.AddRectangle(new RectangleF(12.6f, 2.5f, 1.3f, 8.2f));
        path.AddPolygon(new[] { new PointF(5.2f, 3.6f), new PointF(13.9f, 1.6f), new PointF(13.9f, 4.1f), new PointF(5.2f, 6.1f) });
        g.FillPath(brush, path);
        g.Restore(st);
    }


    /// <summary>Recorte quadrado central da capa, já no tamanho final.</summary>
    public static Bitmap SquareCover(Image src, int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        int side = Math.Min(src.Width, src.Height);
        var srcRect = new Rectangle((src.Width - side) / 2, (src.Height - side) / 2, side, side);
        using var ia = new ImageAttributes();
        ia.SetWrapMode(WrapMode.TileFlipXY);
        g.DrawImage(src, new Rectangle(0, 0, size, size), srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height, GraphicsUnit.Pixel, ia);
        return bmp;
    }

    /// <summary>Cor predominante da capa, saturada e com luminosidade controlada (o "reflexo").</summary>
    public static Color? Dominant(Image? img)
    {
        if (img == null) return null;
        const int n = 8;
        using var small = new Bitmap(n, n, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(img, new Rectangle(0, 0, n, n));
        }
        double r = 0, gr = 0, b = 0, wsum = 0;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var c = small.GetPixel(x, y);
                // pixels mais saturados pesam mais: a cor "da capa", não o cinza do fundo
                double w = 0.15 + c.GetSaturation() * (1 - Math.Abs(c.GetBrightness() - 0.5) * 1.6);
                if (w < 0.05) w = 0.05;
                r += c.R * w; gr += c.G * w; b += c.B * w; wsum += w;
            }
        var avg = Color.FromArgb((int)(r / wsum), (int)(gr / wsum), (int)(b / wsum));
        float hue = avg.GetHue();
        float sat = Math.Min(1f, avg.GetSaturation() * 1.5f + 0.1f);
        float lum = Math.Clamp(avg.GetBrightness(), 0.42f, 0.6f);
        return FromHsl(hue, sat, lum);
    }

    static Color FromHsl(float h, float s, float l)
    {
        float c = (1 - Math.Abs(2 * l - 1)) * s;
        float hp = h / 60f;
        float x = c * (1 - Math.Abs(hp % 2 - 1));
        (float r, float g, float b) = hp switch
        {
            < 1 => (c, x, 0f), < 2 => (x, c, 0f), < 3 => (0f, c, x),
            < 4 => (0f, x, c), < 5 => (x, 0f, c), _ => (c, 0f, x),
        };
        float m = l - c / 2;
        return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }

    /// <summary>Capa abstrata desenhada pelo app (exemplo sem direitos autorais de terceiros).</summary>
    public static Bitmap SampleCover(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new LinearGradientBrush(new Rectangle(0, 0, size, size), Color.FromArgb(255, 46, 18, 92), Color.FromArgb(255, 255, 70, 140), 60f))
            g.FillRectangle(bg, 0, 0, size, size);
        // sol
        using (var sun = new LinearGradientBrush(new RectangleF(size * 0.25f, size * 0.2f, size * 0.5f, size * 0.5f),
                   Color.FromArgb(255, 255, 214, 90), Color.FromArgb(255, 255, 90, 120), 90f))
            g.FillEllipse(sun, size * 0.25f, size * 0.2f, size * 0.5f, size * 0.5f);
        // listras do sol
        using (var stripe = new SolidBrush(Color.FromArgb(255, 120, 30, 120)))
            for (int i = 0; i < 4; i++)
                g.FillRectangle(stripe, 0, size * (0.48f + i * 0.055f), size, size * (0.012f + i * 0.006f));
        // montanhas
        using (var hill = new SolidBrush(Color.FromArgb(255, 24, 10, 48)))
            g.FillPolygon(hill, new[] { new PointF(0, size), new PointF(0, size * 0.72f), new PointF(size * 0.3f, size * 0.58f),
                new PointF(size * 0.55f, size * 0.7f), new PointF(size * 0.8f, size * 0.55f), new PointF(size, size * 0.68f), new PointF(size, size) });
        return bmp;
    }
}
