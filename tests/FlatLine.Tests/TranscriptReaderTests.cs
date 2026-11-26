using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProgresLista;

namespace FlatLine.Tests;

[TestClass]
public sealed class TranscriptReaderTests
{
    private string _path = "";

    [TestInitialize]
    public void Init() => _path = Path.Combine(Path.GetTempPath(), $"flatline-test-{Guid.NewGuid():N}.jsonl");

    [TestCleanup]
    public void Cleanup() => File.Delete(_path);

    private static string Assistant(int input, int cacheCreate, int cacheRead, int output = 50) =>
        "{\"type\":\"assistant\",\"isSidechain\":false,\"message\":{\"role\":\"assistant\",\"usage\":{" +
        $"\"input_tokens\":{input},\"cache_creation_input_tokens\":{cacheCreate}," +
        $"\"cache_read_input_tokens\":{cacheRead},\"output_tokens\":{output}" + "}}}\n";

    [TestMethod]
    public void Context_is_input_plus_cache_tokens_of_the_last_assistant_message()
    {
        File.WriteAllText(_path,
            """{"type":"user","message":{"role":"user","content":"ahoj"}}""" + "\n" +
            Assistant(2, 1000, 20000) +
            Assistant(3, 500, 40000));

        Assert.AreEqual(40503L, new TranscriptReader().Read(_path).ContextTokens);
    }

    [TestMethod]
    public void Subagent_messages_do_not_count_toward_the_session_context()
    {
        File.WriteAllText(_path,
            Assistant(1, 0, 30000) +
            Assistant(1, 0, 5000).Replace("\"isSidechain\":false", "\"isSidechain\":true"));

        Assert.AreEqual(30001L, new TranscriptReader().Read(_path).ContextTokens);
    }

    [TestMethod]
    public void Missing_transcript_has_no_context_value()
    {
        Assert.IsNull(new TranscriptReader().Read(_path).ContextTokens);
    }

    [TestMethod]
    public void Message_written_in_parts_is_counted_once_it_is_complete()
    {
        var reader = new TranscriptReader();
        var second = Assistant(0, 0, 90000);
        File.WriteAllText(_path, Assistant(0, 0, 10000) + second[..40]);
        Assert.AreEqual(10000L, reader.Read(_path).ContextTokens);

        File.AppendAllText(_path, second[40..]);
        Assert.AreEqual(90000L, reader.Read(_path).ContextTokens);
    }

    [TestMethod]
    public void Rewritten_shorter_transcript_is_read_from_the_start()
    {
        var reader = new TranscriptReader();
        File.WriteAllText(_path, Assistant(0, 0, 100000) + Assistant(0, 0, 120000));
        Assert.AreEqual(120000L, reader.Read(_path).ContextTokens);

        File.WriteAllText(_path, Assistant(0, 0, 7000));
        Assert.AreEqual(7000L, reader.Read(_path).ContextTokens);
    }

    [TestMethod]
    public void Long_transcript_is_read_from_its_end()
    {
        // Začátek je delší než čtený konec a končí useknutým řádkem, který se nesmí započítat.
        var filler = Assistant(0, 0, 1) + new string('x', (int)TranscriptReader.FirstReadTailBytes) + "\n";
        File.WriteAllText(_path, filler + Assistant(0, 0, 64000));

        Assert.AreEqual(64000L, new TranscriptReader().Read(_path).ContextTokens);
    }

    // --- Rozpracovaný tah ---

    private static string Line(string time, string type, string body, bool side = false, string extra = "") =>
        $"{{\"type\":\"{type}\",\"isSidechain\":{(side ? "true" : "false")},{extra}\"timestamp\":\"2026-11-26T{time}.000Z\",\"message\":{body}}}\n";
    private static string Reply(string time, string stop) =>
        Line(time, "assistant", $"{{\"role\":\"assistant\",\"stop_reason\":\"{stop}\",\"content\":[]}}");
    private static string Prompt(string time, string text, string extra = "") =>
        Line(time, "user", $"{{\"role\":\"user\",\"content\":\"{text}\"}}", extra: extra);
    private static string ToolResult(string time) =>
        Line(time, "user", "{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\"}]}");
    private static long Ms(string time) => DateTimeOffset.Parse($"2026-11-26T{time}Z").ToUnixTimeMilliseconds();

    [TestMethod]
    public void Finished_reply_closes_the_turn()
    {
        File.WriteAllText(_path, Prompt("10:00:00", "ahoj") + Reply("10:00:05", "end_turn") +
            "{\"type\":\"system\",\"subtype\":\"stop_hook_summary\",\"timestamp\":\"2026-11-26T10:00:06.000Z\"}\n");

        var info = new TranscriptReader().Read(_path);
        Assert.IsFalse(info.TurnOpen);
        Assert.AreEqual(Ms("10:00:05"), info.LastActivity);
    }

    [TestMethod]
    public void Message_after_a_finished_reply_opens_a_turn()
    {
        // Umbrellu probudí zpráva od subsession; hook se o tom dozvědět nemusí.
        File.WriteAllText(_path, Reply("10:00:05", "end_turn") + Prompt("10:01:00", "The session finished"));

        var info = new TranscriptReader().Read(_path);
        Assert.IsTrue(info.TurnOpen);
        Assert.AreEqual(Ms("10:01:00"), info.LastActivity);
    }

    [TestMethod]
    public void Turn_stays_open_while_tools_run()
    {
        var reader = new TranscriptReader();
        File.WriteAllText(_path, Prompt("10:00:00", "udelej to") + Reply("10:00:03", "tool_use"));
        Assert.IsTrue(reader.Read(_path).TurnOpen);

        File.AppendAllText(_path, ToolResult("10:00:09") + Reply("10:00:12", "end_turn"));
        Assert.IsFalse(reader.Read(_path).TurnOpen);
    }

    [TestMethod]
    public void Any_final_reply_closes_the_turn_so_the_next_message_counts_again()
    {
        var reader = new TranscriptReader();
        File.WriteAllText(_path, Prompt("10:00:00", "udelej to") + Reply("10:00:03", "refusal"));
        Assert.IsFalse(reader.Read(_path).TurnOpen);

        File.AppendAllText(_path, Prompt("10:05:00", "The session finished"));
        var info = reader.Read(_path);
        Assert.IsTrue(info.TurnOpen);
        Assert.AreEqual(Ms("10:05:00"), info.LastActivity);
    }

    [TestMethod]
    public void Interrupted_turn_is_closed()
    {
        File.WriteAllText(_path, Prompt("10:00:00", "udelej to") + Reply("10:00:03", "tool_use") +
            Prompt("10:00:04", "[Request interrupted by user]"));

        Assert.IsFalse(new TranscriptReader().Read(_path).TurnOpen);
    }

    [TestMethod]
    public void Local_commands_and_compaction_summary_are_not_a_turn()
    {
        File.WriteAllText(_path, Reply("10:00:05", "end_turn") +
            Prompt("10:01:00", "<command-name>/model</command-name>") +
            Prompt("10:01:01", "<local-command-stdout>Set model</local-command-stdout>") +
            Prompt("10:02:00", "This session is being continued", extra: "\"isCompactSummary\":true,"));

        var info = new TranscriptReader().Read(_path);
        Assert.IsFalse(info.TurnOpen);
        Assert.AreEqual(Ms("10:00:05"), info.LastActivity);
    }

    [TestMethod]
    public void Subagent_activity_does_not_open_the_session_turn()
    {
        File.WriteAllText(_path, Reply("10:00:05", "end_turn") + Line("10:00:09", "user", "{\"role\":\"user\",\"content\":\"x\"}", side: true));

        Assert.IsFalse(new TranscriptReader().Read(_path).TurnOpen);
    }

    [TestMethod]
    [DataRow("done", 1000L, true, "running")]       // tah pracuje i po posledním hlášení hooku (probuzení, auto-compact)
    [DataRow("done", 3000L, true, "done")]          // od hlášení „hotovo“ se v transkriptu nic nestalo
    [DataRow("done", 1000L, false, "done")]         // tah je uzavřený
    [DataRow("attention", 1000L, true, "attention")]
    [DataRow("running", 1000L, false, "running")]
    public void Hook_done_turns_running_while_an_open_turn_keeps_working(string hook, long hookUpdated, bool open, string expected)
    {
        var info = new TranscriptInfo(null, open, LastActivity: 2000);
        Assert.AreEqual(expected, TranscriptReader.LiveState(hook, hookUpdated, info, now: 2500));
    }

    [TestMethod]
    public void Abandoned_open_turn_does_not_keep_the_session_running()
    {
        // Např. session ukončená uprostřed tahu.
        var info = new TranscriptInfo(null, TurnOpen: true, LastActivity: 2000);
        Assert.AreEqual("done", TranscriptReader.LiveState("done", 1000, info, now: 2000 + TranscriptReader.StaleTurnMs));
    }

    [TestMethod]
    [DataRow(0L, ContextLevel.Normal)]
    [DataRow(134_999L, ContextLevel.Normal)]
    [DataRow(135_000L, ContextLevel.Warning)]   // hranice − 10 %
    [DataRow(150_000L, ContextLevel.Warning)]   // dosažení hranice ještě není přesah
    [DataRow(150_001L, ContextLevel.Over)]
    public void Line_turns_yellow_at_boundary_minus_ten_percent_and_red_above_boundary(long tokens, ContextLevel expected)
    {
        Assert.AreEqual(expected, ContextMeter.Level(tokens, 150_000));
    }
}
