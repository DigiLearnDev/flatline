using System.IO;
using System.Text;
using System.Text.Json;

namespace ProgresLista;

public enum ContextLevel { Normal, Warning, Over }

/// <summary>Hranice kontextu a barva čáry kontextu.</summary>
public static class ContextMeter
{
    /// <summary>Výchozí hranice kontextu v tokenech.</summary>
    public const long DefaultBoundary = 150_000;

    /// <summary>Žlutá od hranice kontextu −10 %, červená až po jejím přesažení.</summary>
    public static ContextLevel Level(long tokens, long boundary) =>
        tokens > boundary ? ContextLevel.Over
        : tokens * 10 >= boundary * 9 ? ContextLevel.Warning
        : ContextLevel.Normal;
}

/// <summary>Co lišta vyčte z transkriptu session.</summary>
/// <param name="ContextTokens">Zaplnění kontextu: vstupní tokeny (včetně cache) poslední odpovědi hlavního agenta.</param>
/// <param name="OpenTurnSince">Kdy začal rozpracovaný tah (ms od epochy); null, když session na nic nepracuje.</param>
/// <param name="LastActivity">Čas posledního záznamu hlavního agenta nebo uživatele (ms od epochy).</param>
public sealed record TranscriptInfo(long? ContextTokens, long? OpenTurnSince, long? LastActivity);

/// <summary>
/// Čte transkript Claude Code (.jsonl). Transkript jen roste, proto se pamatuje, kam se dočetlo,
/// a při dalším čtení se zpracují jen nové řádky. Záznamy subagentů (isSidechain) se přeskakují.
/// </summary>
public sealed class TranscriptReader
{
    /// <summary>Rozpracovaný tah bez nového záznamu déle než tohle se považuje za opuštěný (např. lokální příkaz bez odpovědi).</summary>
    public const long StaleTurnMs = 10 * 60 * 1000;

    /// <summary>Kolik bajtů z konce transkriptu se přečte poprvé (celé transkripty mají i desítky MB).</summary>
    public const long FirstReadTailBytes = 2 * 1024 * 1024;

    private sealed class Cursor
    {
        public long Offset;          // konec posledního celého řádku
        public long? Tokens;
        public long? OpenSince;
        public long? LastActivity;
        public TranscriptInfo Info => new(Tokens, OpenSince, LastActivity);
    }

    private readonly Dictionary<string, Cursor> _cursors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Stav session pro lištu. Hook hlásí „hotovo“ na konci tahu, ale o novém tahu se dozvědět nemusí:
    /// umbrellu probudí zpráva od subsession a ona pracuje dál. Když po posledním hlášení hooku začal
    /// v transkriptu nový tah a není opuštěný, session běží.
    /// </summary>
    public static string LiveState(string hookState, long hookUpdated, TranscriptInfo t, long now) =>
        hookState == "done" && t.OpenTurnSince is { } since && since > hookUpdated
            && t.LastActivity is { } last && now - last < StaleTurnMs
            ? "running" : hookState;

    public TranscriptInfo Read(string path)
    {
        if (!_cursors.TryGetValue(path, out var c)) _cursors[path] = c = new Cursor();
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length < c.Offset) { _cursors[path] = c = new Cursor(); }   // soubor byl přepsán
            if (fs.Length == c.Offset) return c.Info;

            // Poprvé stačí konec souboru: kontext i rozpracovaný tah určují poslední záznamy.
            // První (nejspíš useknutý) řádek konce se zahodí.
            bool skipFirstLine = false;
            if (c.Offset == 0 && fs.Length > FirstReadTailBytes)
            {
                c.Offset = fs.Length - FirstReadTailBytes;
                skipFirstLine = true;
            }

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
                if (skipFirstLine) skipFirstLine = false;
                else Apply(c, Encoding.UTF8.GetString(bytes, start, i - start));
                start = i + 1;
            }
            c.Offset += start;
            return c.Info;
        }
        catch (IOException) { return c.Info; }
        catch (UnauthorizedAccessException) { return c.Info; }
    }

    private static void Apply(Cursor c, string line)
    {
        // Rychlé síto: zajímají nás jen zprávy uživatele a hlavního agenta.
        bool assistant = line.Contains("\"type\":\"assistant\"");
        if (!assistant && !line.Contains("\"type\":\"user\"")) return;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            if (root.TryGetProperty("isSidechain", out var side) && side.ValueKind == JsonValueKind.True) return;
            if (root.TryGetProperty("isMeta", out var meta) && meta.ValueKind == JsonValueKind.True) return;
            var type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            if (type is not ("assistant" or "user")) return;
            if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object) return;

            long? at = root.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(ts.GetString(), out var dt) ? dt.ToUnixTimeMilliseconds() : null;
            if (at != null) c.LastActivity = at;

            if (type == "assistant")
            {
                if (msg.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
                {
                    long Get(string name) => u.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : 0;
                    c.Tokens = Get("input_tokens") + Get("cache_creation_input_tokens") + Get("cache_read_input_tokens");
                }
                var stop = msg.TryGetProperty("stop_reason", out var sr) && sr.ValueKind == JsonValueKind.String ? sr.GetString() : null;
                if (stop is "end_turn" or "stop_sequence" or "max_tokens") c.OpenSince = null;   // odpověď je hotová
                else if (stop != null) c.OpenSince ??= at;                                      // tool_use: tah pokračuje
            }
            else if (IsInterruption(msg)) c.OpenSince = null;
            else c.OpenSince ??= at;   // nový pokyn, zpráva od subsession nebo výsledek nástroje
        }
        catch (JsonException) { }
    }

    private static bool IsInterruption(JsonElement msg)
    {
        if (!msg.TryGetProperty("content", out var content)) return false;
        string? text = content.ValueKind switch
        {
            JsonValueKind.String => content.GetString(),
            JsonValueKind.Array => content.EnumerateArray()
                .Where(b => b.ValueKind == JsonValueKind.Object && b.TryGetProperty("text", out var x) && x.ValueKind == JsonValueKind.String)
                .Select(b => b.GetProperty("text").GetString()).FirstOrDefault(),
            _ => null,
        };
        return text?.StartsWith("[Request interrupted", StringComparison.Ordinal) == true;
    }
}
