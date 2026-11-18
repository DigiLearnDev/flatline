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
                sessions.Add(new SessionView(s, file));
            }
            catch { /* soubor se právě zapisuje, načte se při další změně */ }
        }

        // Zvuk jen při nově vzniklé potřebě pozornosti.
        var attention = sessions.Where(s => s.Status.State == "attention")
            .Select(s => s.Status.Session_id + s.Status.Updated).ToHashSet();
        if (attention.Except(_attentionSeen).Any()) SystemSounds.Exclamation.Play();
        _attentionSeen.Clear();
        _attentionSeen.UnionWith(attention);

        Items.ItemsSource = sessions
            .OrderBy(s => s.Status.State switch { "attention" => 0, "running" => 1, _ => 2 })
            .ThenBy(s => s.Status.Name)
            .ToList();

        int run = sessions.Count(s => s.Status.State == "running");
        int att = sessions.Count(s => s.Status.State == "attention");
        int done = sessions.Count - run - att;
        Summary.Text = $"Progres lišta  ·  běží {run}  ·  čeká {att}  ·  hotovo {done}";
    }

    private void Item_RightClick(object sender, MouseButtonEventArgs e)
    {
        // Pravý klik odebere položku (např. session, která skončila bez SessionEnd).
        if (((FrameworkElement)sender).DataContext is SessionView v)
        {
            try { File.Delete(v.File); } catch { }
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
    private static readonly Brush Orange = Freeze(Color.FromRgb(0xF5, 0x9E, 0x0B));
    private static readonly Brush Green = Freeze(Color.FromRgb(0x22, 0xC5, 0x5E));
    private static readonly Brush ItemBg = Freeze(Color.FromRgb(0x2B, 0x2D, 0x31));
    private static readonly Brush AttentionBg = Freeze(Color.FromRgb(0x7C, 0x4A, 0x03));

    public SessionView(SessionStatus status, string file)
    {
        Status = status;
        File = file;
    }

    public SessionStatus Status { get; }
    public string File { get; }

    public Brush Dot => Status.State switch { "running" => Blue, "attention" => Orange, _ => Green };
    public Brush Background => Status.State == "attention" ? AttentionBg : ItemBg;

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
