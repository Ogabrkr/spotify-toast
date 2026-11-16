using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace SpotifyToast;

/// <summary>
/// Popup estilo notificação de console: cartão arredondado que desliza da borda da tela.
/// O vidro é o desfoque nativo do Windows (DWM) — o app nunca lê a tela.
/// O conteúdo é desenhado uma única vez; durante a animação a janela só muda de posição.
/// </summary>
internal sealed class ToastWindow : Form
{
    enum Phase { Idle, In, Hold, Out }

    const double InSeconds = 0.45, OutSeconds = 0.32;

    readonly System.Windows.Forms.Timer timer = new() { Interval = 15 };
    readonly Stopwatch clock = Stopwatch.StartNew();
    Phase phase = Phase.Idle;
    double p, lastTick, holdUntil, holdSeconds = 5;

    // geometria em pixels físicos: dpi = escala do monitor; s = dpi × tamanho escolhido
    float dpi = 1, s = 1;
    int cardW, cardH;
    Point shown, hidden;

    // conteúdo pronto (ARGB pré-multiplicado) copiado na janela no WM_PAINT
    IntPtr memDc, dib, oldBmp;
    Bitmap? surface;

    // último conteúdo, para redesenhar quando o mouse entra/sai (o vidro é "interactive")
    TrackInfo? track;
    bool glassOn, darkOn, hovered;
    string nowPlaying = "";

    public bool IsShowing => phase != Phase.Idle;

    public ToastWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        TopMost = true;
        Text = "Spotify Toast";
        Cursor = Cursors.Hand;
        DoubleBuffered = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
        timer.Tick += (_, _) => Tick();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style = unchecked((int)0x80000000); // WS_POPUP
            cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    int R(float v) => (int)Math.Round(v * s);

    // raio do arredondamento do Windows 11 (DWMWCP_ROUND): o desfoque nativo só respeita esse recorte
    const float GlassRadius = 8;

    public void ShowTrack(TrackInfo t, AppSettings st)
    {
        bool wasIdle = phase == Phase.Idle;
        bool dark = st.IsDark;
        holdSeconds = Math.Clamp(st.DurationSeconds, 2, 30);

        if (wasIdle) { ComputeGeometry(st); hovered = false; }

        // cópia própria: o TrayApp libera a faixa anterior quando chega outra
        var mine = new TrackInfo(t.Title, t.Artist, t.Album, t.Cover != null ? new Bitmap(t.Cover) : null);
        track?.Dispose();
        track = mine; glassOn = st.Glass; darkOn = dark; nowPlaying = st.Text.NowPlaying;

        Native.SetBackdrop(Handle, st.Glass, dark);
        if (!RenderContent(mine, st.Glass, dark))
        {
            if (!wasIdle) Finish();
            return; // sem recursos gráficos: melhor não mostrar do que mostrar um cartão vazio
        }

        if (wasIdle)
        {
            p = 0;
            Bounds = new Rectangle(hidden, new Size(cardW, cardH));
            Visible = true; // ShowWithoutActivation => não rouba o foco
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, hidden.X, hidden.Y, cardW, cardH, Native.SWP_NOACTIVATE);
        }
        Invalidate();
        phase = Phase.In;
        timer.Interval = 15;
        lastTick = clock.Elapsed.TotalSeconds;
        timer.Start();
    }

    public void HideNow()
    {
        if (phase != Phase.Idle) Finish();
    }

    void ComputeGeometry(AppSettings st)
    {
        var scr = Screen.PrimaryScreen ?? Screen.AllScreens[0];
        var mon = scr.Bounds;
        var wa = scr.WorkingArea;
        dpi = Native.ScaleFor(mon);
        s = dpi * st.SizeFactor;

        cardW = R(372); cardH = R(88);
        int margin = (int)Math.Round(18 * dpi);
        bool right = st.Position == "right";
        shown = new Point(right ? wa.Right - margin - cardW : wa.Left + margin, wa.Top + margin);

        // desliza pela lateral; se houver outro monitor desse lado, desce pelo topo
        // se os dois tiverem vizinho, aparece direto no lugar (sem invadir o outro monitor)
        var size = new Size(cardW, cardH);
        bool Blocked(Point from) => Screen.AllScreens.Any(o => !o.Bounds.Equals(mon) &&
            o.Bounds.IntersectsWith(Rectangle.Union(new Rectangle(from, size), new Rectangle(shown, size))));
        var side = new Point(right ? mon.Right + R(4) : mon.Left - cardW - R(4), shown.Y);
        var top = new Point(shown.X, mon.Top - cardH - R(4));
        hidden = !Blocked(side) ? side : !Blocked(top) ? top : shown;
    }

    bool RenderContent(TrackInfo t, bool glass, bool dark)
    {
        if (!EnsureSurface()) return false;
        using var g = Graphics.FromImage(surface!);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;

        var card = new RectangleF(0, 0, cardW, cardH);

        if (glass) DrawGlass(g, card, dark, Fx.Dominant(t.Cover));
        else
        {
            using var fill = new SolidBrush(dark ? Color.FromArgb(255, 30, 31, 35) : Color.FromArgb(255, 250, 250, 252));
            g.FillRectangle(fill, card);
        }

        DrawContent(g, t, card, dark);
        g.Flush(FlushIntention.Sync);
        return true;
    }

    /// <summary>
    /// Material Liquid Glass por cima do desfoque do Windows:
    /// tinta leve (vidro límpido, não fosco) + reflexo da cor da capa + brilho especular no topo
    /// + borda de lente (luz que entra pela espessura do vidro) + sombra interna embaixo
    /// + aro com reflexos em cantos opostos. Com o mouse em cima, o vidro acende (interactive).
    /// </summary>
    void DrawGlass(Graphics g, RectangleF card, bool dark, Color? reflect)
    {
        float radius = GlassRadius * dpi; // o canto do Windows não muda com o tamanho do cartão
        float hair = Math.Max(1f, s);
        var grad = RectangleF.Inflate(card, 1, 1);
        using var body = Fx.RoundedRect(card, radius);

        // 1. tinta base: clara o bastante para ver o fundo, densa o bastante para ler
        using (var tint = new SolidBrush(dark ? Color.FromArgb(hovered ? 72 : 86, 14, 14, 20)
                                              : Color.FromArgb(hovered ? 120 : 104, 255, 255, 255)))
            g.FillPath(tint, body);

        // 2. reflexo da cor do conteúdo (capa): mais forte perto da capa, some para a direita
        if (reflect is Color c)
        {
            using var refl = new LinearGradientBrush(grad, Color.Black, Color.Black, LinearGradientMode.Horizontal);
            refl.InterpolationColors = new ColorBlend
            {
                Colors = new[] { Color.FromArgb(dark ? 78 : 60, c), Color.FromArgb(dark ? 30 : 22, c), Color.FromArgb(dark ? 14 : 10, c) },
                Positions = new[] { 0f, 0.45f, 1f },
            };
            g.FillPath(refl, body);
        }

        // 3. brilho especular: luz vinda de cima, só na metade superior
        using (var sheen = new LinearGradientBrush(grad, Color.White, Color.White, LinearGradientMode.Vertical))
        {
            sheen.InterpolationColors = new ColorBlend
            {
                Colors = new[]
                {
                    Color.FromArgb((dark ? 30 : 46) + (hovered ? 14 : 0), 255, 255, 255),
                    Color.FromArgb(0, 255, 255, 255),
                    Color.FromArgb(0, 255, 255, 255),
                },
                Positions = new[] { 0f, 0.48f, 1f },
            };
            g.FillPath(sheen, body);
        }

        var state = g.Save();
        g.SetClip(body);

        // 4. borda de lente: faixa clara por dentro do contorno, mais intensa em cima e embaixo
        float lens = R(5);
        using (var lensPath = Fx.RoundedRect(RectangleF.Inflate(card, -lens / 2, -lens / 2), radius - lens / 2))
        using (var lensBrush = new LinearGradientBrush(grad, Color.White, Color.White, LinearGradientMode.Vertical))
        {
            lensBrush.InterpolationColors = new ColorBlend
            {
                Colors = new[]
                {
                    Color.FromArgb(dark ? 46 : 70, 255, 255, 255),
                    Color.FromArgb(dark ? 8 : 14, 255, 255, 255),
                    Color.FromArgb(dark ? 8 : 14, 255, 255, 255),
                    Color.FromArgb(dark ? 30 : 50, 255, 255, 255),
                },
                Positions = new[] { 0f, 0.3f, 0.7f, 1f },
            };
            using var pen = new Pen(lensBrush, lens);
            g.DrawPath(pen, lensPath);
        }

        // 5. sombra interna embaixo: dá espessura ao vidro
        using (var shadePath = Fx.RoundedRect(RectangleF.Inflate(card, -hair * 1.5f, -hair * 1.5f), radius - hair * 1.5f))
        using (var shadeBrush = new LinearGradientBrush(grad, Color.Black, Color.Black, LinearGradientMode.Vertical))
        {
            shadeBrush.InterpolationColors = new ColorBlend
            {
                Colors = new[] { Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(dark ? 60 : 26, 0, 0, 0) },
                Positions = new[] { 0f, 0.6f, 1f },
            };
            using var pen = new Pen(shadeBrush, hair * 1.5f);
            g.DrawPath(pen, shadePath);
        }
        g.Restore(state);

        // 6. aro: reflexo forte no canto superior esquerdo e no inferior direito (vidro curvo)
        using var rimPath = Fx.RoundedRect(RectangleF.Inflate(card, -hair / 2, -hair / 2), radius - hair / 2);
        using var rimBrush = new LinearGradientBrush(grad, Color.White, Color.White, 35f);
        rimBrush.InterpolationColors = new ColorBlend
        {
            Colors = new[]
            {
                Color.FromArgb(dark ? 210 : 255, 255, 255, 255),
                Color.FromArgb(dark ? 40 : 90, 255, 255, 255),
                Color.FromArgb(dark ? 22 : 60, 255, 255, 255),
                Color.FromArgb(dark ? 150 : 220, 255, 255, 255),
            },
            Positions = new[] { 0f, 0.3f, 0.65f, 1f },
        };
        using var rimPen = new Pen(rimBrush, hair * 1.2f);
        g.DrawPath(rimPen, rimPath);
    }

    void DrawContent(Graphics g, TrackInfo t, RectangleF card, bool dark)
    {
        int inset = R(11);
        int cs = (int)card.Height - inset * 2;
        var coverRect = new Rectangle((int)card.X + inset, (int)card.Y + inset, cs, cs);

        using (var coverPath = Fx.RoundedRect(coverRect, R(6)))
        {
            if (t.Cover != null)
            {
                using var sq = Fx.SquareCover(t.Cover, cs);
                using var tb = new TextureBrush(sq, WrapMode.Clamp);
                tb.TranslateTransform(coverRect.X, coverRect.Y);
                g.FillPath(tb, coverPath);
            }
            else
            {
                using var bg = new LinearGradientBrush(RectangleF.Inflate(coverRect, 1, 1),
                    Color.FromArgb(30, 215, 96), Color.FromArgb(10, 95, 80), 45f);
                g.FillPath(bg, coverPath);
                using var white = new SolidBrush(Color.FromArgb(235, 255, 255, 255));
                float n = cs * 0.5f;
                Fx.FillNote(g, white, new RectangleF(coverRect.X + (cs - n) / 2, coverRect.Y + (cs - n) / 2, n, n));
            }
            using var hl = new Pen(dark ? Color.FromArgb(45, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0), Math.Max(1f, s));
            g.DrawPath(hl, coverPath);
        }

        float tx = coverRect.Right + R(14);
        float tw = card.Right - R(16) - tx;

        using var labelFont = new Font("Segoe UI Semibold", 10.5f * s, FontStyle.Regular, GraphicsUnit.Pixel);
        using var titleFont = new Font("Segoe UI Semibold", 15.5f * s, FontStyle.Regular, GraphicsUnit.Pixel);
        using var artistFont = new Font("Segoe UI", 13f * s, FontStyle.Regular, GraphicsUnit.Pixel);
        float lh = labelFont.GetHeight(g), th = titleFont.GetHeight(g), ah = artistFont.GetHeight(g);
        float gap = R(2);
        float y = card.Y + (card.Height - (lh + gap + th + ah)) / 2;

        using var sf = (StringFormat)StringFormat.GenericTypographic.Clone();
        sf.FormatFlags = (sf.FormatFlags | StringFormatFlags.NoWrap) & ~StringFormatFlags.NoClip;
        sf.Trimming = StringTrimming.EllipsisCharacter;

        var primary = dark ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 20, 20, 22);
        var secondary = dark ? Color.FromArgb(200, 255, 255, 255) : Color.FromArgb(175, 0, 0, 0);
        var tertiary = dark ? Color.FromArgb(160, 255, 255, 255) : Color.FromArgb(140, 0, 0, 0);

        float dot = R(7);
        using (var green = new SolidBrush(Color.FromArgb(30, 215, 96)))
            g.FillEllipse(green, tx, y + (lh - dot) / 2, dot, dot);
        using (var b = new SolidBrush(tertiary))
            g.DrawString(nowPlaying, labelFont, b, new RectangleF(tx + dot + R(6), y, tw - dot - R(6), lh), sf);
        y += lh + gap;
        if (dark)
        {
            // contraste sobre qualquer fundo: texto no vidro precisa continuar legível
            using var shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0));
            TextLine.Draw(g, t.Title, titleFont, shadow, tx, y + R(1), tw);
        }
        using (var b = new SolidBrush(primary))
            TextLine.Draw(g, t.Title, titleFont, b, tx, y, tw);
        y += th;
        string sub = string.IsNullOrWhiteSpace(t.Artist) ? t.Album : t.Artist;
        using (var b = new SolidBrush(secondary))
            TextLine.Draw(g, sub, artistFont, b, tx, y, tw);
    }

    bool EnsureSurface()
    {
        if (surface != null && surface.Width == cardW && surface.Height == cardH) return true;
        FreeSurface();
        var bmi = new Native.BITMAPINFOHEADER
        {
            biSize = 40, biWidth = cardW, biHeight = -cardH, biPlanes = 1, biBitCount = 32, biCompression = 0,
        };
        memDc = Native.CreateCompatibleDC(IntPtr.Zero);
        if (memDc == IntPtr.Zero) return false;
        dib = Native.CreateDIBSection(memDc, ref bmi, 0, out var bits, IntPtr.Zero, 0);
        if (dib == IntPtr.Zero || bits == IntPtr.Zero)
        {
            if (dib != IntPtr.Zero) Native.DeleteObject(dib);
            Native.DeleteDC(memDc);
            memDc = dib = IntPtr.Zero;
            return false;
        }
        oldBmp = Native.SelectObject(memDc, dib);
        surface = new Bitmap(cardW, cardH, cardW * 4, PixelFormat.Format32bppPArgb, bits);
        return true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (memDc == IntPtr.Zero) return;
        var hdc = e.Graphics.GetHdc();
        Native.BitBlt(hdc, 0, 0, cardW, cardH, memDc, 0, 0, Native.SRCCOPY); // copia com o canal alfa
        e.Graphics.ReleaseHdc(hdc);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    static double Ease(double t) => 1 - Math.Pow(1 - t, 4);

    void Slide()
    {
        double e = Ease(p);
        int x = (int)Math.Round(hidden.X + (shown.X - hidden.X) * e);
        int y = (int)Math.Round(hidden.Y + (shown.Y - hidden.Y) * e);
        Native.SetWindowPos(Handle, IntPtr.Zero, x, y, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOZORDER);
        Update(); // pinta já a parte que acabou de entrar na tela
    }

    void Tick()
    {
        double now = clock.Elapsed.TotalSeconds;
        double dt = Math.Min(0.05, now - lastTick);
        lastTick = now;

        switch (phase)
        {
            case Phase.In:
                p = Math.Min(1, p + dt / InSeconds);
                Slide();
                if (p >= 1)
                {
                    phase = Phase.Hold;
                    holdUntil = now + holdSeconds;
                    timer.Interval = 120; // parado na tela: só confere o mouse
                }
                break;
            case Phase.Hold:
                if (new Rectangle(shown, new Size(cardW, cardH)).Contains(Cursor.Position))
                    holdUntil = Math.Max(holdUntil, now + 1.2);
                if (now >= holdUntil) BeginOut();
                break;
            case Phase.Out:
                p = Math.Max(0, p - dt / OutSeconds);
                Slide();
                if (p <= 0) Finish();
                break;
        }
    }

    void BeginOut()
    {
        phase = Phase.Out;
        timer.Interval = 15;
        lastTick = clock.Elapsed.TotalSeconds;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        SetHover(true);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        SetHover(false);
    }

    void SetHover(bool on)
    {
        if (hovered == on || track == null || phase == Phase.Idle) return;
        hovered = on;
        RenderContent(track, glassOn, darkOn);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (phase is Phase.In or Phase.Hold) BeginOut();
    }

    void Finish()
    {
        timer.Stop();
        phase = Phase.Idle;
        p = 0;
        Visible = false;
        FreeSurface();
        track?.Dispose();
        track = null;
    }

    void FreeSurface()
    {
        surface?.Dispose(); surface = null;
        if (memDc != IntPtr.Zero)
        {
            Native.SelectObject(memDc, oldBmp);
            Native.DeleteObject(dib);
            Native.DeleteDC(memDc);
            memDc = dib = oldBmp = IntPtr.Zero;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            FreeSurface();
            track?.Dispose();
        }
        base.Dispose(disposing);
    }
}
