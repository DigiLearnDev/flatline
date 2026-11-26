using System.IO;
using System.Text.Json;

namespace ProgresLista;

/// <summary>
/// Mapuje CLI session ID (co dostává hook) na název session z levého panelu desktop aplikace Claude.
/// Desktop app ukládá metadata do %LOCALAPPDATA%\Claude-Data\*\claude-code-sessions\**\local_*.json
/// (pole title, cliSessionId, lastAssistantCliSessionId). Soubory se čtou znovu jen při změně.
/// Z pole spawnedFrom zjistí i umbrellu, která session spustila (kromě sessions, které uživatel
/// od rodiče odpojil: lineageDetached).
/// </summary>
public sealed class TitleIndex
{
    private readonly Dictionary<string, (DateTime Stamp, string? Title, string[] CliIds, string? Parent)> _files = new();
    private readonly Dictionary<string, string> _titles = new();

    private readonly Dictionary<string, string> _localIds = new();
    private readonly Dictionary<string, string> _parentLocal = new();      // local id → local id rodiče
    private readonly Dictionary<string, string[]> _cliIdsOfLocal = new();  // local id → jeho CLI ID

    /// <summary>CLI ID umbrelly, která session spustila (může jich být víc, např. po rewindu); prázdné, když žádná není.</summary>
    public IReadOnlyList<string> ParentCliIds(string cliSessionId) =>
        _localIds.TryGetValue(cliSessionId, out var l) && _parentLocal.TryGetValue(l, out var p)
            && _cliIdsOfLocal.TryGetValue(p, out var ids) ? ids : Array.Empty<string>();

    /// <summary>Odkaz, který otevře session v desktop aplikaci Claude; null pro sessions z CLI.</summary>
    public string? AppLink(string cliSessionId) =>
        _localIds.TryGetValue(cliSessionId, out var l) ? "claude://claude.ai/epitaxy/" + l : null;

    public string? Lookup(string cliSessionId) =>
        _titles.TryGetValue(cliSessionId, out var t) ? t : null;

    public void Refresh()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Claude-Data");
        if (!Directory.Exists(root)) return;

        var seen = new HashSet<string>();
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var sessions = Path.Combine(dir, "claude-code-sessions");
            if (!Directory.Exists(sessions)) continue;
            foreach (var file in Directory.EnumerateFiles(sessions, "local_*.json", SearchOption.AllDirectories))
            {
                seen.Add(file);
                var stamp = File.GetLastWriteTimeUtc(file);
                if (_files.TryGetValue(file, out var cached) && cached.Stamp == stamp) continue;
                _files[file] = (stamp, ReadTitle(file, out var ids, out var parent), ids, parent);
            }
        }
        foreach (var gone in _files.Keys.Where(k => !seen.Contains(k)).ToList()) _files.Remove(gone);

        _titles.Clear();
        _localIds.Clear();
        _parentLocal.Clear();
        _cliIdsOfLocal.Clear();
        foreach (var (file, (_, title, ids, parent)) in _files)
        {
            var local = Path.GetFileNameWithoutExtension(file);
            _cliIdsOfLocal[local] = ids;
            if (parent != null) _parentLocal[local] = parent;
            foreach (var id in ids)
            {
                // Název souboru je ID session v desktop aplikaci (local_…), z něj vzniká odkaz.
                _localIds[id] = local;
                if (!string.IsNullOrWhiteSpace(title)) _titles[id] = title;
            }
        }
    }

    private static string? ReadTitle(string file, out string[] cliIds, out string? parent)
    {
        cliIds = Array.Empty<string>();
        parent = null;
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            var r = doc.RootElement;
            cliIds = new[] { "cliSessionId", "lastAssistantCliSessionId" }
                .Select(p => r.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null)
                .Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).Distinct().ToArray();
            bool detached = r.TryGetProperty("lineageDetached", out var d) && d.ValueKind == JsonValueKind.True;
            if (!detached && r.TryGetProperty("spawnedFrom", out var sf) && sf.ValueKind == JsonValueKind.Object
                && sf.TryGetProperty("sessionId", out var ps) && ps.ValueKind == JsonValueKind.String)
                parent = ps.GetString();
            return r.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        }
        catch { return null; } // soubor se právě zapisuje, načte se příště
    }
}
