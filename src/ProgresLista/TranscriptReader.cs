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
/// <param name="TurnOpen">Session má rozpracovaný tah: poslední záznam je pokyn, zpráva od subsession,
/// výsledek nástroje nebo odpověď, která volá nástroj.</param>
/// <param name="LastActivity">Čas posledního záznamu hlavního agenta nebo uživatele (ms od epochy).</param>
public sealed record TranscriptInfo(long? ContextTokens, bool TurnOpen, long? LastActivity);

/// <summary>
/// Čte transkript Claude Code (.jsonl). Transkript jen roste, proto se pamatuje, kam se dočetlo,
/// a při dalším čtení se zpracují jen nové řádky. Záznamy subagentů (isSidechain) se přeskakují.
/// </summary>
public sealed class TranscriptReader
{
    /// <summary>
    /// Rozpracovaný tah bez nového záznamu déle než tohle se považuje za opuštěný (session ukončená
    /// uprostřed tahu). Je dlouhý, aby ho nepřekročil ani dlouho běžící nástroj, např. verify.
    /// </summary>
    public const long StaleTurnMs = 60 * 60 * 1000;

    /// <summary>Kolik bajtů z konce transkriptu se přečte poprvé (celé transkripty mají i desítky MB).</summary>
    public const long FirstReadTailBytes = 4 * 1024 * 1024;

    private sealed class Cursor
    {
        public long Offset;          // konec posledního celého řádku
        public long? Tokens;
        public bool TurnOpen;
        public long? LastActivity;
        public TranscriptInfo Info => new(Tokens, TurnOpen, LastActivity);
    }

    private readonly Dictionary<string, Cursor> _cursors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Stav session pro lištu. Hook hlásí „hotovo“ na konci tahu, ale o dalším tahu se dozvědět nemusí:
    /// umbrellu probudí zpráva od subsession a ona pracuje dál (verify, merge). Hook také hlásí
    /// „hotovo“ při auto-compactu uprostřed tahu. Když má session rozpracovaný tah a po posledním
    /// hlášení hooku v něm přibyl záznam, session běží.
    /// </summary>
    public static string LiveState(string hookState, long hookUpdated, TranscriptInfo t, long now) =>
        hookState == "done" && t.TurnOpen
            && t.LastActivity is { } last && last > hookUpdated && now - last < StaleTurnMs
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
                else ReadEntry(c, Encoding.UTF8.GetString(bytes, start, i - start));
                start = i + 1;
            }
            c.Offset += start;
            return c.Info;
        }
        catch (IOException) { return c.Info; }
        catch (UnauthorizedAccessException) { return c.Info; }
    }

    private static void ReadEntry(Cursor c, string line)
    {
        // Rychlé síto: zajímají nás jen zprávy uživatele a hlavního agenta.
        bool assistant = line.Contains("\"type\":\"assistant\"");
        if (!assistant && !line.Contains("\"type\":\"user\"")) return;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            bool Flag(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
            // Subagenti, systémové vložky a shrnutí po compactu nejsou tah session.
            if (Flag("isSidechain") || Flag("isMeta") || Flag("isCompactSummary")) return;
            var type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            if (type is not ("assistant" or "user")) return;
            if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object) return;

            if (type == "assistant" && msg.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
            {
                long Get(string name) => u.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : 0;
                c.Tokens = Get("input_tokens") + Get("cache_creation_input_tokens") + Get("cache_read_input_tokens");
            }

            if (type == "assistant")
            {
                // Odpověď, která volá nástroj, tah nechává otevřený; jakákoli jiná ho uzavírá.
                var stop = msg.TryGetProperty("stop_reason", out var sr) && sr.ValueKind == JsonValueKind.String ? sr.GetString() : null;
                if (stop == null) return;   // rozepsaná odpověď nic nemění
                c.TurnOpen = stop is "tool_use" or "pause_turn";
            }
            else
            {
                var text = FirstText(msg);
                // Lokální příkazy (/model, /compact…) model nespouští, tah nezačínají.
                if (text != null && (text.StartsWith("<command-name>", StringComparison.Ordinal)
                    || text.StartsWith("<local-command-", StringComparison.Ordinal))) return;
                c.TurnOpen = text?.StartsWith("[Request interrupted", StringComparison.Ordinal) != true;
            }

            if (root.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(ts.GetString(), out var dt))
                c.LastActivity = dt.ToUnixTimeMilliseconds();
        }
        catch (JsonException) { }
    }

    /// <summary>Text zprávy uživatele (řetězec nebo první textový blok); null u výsledku nástroje.</summary>
    private static string? FirstText(JsonElement msg)
    {
        if (!msg.TryGetProperty("content", out var content)) return null;
        return content.ValueKind switch
        {
            JsonValueKind.String => content.GetString(),
            JsonValueKind.Array => content.EnumerateArray()
                .Where(b => b.ValueKind == JsonValueKind.Object && b.TryGetProperty("text", out var x) && x.ValueKind == JsonValueKind.String)
                .Select(b => b.GetProperty("text").GetString()).FirstOrDefault(),
            _ => null,
        };
    }
}
