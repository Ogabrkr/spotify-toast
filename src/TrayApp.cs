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

    readonly ToolStripMenuItem glassItem, solidItem, darkItem, lightItem, systemItem, leftItem, rightItem,
        smallItem, mediumItem, largeItem, langSystemItem, ptItem, enItem, esItem,
        fullscreenItem, startupItem, showItem, quitItem,
        styleMenu, themeMenu, posMenu, sizeMenu, durMenu, langMenu;
    readonly (ToolStripMenuItem item, int secs)[] durationItems;
    Lang? textsFor;

    public TrayApp(string[] args)
    {
        settings = AppSettings.Load(out bool firstRun);
        if (firstRun) settings.Save(); // "Iniciar com o Windows" fica a cargo do instalador

        toast = new ToastWindow();
        _ = toast.Handle; // cria a janela já (fica invisível)

        menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true, Padding = new Padding(0, 4, 0, 4) };
        var header = new ToolStripMenuItem("Spotify Toast") { Enabled = false, Font = new Font("Segoe UI Semibold", 9f) };

        // os textos são preenchidos em ApplyTexts (mudam com o idioma)
        glassItem = Choice(() => settings.Glass = true);
        solidItem = Choice(() => settings.Glass = false);
        styleMenu = Sub(glassItem, solidItem);

        darkItem = Choice(() => settings.Theme = "dark");
        lightItem = Choice(() => settings.Theme = "light");
        systemItem = Choice(() => settings.Theme = "system");
        themeMenu = Sub(darkItem, lightItem, systemItem);

        leftItem = Choice(() => settings.Position = "left");
        rightItem = Choice(() => settings.Position = "right");
        posMenu = Sub(leftItem, rightItem);

        smallItem = Choice(() => settings.Size = "small");
        mediumItem = Choice(() => settings.Size = "medium");
        largeItem = Choice(() => settings.Size = "large");
        sizeMenu = Sub(smallItem, mediumItem, largeItem);

        durationItems = new[] { 3, 5, 8, 12 }
            .Select(n => (Choice(() => settings.DurationSeconds = n, preview: false), n)).ToArray();
        durMenu = Sub(durationItems.Select(d => d.item).ToArray());

        langSystemItem = Choice(() => settings.Language = "system");
        // cada idioma aparece escrito nele mesmo
        ptItem = Choice(() => settings.Language = "pt"); ptItem.Text = "Português";
        enItem = Choice(() => settings.Language = "en"); enItem.Text = "English";
        esItem = Choice(() => settings.Language = "es"); esItem.Text = "Español";
        langMenu = Sub(langSystemItem, new ToolStripSeparator(), ptItem, enItem, esItem);

        fullscreenItem = new ToolStripMenuItem("", null, (_, _) =>
        {
            settings.ShowInFullscreen = !settings.ShowInFullscreen;
            settings.Save();
        });
        startupItem = new ToolStripMenuItem("", null, (_, _) => Startup.Set(!Startup.IsEnabled));
        showItem = new ToolStripMenuItem("", null, (_, _) => Preview());
        quitItem = new ToolStripMenuItem("", null, (_, _) => Quit());

        menu.Items.AddRange(new ToolStripItem[]
        {
            header, new ToolStripSeparator(),
            styleMenu, themeMenu, posMenu, sizeMenu, durMenu, langMenu, new ToolStripSeparator(),
            fullscreenItem, startupItem, new ToolStripSeparator(),
            showItem, quitItem,
        });
        ApplyTexts();
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

        // --demo: mostra a música atual | --sample ["título"]: música de exemplo (prints do README e testes)
        // --show-menu: abre o menu do tray num ponto fixo (para prints do README)
        if (args.Contains("--demo")) Delay(1500, Preview);
        if (args.Contains("--sample"))
            Delay(1200, () =>
            {
                // --sample "título": testa nomes com emoji, longos etc.
                int i = Array.IndexOf(args, "--sample");
                string title = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : "Neon Coastline";
                using var sample = new TrackInfo(title, "The Midnight Arcade", "", Fx.SampleCover(256));
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
            tray.ShowBalloonTip(5000, "Spotify Toast", settings.Text.MediaError + ex.Message, ToolTipIcon.Warning);
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
            using var sample = new TrackInfo(settings.Text.NoTrackYet, settings.Text.PressPlay, "", null);
            toast.ShowTrack(sample, settings);
        }
    }

    ToolStripMenuItem Choice(Action apply, bool preview = true) => new("", null, (_, _) =>
    {
        apply();
        settings.Save();
        UpdateTrayIcon();
        if (preview) Preview();
    });

    static ToolStripMenuItem Sub(params ToolStripItem[] children)
    {
        var item = new ToolStripMenuItem();
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
        smallItem.Checked = settings.Size == "small";
        mediumItem.Checked = settings.Size is not ("small" or "large");
        largeItem.Checked = settings.Size == "large";
        foreach (var (item, secs) in durationItems) item.Checked = settings.DurationSeconds == secs;
        langSystemItem.Checked = settings.Language is not ("pt" or "en" or "es");
        ptItem.Checked = settings.Language == "pt";
        enItem.Checked = settings.Language == "en";
        esItem.Checked = settings.Language == "es";
        ApplyTexts();
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

    void ApplyTexts()
    {
        var t = settings.Text;
        if (ReferenceEquals(textsFor, t)) return;
        textsFor = t;
        styleMenu.Text = t.Style; glassItem.Text = t.Glass; solidItem.Text = t.Solid;
        themeMenu.Text = t.Theme; darkItem.Text = t.Dark; lightItem.Text = t.Light; systemItem.Text = t.FollowWindows;
        posMenu.Text = t.Position; leftItem.Text = t.TopLeft; rightItem.Text = t.TopRight;
        sizeMenu.Text = t.Size; smallItem.Text = t.Small; mediumItem.Text = t.Medium; largeItem.Text = t.Large;
        durMenu.Text = t.Duration;
        foreach (var (item, secs) in durationItems) item.Text = string.Format(t.SecondsFormat, secs);
        langMenu.Text = t.Language; langSystemItem.Text = t.Automatic;
        fullscreenItem.Text = t.ShowOverFullscreen; startupItem.Text = t.StartWithWindows;
        showItem.Text = t.ShowNotification; quitItem.Text = t.Quit;
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
