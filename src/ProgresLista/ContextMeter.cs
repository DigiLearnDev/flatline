using System.IO;
using System.Text;
using System.Text.Json;

namespace ProgresLista;

public enum ContextLevel { Normal, Warning, Over }

/// <summary>
/// Zaplnění kontextu session: vstupní tokeny (včetně cache) poslední odpovědi hlavního agenta
/// v transkriptu Claude Code (.jsonl). Transkript jen roste, proto se pamatuje, kam se dočetlo,
/// a při dalším čtení se zpracují jen nové řádky.
/// </summary>
public sealed class ContextMeter
{
    private sealed class Cursor
    {
        public long Offset;          // konec posledního celého řádku
        public long? Tokens;
    }

    private readonly Dictionary<string, Cursor> _cursors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Žlutá od limitu −10 %, červená až po jeho přesažení.</summary>
    public static ContextLevel Level(long tokens, long limit) =>
        tokens > limit ? ContextLevel.Over
        : tokens * 10 >= limit * 9 ? ContextLevel.Warning
        : ContextLevel.Normal;

    /// <returns>Počet tokenů v kontextu, nebo null, když transkript chybí nebo v něm ještě není odpověď.</returns>
    public long? Read(string path)
    {
        if (!_cursors.TryGetValue(path, out var c)) _cursors[path] = c = new Cursor();
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length < c.Offset) { c.Offset = 0; c.Tokens = null; }   // soubor byl přepsán
            if (fs.Length == c.Offset) return c.Tokens;

            fs.Position = c.Offset;
            var bytes = new byte[fs.Length - c.Offset];
            int read = 0;
            while (read < bytes.Length)
            {
                int n = fs.Read(bytes, read, bytes.Length - read);
                if (n == 0) break;
                read += n;
            }

            // Zpracují se jen celé řádky; rozepsaný konec se dočte příště.
            int start = 0;
            for (int i = 0; i < read; i++)
            {
                if (bytes[i] != (byte)'\n') continue;
                if (Parse(Encoding.UTF8.GetString(bytes, start, i - start)) is { } tokens) c.Tokens = tokens;
                start = i + 1;
            }
            c.Offset += start;
            return c.Tokens;
        }
        catch (IOException) { return c.Tokens; }
        catch (UnauthorizedAccessException) { return c.Tokens; }
    }

    private static long? Parse(string line)
    {
        // Rychlé síto: velká většina řádků (výstupy nástrojů) usage nemá.
        if (!line.Contains("\"usage\"")) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            // Subagenti mají vlastní kontext; do zaplnění session se nepočítají.
            if (root.TryGetProperty("isSidechain", out var side) && side.ValueKind == JsonValueKind.True) return null;
            if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object
                || !msg.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object) return null;
            long Get(string name) => u.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : 0;
            return Get("input_tokens") + Get("cache_creation_input_tokens") + Get("cache_read_input_tokens");
        }
        catch (JsonException) { return null; }
    }
}
