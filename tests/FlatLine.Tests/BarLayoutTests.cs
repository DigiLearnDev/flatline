using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProgresLista;

namespace FlatLine.Tests;

[TestClass]
public sealed class BarLayoutTests
{
    private static SessionNode S(string id, string state, long updated = 1000, string? parent = null) =>
        new(id, state, updated, parent);

    private static BarLayout Build(IEnumerable<SessionNode> sessions, params string[] pins) =>
        BarLayout.Build(sessions, pins.ToHashSet(), counterResetAt: 0);

    private static string[] Ids(IEnumerable<SessionGroup> groups) => groups.Select(g => g.Root.Id).ToArray();

    [TestMethod]
    public void Standalone_sessions_go_to_pinned_attention_running_or_done()
    {
        var layout = Build(new[]
        {
            S("pin", "running"), S("att", "attention"), S("run", "running"), S("done", "done"),
        }, "pin");

        CollectionAssert.AreEqual(new[] { "pin" }, Ids(layout.Pinned));
        CollectionAssert.AreEqual(new[] { "att" }, layout.Attention.Select(s => s.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "run" }, Ids(layout.Running));
        CollectionAssert.AreEqual(new[] { "done" }, Ids(layout.Done));
    }

    [TestMethod]
    public void Subsessions_are_listed_under_their_umbrella_not_on_their_own()
    {
        var layout = Build(new[]
        {
            S("umb", "running"), S("a", "running", parent: "umb"), S("b", "running", parent: "umb"),
        });

        var umbrella = layout.Running.Single();
        Assert.AreEqual("umb", umbrella.Root.Id);
        CollectionAssert.AreEquivalent(new[] { "a", "b" }, umbrella.Children.Select(c => c.Id).ToArray());
        Assert.IsTrue(umbrella.IsUmbrella);
    }

    [TestMethod]
    public void Umbrella_is_done_only_when_all_its_subsessions_are_idle()
    {
        var busy = Build(new[] { S("umb", "done"), S("a", "done", parent: "umb"), S("b", "running", parent: "umb") });
        Assert.AreEqual(SessionState.Running, busy.Running.Single().State);
        Assert.AreEqual(0, busy.Done.Count);

        var idle = Build(new[] { S("umb", "done"), S("a", "done", parent: "umb"), S("b", "done", parent: "umb") });
        Assert.AreEqual(SessionState.Done, idle.Done.Single().State);
        Assert.AreEqual(0, idle.Running.Count);
    }

    [TestMethod]
    public void Subsession_needing_attention_shows_on_the_bar_and_its_umbrella_stays_gray()
    {
        var layout = Build(new[] { S("umb", "done"), S("a", "attention", parent: "umb") });

        CollectionAssert.AreEqual(new[] { "a" }, layout.Attention.Select(s => s.Id).ToArray());
        Assert.AreEqual(SessionState.Running, layout.Running.Single().State);
    }

    [TestMethod]
    public void Umbrella_on_the_bar_keeps_its_subsessions_reachable_from_its_tab()
    {
        var attention = Build(new[] { S("umb", "attention"), S("a", "running", parent: "umb") });
        CollectionAssert.AreEqual(new[] { "umb" }, attention.Attention.Select(s => s.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "a" }, attention.Running.Single().Children.Select(c => c.Id).ToArray());

        var pinned = Build(new[] { S("umb", "done"), S("a", "done", parent: "umb") }, "umb");
        CollectionAssert.AreEqual(new[] { "umb" }, Ids(pinned.Pinned));
        CollectionAssert.AreEqual(new[] { "a" }, pinned.Done.Single().Children.Select(c => c.Id).ToArray());
    }

    [TestMethod]
    public void Nested_subsessions_belong_to_the_topmost_umbrella_on_the_bar()
    {
        var layout = Build(new[]
        {
            S("orphan", "running", parent: "not-on-bar"),
            S("umb", "running"), S("a", "running", parent: "umb"), S("a1", "running", parent: "a"),
        });

        CollectionAssert.AreEquivalent(new[] { "orphan", "umb" }, Ids(layout.Running));
        var umbrella = layout.Running.Single(g => g.Root.Id == "umb");
        CollectionAssert.AreEquivalent(new[] { "a", "a1" }, umbrella.Children.Select(c => c.Id).ToArray());
    }

    [TestMethod]
    public void Sessions_spawning_each_other_in_a_cycle_do_not_hang()
    {
        var layout = Build(new[] { S("x", "running", parent: "y"), S("y", "running", parent: "x") });
        Assert.AreEqual(2, layout.Running.Sum(g => 1 + g.Children.Count));
    }

    [TestMethod]
    public void Done_counter_shows_how_many_are_listed_and_how_many_arrived_since_reset()
    {
        var sessions = Enumerable.Range(0, 20).Select(i => S("d" + i, "done", updated: 1000 + i)).ToList();

        var layout = BarLayout.Build(sessions, new HashSet<string>(), counterResetAt: 1018);

        Assert.AreEqual(20, layout.Done.Count);
        Assert.AreEqual(1, layout.DoneNew);   // jen d19 dokončila po vynulování
    }
}
