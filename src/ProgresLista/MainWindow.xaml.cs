using System.IO;
using System.Media;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ProgresLista;

public partial class MainWindow : Window
{
    /// <summary>Složka, do které hook zapisuje jeden JSON soubor na session.</summary>
    public static readonly string StatusDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "progres-lista");

    private readonly AppBar _appBar;
    private readonly FileSystemWatcher _watcher;
    private readonly DispatcherTimer _timer;
    private readonly HashSet<string> _attentionSeen = new();
    private readonly TitleIndex _titles = new();
    private readonly ContextMeter _context = new();

    public MainWindow()
    {
        InitializeComponent();
        Directory.CreateDirectory(StatusDir);
        _appBar = new AppBar(this, 30);

        SourceInitialized += (_, _) => _appBar.Register();
        Closing += (_, _) => _appBar.Unregister();

        _watcher = new FileSystemWatcher(StatusDir, "*.json") { EnableRaisingEvents = true };
        FileSystemEventHandler onChange = (_, _) => Dispatcher.BeginInvoke(Reload);
        _watcher.Created += onChange;
        _watcher.Changed += onChange;
        _watcher.Deleted += onChange;
        _watcher.Renamed += (_, _) => Dispatcher.BeginInvoke(Reload);

        // Pojistka a obnova „před X min“.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _timer.Tick += (_, _) => Reload();
        _timer.Start();

        Reload();

        var usageTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
        usageTimer.Tick += async (_, _) => await RefreshUsage();
        usageTimer.Start();
        _ = RefreshUsage();
    }

    private async Task RefreshUsage()
    {
        var u = await UsageClient.FetchAsync();
        Show(FiveBar, FiveText, u.FiveHour);
        Show(WeekBar, WeekText, u.Week);
        Usage.ToolTip = u.Error ?? $"5h: reset {Fmt(u.FiveHour)}\nTýden: reset {Fmt(u.Week)}";

        static string Fmt(UsageWindow? w) => w?.ResetsAt?.ToLocalTime().ToString("ddd HH:mm") ?? "?";
    }

    private static void Show(System.Windows.Controls.ProgressBar bar, System.Windows.Controls.TextBlock text, UsageWindow? w)
    {
        bar.Value = w?.Percent ?? 0;
        bar.Foreground = new SolidColorBrush(w?.Percent switch
        {
            >= 90 => Color.FromRgb(0xEF, 0x44, 0x44),
            >= 70 => Color.FromRgb(0xF5, 0x9E, 0x0B),
            _ => Color.FromRgb(0x3B, 0x82, 0xF6),
        });
        text.Text = w == null ? "?" : $"{w.Percent:0}%";
    }

    private void Reload()
    {
        _titles.Refresh();
        var sessions = new List<SessionView>();
        foreach (var file in Directory.EnumerateFiles(StatusDir, "*.json"))
        {
            try
            {
                var s = JsonSerializer.Deserialize<SessionStatus>(File.ReadAllText(file),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (s == null) continue;
                // Ručně pojmenovaná session z levého panelu má přednost před názvem složky.
                if (_titles.Lookup(s.Session_id) is { } title) s.Name = title;
                sessions.Add(new SessionView(s, file)
                {
                    AppLink = _titles.AppLink(s.Session_id),
                    ContextTokens = TranscriptOf(s) is { } t ? _context.Read(t) : null,
                    ContextBoundary = _state.ContextBoundary,
                });
            }
            catch { /* soubor se právě zapisuje, načte se při další změně */ }
        }

        // Zvuk jen při nově vzniklé potřebě pozornosti.
        var attention = sessions.Where(s => s.Status.State == "attention")
            .Select(s => s.Status.Session_id + s.Status.Updated).ToHashSet();
        if (attention.Except(_attentionSeen).Any()) SystemSounds.Exclamation.Play();
        _attentionSeen.Clear();
        _attentionSeen.UnionWith(attention);

        // Krátké bliknutí lišty: když přibude nová session, nebo když existující přejde na hotovo
        // či pozornost. Přechod existující session na „běží“ (šedá) a první načtení neblikají.
        var states = sessions.ToDictionary(s => s.Status.Session_id, s => s.Status.State);
        if (_lastStates != null && states.Any(kv => _lastStates.TryGetValue(kv.Key, out var old)
                ? old != kv.Value && kv.Value != "running"
                : true))
            FlashBar();
        _lastStates = states;

        foreach (var s in sessions) s.Pinned = _state.Pins.Contains(s.Status.Session_id);

        // Vazby umbrella → subsession z metadat desktop aplikace. Session patří k nejbližší session
        // v řetězci spuštění, která je na liště (i když prostřední session z lišty zmizela).
        var views = sessions.GroupBy(s => s.Status.Session_id).ToDictionary(g => g.Key, g => g.First());
        string? ParentOf(string id) => _titles.SpawnChain(id)
            .Select(ids => ids.FirstOrDefault(views.ContainsKey)).FirstOrDefault(x => x != null);
        var layout = BarLayout.Build(
            views.Values.Select(v => new SessionNode(v.Status.Session_id, v.Status.State, v.Status.Updated, ParentOf(v.Status.Session_id))),
            _state.Pins, _state.CounterResetAt);

        List<SessionView> ViewsOf(IEnumerable<SessionNode> nodes) =>
            nodes.Select(n => views[n.Id]).OrderBy(v => v.Status.Name).ToList();
        SessionView OnBar(SessionGroup g) => views[g.Root.Id].ForGroup(g, ViewsOf(g.Children), inTab: false);
        SessionView InTab(SessionGroup g) => views[g.Root.Id].ForGroup(g, ViewsOf(g.Children), inTab: true);

        PinnedItems.ItemsSource = layout.Pinned.Select(OnBar).OrderBy(v => v.Status.Name).ToList();
        AttentionItems.ItemsSource = ViewsOf(layout.Attention);
        RunningItems.ItemsSource = layout.Running.Select(InTab).ToList();
        DoneItems.ItemsSource = layout.Done.Select(InTab).ToList();
        RefreshChildPopup(DoneChildPopup, DoneChildItems, DoneItems);
        RefreshChildPopup(RunningChildPopup, RunningChildItems, RunningItems);

        // Hotovo ukazuje, kolik je v seznamu; modrý odznak, kolik jich doběhlo od vynulování.
        DoneText.Text = $"Hotovo  {layout.Done.Count}";
        DoneNewText.Text = layout.DoneNew.ToString();
        DoneNewBadge.Visibility = layout.DoneNew > 0 ? Visibility.Visible : Visibility.Collapsed;
        DoneChip.Opacity = layout.Done.Count > 0 ? 1 : 0.5;
        RunningText.Text = $"Running  {layout.Running.Count}";
        RunningChip.Opacity = layout.Running.Count > 0 ? 1 : 0.5;
    }

    private readonly BarState _state = BarState.Load();

    private Dictionary<string, string>? _lastStates;

    private void FlashBar()
    {
        var ease = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut };
        var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 0.35, TimeSpan.FromMilliseconds(150))
        {
            AutoReverse = true,
            EasingFunction = ease,
        };
        Flash.BeginAnimation(OpacityProperty, anim);
    }

    private void Item_Click(object sender, MouseButtonEventArgs e)
    {
        // Otevře session v desktop aplikaci Claude (jen sessions, které tam existují).
        if (((FrameworkElement)sender).DataContext is SessionView { AppLink: { } link })
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link) { UseShellExecute = true }); }
            catch { }
            DonePopup.IsOpen = false;
            RunningPopup.IsOpen = false;
        }
    }

    private void RunningChip_Click(object sender, MouseButtonEventArgs e) => RunningPopup.IsOpen = !RunningPopup.IsOpen;

    private void RunningChip_RightClick(object sender, MouseButtonEventArgs e) => e.Handled = true;

    /// <summary>Najetí na umbrellu v rozbalené záložce otevře vpravo sloupec s jejími subsessions.</summary>
    private void Item_MouseEnter(object sender, MouseEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not SessionView { ShowsChildrenOnHover: true } v) return;
        var (popup, items) = DoneItems.Items.Contains(v) ? (DoneChildPopup, DoneChildItems) : (RunningChildPopup, RunningChildItems);
        if (v.Children.Count == 0) { popup.IsOpen = false; return; }
        items.ItemsSource = v.Children;
        popup.PlacementTarget = (UIElement)sender;
        popup.Tag = v.Status.Session_id;
        popup.IsOpen = true;
    }

    /// <summary>Po obnovení záložky ukáže otevřený sloupec čerstvé subsessions téže umbrelly, nebo se zavře.</summary>
    private static void RefreshChildPopup(System.Windows.Controls.Primitives.Popup popup,
        System.Windows.Controls.ItemsControl items, System.Windows.Controls.ItemsControl tab)
    {
        if (!popup.IsOpen) return;
        var v = tab.Items.OfType<SessionView>().FirstOrDefault(x => x.Status.Session_id == (string?)popup.Tag);
        if (v == null || v.Children.Count == 0) { popup.IsOpen = false; return; }
        items.ItemsSource = v.Children;
        // Položky záložky vznikly znovu; sloupec se po vykreslení přichytí k nové položce umbrelly.
        tab.Dispatcher.BeginInvoke(() =>
        {
            if (tab.ItemContainerGenerator.ContainerFromItem(v) is FrameworkElement c && c.IsVisible)
            {
                popup.PlacementTarget = c;
                popup.HorizontalOffset += 0.01;   // přinutí Popup přepočítat polohu
                popup.HorizontalOffset -= 0.01;
            }
            else popup.IsOpen = false;
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void TabPopup_Closed(object? sender, EventArgs e)
    {
        DoneChildPopup.IsOpen = false;
        RunningChildPopup.IsOpen = false;
    }

    private void Pin_Click(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not SessionView v) return;
        if (!_state.Pins.Remove(v.Status.Session_id)) _state.Pins.Add(v.Status.Session_id);
        _state.Save();
        e.Handled = true;
        Reload();
    }

    private void DoneChip_Click(object sender, MouseButtonEventArgs e) => DonePopup.IsOpen = !DonePopup.IsOpen;

    private void DoneChip_RightClick(object sender, MouseButtonEventArgs e)
    {
        _state.CounterResetAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _state.Save();
        e.Handled = true;
        Reload();
    }

    private void Item_RightClick(object sender, MouseButtonEventArgs e)
    {
        // Pravý klik odebere položku (např. session, která skončila bez SessionEnd).
        if (((FrameworkElement)sender).DataContext is SessionView v)
        {
            try { File.Delete(v.File); } catch { }
            _state.Pins.Remove(v.Status.Session_id);
            _state.Save();
        }
        e.Handled = true;
    }

    /// <summary>
    /// Cesta k transkriptu session. Starší stavové soubory ji nemají (hook ji zapisuje až od zavedení
    /// čáry kontextu), pak se odvodí z pracovní složky stejně jako u Claude Code: ~/.claude/projects/
    /// &lt;cwd, kde je každý znak mimo písmena a číslice nahrazen pomlčkou&gt;/&lt;session_id&gt;.jsonl.
    /// </summary>
    private static string? TranscriptOf(SessionStatus s)
    {
        if (!string.IsNullOrEmpty(s.Transcript)) return s.Transcript;
        if (string.IsNullOrEmpty(s.Cwd)) return null;
        var dir = System.Text.RegularExpressions.Regex.Replace(s.Cwd, "[^A-Za-z0-9]", "-");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "projects", dir, s.Session_id + ".jsonl");
    }

    private static readonly long[] ContextBoundaries = { 100_000, 150_000, 200_000, 300_000, 500_000, 1_000_000 };

    private void Bar_RightClick(object sender, MouseButtonEventArgs e)
    {
        // Jen volné místo lišty; položky a záložka Hotovo mají vlastní pravý klik, tlačítka ho ignorují.
        for (var d = e.OriginalSource as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d))
            if (d is System.Windows.Controls.Primitives.ButtonBase) return;
        // Rozbalená záložka Hotovo je v jiném okně (Popup), její události ale probublají sem.
        if (e.OriginalSource is Visual v && PresentationSource.FromVisual(v)?.RootVisual != this) return;

        var menu = new System.Windows.Controls.ContextMenu();
        menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "Plná čára kontextu při", IsEnabled = false });
        foreach (var boundary in ContextBoundaries)
        {
            var item = new System.Windows.Controls.MenuItem
            {
                Header = SessionView.FormatTokens(boundary) + " tokenů",
                IsCheckable = true,
                IsChecked = boundary == _state.ContextBoundary,
            };
            item.Click += (_, _) =>
            {
                _state.ContextBoundary = boundary;
                _state.Save();
                Reload();
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed class SessionStatus
{
    public string Session_id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Cwd { get; set; } = "";
    public string State { get; set; } = "done";
    public string Message { get; set; } = "";
    public string Transcript { get; set; } = "";
    public long Updated { get; set; }
}

public sealed class SessionView
{
    private static readonly Brush Blue = Freeze(Color.FromRgb(0x3B, 0x82, 0xF6));
    private static readonly Brush Gray = Freeze(Color.FromRgb(0xA1, 0xA1, 0xAA));
    private static readonly Brush DarkOrange = Freeze(Color.FromRgb(0xC2, 0x41, 0x0C));
    private static readonly Brush ItemBg = Freeze(Color.FromRgb(0x2B, 0x2D, 0x31));
    private static readonly Brush DoneBg = Freeze(Color.FromRgb(0x15, 0x80, 0x3D));
    private static readonly Brush AttentionBg = Freeze(Color.FromRgb(0xEA, 0xB3, 0x08));
    private static readonly Brush LightText = Freeze(Color.FromRgb(0xEE, 0xEE, 0xEE));
    private static readonly Brush DarkText = Freeze(Color.FromRgb(0x1E, 0x1F, 0x22));

    private static readonly Brush LineLight = Freeze(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF));
    private static readonly Brush LineDark = Freeze(Color.FromArgb(0x90, 0x1E, 0x1F, 0x22));
    private static readonly Brush LineYellow = Freeze(Color.FromRgb(0xFA, 0xCC, 0x15));
    private static readonly Brush LineYellowOnYellow = Freeze(Color.FromRgb(0x85, 0x4D, 0x0E));
    private static readonly Brush LineRed = Freeze(Color.FromRgb(0xEF, 0x44, 0x44));

    public string? AppLink { get; init; }
    public long? ContextTokens { get; init; }
    public long ContextBoundary { get; init; } = ContextMeter.DefaultBoundary;

    // Zaplnění kontextu jako tenká čára na spodní hraně položky; plná šířka = hranice kontextu.
    private double ContextFraction => Math.Clamp((double)(ContextTokens ?? 0) / Math.Max(1, ContextBoundary), 0, 1);
    public GridLength ContextFilled => new(ContextFraction, GridUnitType.Star);
    public GridLength ContextRest => new(1 - ContextFraction, GridUnitType.Star);
    public Visibility ContextVisibility => ContextTokens is > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Brush ContextBrush
    {
        get
        {
            // Na žluté položce (pozornost) by žlutá čára splynula, proto tmavší odstín.
            bool onYellow = State == "attention";
            return ContextMeter.Level(ContextTokens ?? 0, ContextBoundary) switch
            {
                ContextLevel.Over => LineRed,
                ContextLevel.Warning => onYellow ? LineYellowOnYellow : LineYellow,
                _ => onYellow ? LineDark : LineLight,
            };
        }
    }

    public static string FormatTokens(long n) => n >= 1_000_000 ? $"{n / 1_000_000.0:0.#}M" : $"{n / 1000}k";

    public SessionView(SessionStatus status, string file)
    {
        Status = status;
        File = file;
        State = status.State;
        Updated = status.Updated;
    }

    /// <summary>Stav, podle kterého se položka barví; u umbrelly souhrnný stav celé skupiny.</summary>
    public string State { get; private init; }
    /// <summary>Poslední změna; u umbrelly poslední změna v celé skupině.</summary>
    public long Updated { get; private init; }
    public IReadOnlyList<SessionView> Children { get; private init; } = Array.Empty<SessionView>();
    /// <summary>Položka v rozbalené záložce: najetí myší ukáže subsessions.</summary>
    public bool ShowsChildrenOnHover { get; private init; }
    public string UmbrellaBadge => Children.Count.ToString();
    public Visibility UmbrellaVisibility => Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Položka za celou skupinu: barva a čas podle souhrnu, v záložce navíc subsessions po najetí myší.</summary>
    public SessionView ForGroup(SessionGroup g, IReadOnlyList<SessionView> children, bool inTab) =>
        new(Status, File)
        {
            AppLink = AppLink,
            ContextTokens = ContextTokens,
            ContextBoundary = ContextBoundary,
            Pinned = Pinned,
            State = g.State switch { SessionState.Attention => "attention", SessionState.Running => "running", _ => "done" },
            Updated = g.Updated,
            Children = children,
            ShowsChildrenOnHover = inTab,
        };

    public SessionStatus Status { get; }
    public string File { get; }
    public bool Pinned { get; set; }
    public double PinOpacity => Pinned ? 1 : 0.25;
    // Připnutá = bíle vyplněný špendlík: obrys Pinned (se špičkou) přes výplň PinnedFill.
    // Nepřipnutá = jen obrys Pin.
    public string PinGlyph => Pinned ? "" : "";
    public string PinFillGlyph => Pinned ? "" : "";
    public Brush PinForeground => Pinned ? Brushes.White : Foreground;

    // Hotovo = modrý puntík jako v aplikaci Claude, běží = šedý.
    public Brush Dot => State switch { "running" => Gray, "attention" => DarkOrange, _ => Blue };
    public Brush Background => State switch { "attention" => AttentionBg, "done" => DoneBg, _ => ItemBg };
    public Brush Foreground => State == "attention" ? DarkText : LightText;

    public string Label
    {
        get
        {
            var mins = (int)(DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(Updated)).TotalMinutes;
            return mins < 1 ? Status.Name : $"{Status.Name} · {mins} min";
        }
    }

    public string Tooltip
    {
        get
        {
            var state = State switch { "running" => "běží", "attention" => "potřebuje pozornost", _ => "hotovo" };
            if (Children.Count > 0) state = $"umbrella, {Children.Count} subsessions: {state}";
            var msg = string.IsNullOrEmpty(Status.Message) ? "" : "\n" + Status.Message;
            var ctx = ContextTokens is long t and > 0 ? $"\nkontext {FormatTokens(t)} / {FormatTokens(ContextBoundary)}" : "";
            return $"{state}{ctx}\n{Status.Cwd}{msg}\n(pravý klik = odebrat)";
        }
    }

    private static Brush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
