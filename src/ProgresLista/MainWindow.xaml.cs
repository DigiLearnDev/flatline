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
                sessions.Add(new SessionView(s, file) { AppLink = _titles.AppLink(s.Session_id) });
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

        // Nepřipnuté hotové sessions, které jsou idle přes 10 minut, jdou do záložky Hotovo.
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        bool InDone(SessionView s) => !s.Pinned && s.Status.State == "done" && now - s.Status.Updated > DoneAfterMs;
        var doneTab = sessions.Where(InDone).OrderByDescending(s => s.Status.Updated).ToList();

        Items.ItemsSource = sessions.Where(s => !InDone(s))
            .OrderBy(s => s.Pinned ? 0 : 1)
            .ThenBy(s => s.Status.State switch { "attention" => 0, "running" => 1, _ => 2 })
            .ThenBy(s => s.Status.Name)
            .ToList();
        DoneItems.ItemsSource = doneTab;

        // Počítadlo = kolik sessions do záložky spadlo od posledního vynulování.
        int fresh = doneTab.Count(s => s.Status.Updated + DoneAfterMs > _state.CounterResetAt);
        DoneText.Text = fresh > 0 ? $"Hotovo  {fresh}" : "Hotovo";
        DoneChip.Opacity = doneTab.Count > 0 ? 1 : 0.5;

        int run = sessions.Count(s => s.Status.State == "running");
        int att = sessions.Count(s => s.Status.State == "attention");
        Summary.Text = $"běží {run}  ·  čeká {att}";
    }

    private const long DoneAfterMs = 10 * 60 * 1000;
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
        }
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

    public string? AppLink { get; init; }

    public SessionView(SessionStatus status, string file)
    {
        Status = status;
        File = file;
    }

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
    public Brush Dot => Status.State switch { "running" => Gray, "attention" => DarkOrange, _ => Blue };
    public Brush Background => Status.State switch { "attention" => AttentionBg, "done" => DoneBg, _ => ItemBg };
    public Brush Foreground => Status.State == "attention" ? DarkText : LightText;

    public string Label
    {
        get
        {
            var mins = (int)(DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(Status.Updated)).TotalMinutes;
            return mins < 1 ? Status.Name : $"{Status.Name} · {mins} min";
        }
    }

    public string Tooltip
    {
        get
        {
            var state = Status.State switch { "running" => "běží", "attention" => "potřebuje pozornost", _ => "hotovo" };
            var msg = string.IsNullOrEmpty(Status.Message) ? "" : "\n" + Status.Message;
            return $"{state}\n{Status.Cwd}{msg}\n(pravý klik = odebrat)";
        }
    }

    private static Brush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
