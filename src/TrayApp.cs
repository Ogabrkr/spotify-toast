using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SpotifyToast;

internal sealed class TrayApp : ApplicationContext
{
    readonly AppSettings settings;
    readonly NotifyIcon tray;
    readonly ContextMenuStrip menu;
    readonly ToastWindow toast;
    readonly MediaWatcher watcher;
    TrackInfo? last;
    Icon? trayIcon;
    bool? iconForLightTaskbar;
    bool? menuDark;

    readonly ToolStripMenuItem glassItem, solidItem, darkItem, lightItem, systemItem, leftItem, rightItem, fullscreenItem, startupItem;
    readonly (ToolStripMenuItem item, int secs)[] durationItems;

    public TrayApp(string[] args)
    {
        settings = AppSettings.Load(out bool firstRun);
        if (firstRun) settings.Save(); // "Iniciar com o Windows" fica a cargo do instalador

        toast = new ToastWindow();
        _ = toast.Handle; // cria a janela já (fica invisível)

        menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true, Padding = new Padding(0, 4, 0, 4) };
        var header = new ToolStripMenuItem("Spotify Toast") { Enabled = false, Font = new Font("Segoe UI Semibold", 9f) };

        glassItem = Choice("Liquid Glass", () => settings.Glass = true);
        solidItem = Choice("Sólido", () => settings.Glass = false);
        var style = Sub("Estilo", glassItem, solidItem);

        darkItem = Choice("Escuro", () => settings.Theme = "dark");
        lightItem = Choice("Claro", () => settings.Theme = "light");
        systemItem = Choice("Seguir o Windows", () => settings.Theme = "system");
        var theme = Sub("Tema", darkItem, lightItem, systemItem);

        leftItem = Choice("Canto superior esquerdo", () => settings.Position = "left");
        rightItem = Choice("Canto superior direito", () => settings.Position = "right");
        var pos = Sub("Posição", leftItem, rightItem);

        durationItems = new[] { 3, 5, 8, 12 }
            .Select(n => (Choice($"{n} segundos", () => settings.DurationSeconds = n, preview: false), n)).ToArray();
        var dur = Sub("Duração", durationItems.Select(d => d.item).ToArray());

        fullscreenItem = new ToolStripMenuItem("Aparecer sobre tela cheia", null, (_, _) =>
        {
            settings.ShowInFullscreen = !settings.ShowInFullscreen;
            settings.Save();
        });
        startupItem = new ToolStripMenuItem("Iniciar com o Windows", null, (_, _) => Startup.Set(!Startup.IsEnabled));

        menu.Items.AddRange(new ToolStripItem[]
        {
            header, new ToolStripSeparator(),
            style, theme, pos, dur, new ToolStripSeparator(),
            fullscreenItem, startupItem, new ToolStripSeparator(),
            new ToolStripMenuItem("Mostrar notificação", null, (_, _) => Preview()),
            new ToolStripMenuItem("Fechar o app", null, (_, _) => Quit()),
        });
        menu.Opening += (_, _) => SyncMenu();
        HookRoundCorners(menu);

        tray = new NotifyIcon { Text = "Spotify Toast", ContextMenuStrip = menu };
        UpdateTrayIcon();
        tray.Visible = true;
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Preview(); };

        watcher = new MediaWatcher(SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext());
        watcher.TrackChanged += OnTrack;
        watcher.TrackKnown += t =>
        {
            if (last != null) { t.Dispose(); return; }
            last = t;
            UpdateTooltip(t);
        };
        _ = StartWatcher();

        // --demo: mostra a música atual | --sample: música de exemplo (para prints do README)
        // --show-menu: abre o menu do tray num ponto fixo (para prints do README)
        if (args.Contains("--demo")) Delay(1500, Preview);
        if (args.Contains("--sample"))
            Delay(1200, () =>
            {
                using var sample = new TrackInfo("Neon Coastline", "The Midnight Arcade", "", Fx.SampleCover(256));
                toast.ShowTrack(sample, settings);
            });
        if (args.Contains("--show-menu"))
            Delay(1200, () => { SyncMenu(); menu.Show(new Point(40, 40)); });
        // ícone do tray acompanha o tema da barra de tarefas
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }


    async Task StartWatcher()
    {
        try { await watcher.StartAsync(); }
        catch (Exception ex)
        {
            tray.ShowBalloonTip(5000, "Spotify Toast", "Não consegui acessar o controle de mídia do Windows: " + ex.Message, ToolTipIcon.Warning);
        }
    }

    void OnTrack(TrackInfo t)
    {
        var old = last;
        last = t;
        UpdateTooltip(t);
        if (settings.ShowInFullscreen || !Native.IsFullscreenActive())
            toast.ShowTrack(t, settings);
        old?.Dispose();
    }

    void UpdateTooltip(TrackInfo t)
    {
        string tip = $"{t.Title} — {t.Artist}";
        tray.Text = tip.Length > 63 ? tip[..62] + "…" : tip;
    }

    void Preview()
    {
        // ao trocar estilo/posição com o popup na tela, esconde e mostra de novo no lugar novo
        if (toast.IsShowing)
        {
            toast.HideNow();
            Delay(160, ShowCurrent);
        }
        else ShowCurrent();
    }

    void ShowCurrent()
    {
        // o popup copia a faixa, então a de exemplo pode ser liberada logo
        if (last != null) toast.ShowTrack(last, settings);
        else
        {
            using var sample = new TrackInfo("Nenhuma música ainda", "Dê play no Spotify", "", null);
            toast.ShowTrack(sample, settings);
        }
    }

    ToolStripMenuItem Choice(string text, Action apply, bool preview = true) => new(text, null, (_, _) =>
    {
        apply();
        settings.Save();
        UpdateTrayIcon();
        if (preview) Preview();
    });

    static ToolStripMenuItem Sub(string text, params ToolStripMenuItem[] children)
    {
        var item = new ToolStripMenuItem(text);
        item.DropDownItems.AddRange(children);
        if (item.DropDown is ToolStripDropDownMenu dd) { dd.ShowImageMargin = false; dd.ShowCheckMargin = true; }
        HookRoundCorners(item.DropDown);
        return item;
    }

    static void HookRoundCorners(ToolStripDropDown dd) =>
        dd.Opened += (_, _) => Native.RoundCorners(dd.Handle, dd.BackColor);

    void SyncMenu()
    {
        glassItem.Checked = settings.Glass;
        solidItem.Checked = !settings.Glass;
        darkItem.Checked = settings.Theme == "dark";
        lightItem.Checked = settings.Theme == "light";
        systemItem.Checked = settings.Theme == "system";
        leftItem.Checked = settings.Position != "right";
        rightItem.Checked = settings.Position == "right";
        foreach (var (item, secs) in durationItems) item.Checked = settings.DurationSeconds == secs;
        fullscreenItem.Checked = settings.ShowInFullscreen;
        startupItem.Checked = Startup.IsEnabled;

        bool dark = settings.IsDark;
        if (menuDark != dark)
        {
            menuDark = dark;
            var renderer = new MenuRenderer(dark);
            ApplyRenderer(menu, renderer);
        }
        UpdateTrayIcon();
    }

    static void ApplyRenderer(ToolStripDropDown dd, ToolStripRenderer r)
    {
        dd.Renderer = r;
        dd.BackColor = r is MenuRenderer mr ? mr.Background : dd.BackColor;
        foreach (ToolStripItem it in dd.Items)
            if (it is ToolStripMenuItem mi && mi.HasDropDownItems) ApplyRenderer(mi.DropDown, r);
    }

    void UpdateTrayIcon()
    {
        bool lightTaskbar = AppSettings.ReadLight("SystemUsesLightTheme");
        if (iconForLightTaskbar == lightTaskbar && trayIcon != null) return;
        iconForLightTaskbar = lightTaskbar;

        int size = SystemInformation.SmallIconSize.Width;
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using var b = new SolidBrush(lightTaskbar ? Color.FromArgb(28, 28, 30) : Color.White);
            Fx.FillNote(g, b, new RectangleF(0, 0, size, size));
        }
        var h = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(h).Clone();
        Native.DestroyIcon(h);

        tray.Icon = icon;
        trayIcon?.Dispose();
        trayIcon = icon;
    }

    static void Delay(int ms, Action action)
    {
        var t = new System.Windows.Forms.Timer { Interval = ms };
        t.Tick += (_, _) => { t.Stop(); t.Dispose(); action(); };
        t.Start();
    }

    void OnUserPreferenceChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category == Microsoft.Win32.UserPreferenceCategory.General) UpdateTrayIcon();
    }

    void Quit()
    {
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        tray.Visible = false;
        watcher.Dispose();
        toast.HideNow();
        toast.Dispose();
        tray.Dispose();
        trayIcon?.Dispose();
        last?.Dispose();
        ExitThread();
    }
}
