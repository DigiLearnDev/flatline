using System.IO;
using System.Text.Json;

namespace ProgresLista;

/// <summary>
/// Mapuje CLI session ID (co dostává hook) na název session z levého panelu desktop aplikace Claude.
/// Desktop app ukládá metadata do %LOCALAPPDATA%\Claude-Data\*\claude-code-sessions\**\local_*.json
/// (pole title, cliSessionId, lastAssistantCliSessionId). Soubory se čtou znovu jen při změně.
/// </summary>
public sealed class TitleIndex
{
    private readonly Dictionary<string, (DateTime Stamp, string? Title, string[] CliIds)> _files = new();
    private readonly Dictionary<string, string> _titles = new();

    private readonly Dictionary<string, string> _localIds = new();

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
                _files[file] = (stamp, ReadTitle(file, out var ids), ids);
            }
        }
        foreach (var gone in _files.Keys.Where(k => !seen.Contains(k)).ToList()) _files.Remove(gone);

        _titles.Clear();
        _localIds.Clear();
        foreach (var (file, (_, title, ids)) in _files)
            foreach (var id in ids)
            {
                // Název souboru je ID session v desktop aplikaci (local_…), z něj vzniká odkaz.
                _localIds[id] = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrWhiteSpace(title)) _titles[id] = title;
            }
    }

    private static string? ReadTitle(string file, out string[] cliIds)
    {
        cliIds = Array.Empty<string>();
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            var r = doc.RootElement;
            cliIds = new[] { "cliSessionId", "lastAssistantCliSessionId" }
                .Select(p => r.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null)
                .Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).Distinct().ToArray();
            return r.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        }
        catch { return null; } // soubor se právě zapisuje, načte se příště
    }
}
