using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProgresLista;

namespace FlatLine.Tests;

[TestClass]
public sealed class ContextMeterTests
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

        Assert.AreEqual(40503L, new ContextMeter().Read(_path));
    }

    [TestMethod]
    public void Subagent_messages_do_not_count_toward_the_session_context()
    {
        File.WriteAllText(_path,
            Assistant(1, 0, 30000) +
            Assistant(1, 0, 5000).Replace("\"isSidechain\":false", "\"isSidechain\":true"));

        Assert.AreEqual(30001L, new ContextMeter().Read(_path));
    }

    [TestMethod]
    public void Missing_transcript_has_no_context_value()
    {
        Assert.IsNull(new ContextMeter().Read(_path));
    }

    [TestMethod]
    public void Message_written_in_parts_is_counted_once_it_is_complete()
    {
        var meter = new ContextMeter();
        var second = Assistant(0, 0, 90000);
        File.WriteAllText(_path, Assistant(0, 0, 10000) + second[..40]);
        Assert.AreEqual(10000L, meter.Read(_path));

        File.AppendAllText(_path, second[40..]);
        Assert.AreEqual(90000L, meter.Read(_path));
    }

    [TestMethod]
    public void Rewritten_shorter_transcript_is_read_from_the_start()
    {
        var meter = new ContextMeter();
        File.WriteAllText(_path, Assistant(0, 0, 100000) + Assistant(0, 0, 120000));
        Assert.AreEqual(120000L, meter.Read(_path));

        File.WriteAllText(_path, Assistant(0, 0, 7000));
        Assert.AreEqual(7000L, meter.Read(_path));
    }

    [TestMethod]
    [DataRow(0L, ContextLevel.Normal)]
    [DataRow(134_999L, ContextLevel.Normal)]
    [DataRow(135_000L, ContextLevel.Warning)]   // limit − 10 %
    [DataRow(150_000L, ContextLevel.Warning)]   // dosažení limitu ještě není přesah
    [DataRow(150_001L, ContextLevel.Over)]
    public void Line_turns_yellow_at_limit_minus_ten_percent_and_red_above_limit(long tokens, ContextLevel expected)
    {
        Assert.AreEqual(expected, ContextMeter.Level(tokens, 150_000));
    }
}
