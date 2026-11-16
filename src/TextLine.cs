using System.Globalization;
using System.Text;

namespace SpotifyToast;

/// <summary>
/// Uma linha de texto que aceita emoji e outros caracteres fora da fonte principal.
/// O GDI+ sozinho falha nesses casos: não troca de fonte para caracteres fora do BMP (vira "□")
/// e, quando troca, a fonte reserva é mais alta e a linha inteira some por não caber no retângulo.
/// Aqui o texto é dividido em trechos (texto comum / emoji / símbolos), cada um na sua fonte,
/// alinhados pela linha de base e cortados com "…" na largura disponível.
/// Os emojis saem na cor do texto (o GDI+ não desenha emoji colorido).
/// </summary>
internal static class TextLine
{
    enum Kind { Text, Emoji, Symbol }

    static readonly StringFormat Measure = CreateFormat();

    static StringFormat CreateFormat()
    {
        var sf = (StringFormat)StringFormat.GenericTypographic.Clone();
        sf.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.NoClip | StringFormatFlags.MeasureTrailingSpaces;
        return sf;
    }

    public static void Draw(Graphics g, string? text, Font font, Brush brush, float x, float y, float maxWidth)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0) return;
        using var emoji = new Font("Segoe UI Emoji", font.Size, FontStyle.Regular, font.Unit);
        using var symbol = new Font("Segoe UI Symbol", font.Size, FontStyle.Regular, font.Unit);
        Font FontOf(Kind k) => k switch { Kind.Emoji => emoji, Kind.Symbol => symbol, _ => font };

        var elems = Elements(text);
        var runs = Runs(elems);
        if (Width(g, runs, FontOf) > maxWidth)
        {
            // corta no fim (por grafema, para não partir um emoji) até caber com as reticências
            int n = elems.Count;
            do
            {
                n--;
                while (n > 0 && elems[n - 1].s.Trim().Length == 0) n--;
                runs = Runs(elems.Take(n).Append(("…", Kind.Text)).ToList());
            } while (n > 0 && Width(g, runs, FontOf) > maxWidth);
        }

        float baseline = y + Ascent(font);
        foreach (var (s, k) in runs)
        {
            var f = FontOf(k);
            g.DrawString(s, f, brush, x, baseline - Ascent(f), Measure);
            x += g.MeasureString(s, f, PointF.Empty, Measure).Width;
        }
    }

    static float Width(Graphics g, List<(string s, Kind k)> runs, Func<Kind, Font> fontOf) =>
        runs.Sum(r => g.MeasureString(r.s, fontOf(r.k), PointF.Empty, Measure).Width);

    static float Ascent(Font f) =>
        f.Size * f.FontFamily.GetCellAscent(f.Style) / f.FontFamily.GetEmHeight(f.Style);

    /// <summary>Grafemas do texto, cada um com a fonte que consegue desenhá-lo.</summary>
    static List<(string s, Kind k)> Elements(string text)
    {
        var list = new List<(string, Kind)>();
        var e = StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext())
        {
            string el = e.GetTextElement();
            list.Add((el, KindOf(el)));
        }
        return list;
    }

    static List<(string s, Kind k)> Runs(List<(string s, Kind k)> elems)
    {
        var runs = new List<(string, Kind)>();
        var sb = new StringBuilder();
        Kind cur = Kind.Text;
        foreach (var (s, k) in elems)
        {
            if (sb.Length > 0 && k != cur) { runs.Add((sb.ToString(), cur)); sb.Clear(); }
            cur = k;
            sb.Append(s);
        }
        if (sb.Length > 0) runs.Add((sb.ToString(), cur));
        return runs;
    }

    static Kind KindOf(string el)
    {
        bool astral = false;
        foreach (var r in el.EnumerateRunes())
        {
            int v = r.Value;
            // seletor de emoji, ZWJ e "keycap" indicam sequência de emoji
            if (v is 0xFE0F or 0x200D or 0x20E3) return Kind.Emoji;
            if (v is >= 0x1F000 and <= 0x1FAFF) return Kind.Emoji;
            if (v is >= 0x2600 and <= 0x27BF or >= 0x2B00 and <= 0x2BFF or >= 0x2300 and <= 0x23FF
                  or 0x3030 or 0x303D or 0x3297 or 0x3299) return Kind.Emoji;
            if (v >= 0x10000) astral = true;
        }
        // ex.: letras "estilizadas" (𝐁𝐨𝐥𝐝) usadas em nomes de música
        return astral ? Kind.Symbol : Kind.Text;
    }
}
