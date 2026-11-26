namespace ProgresLista;

public enum SessionState { Running, Attention, Done }

/// <summary>Session, jak ji vidí rozložení lišty. ParentId = session, která ji spustila (umbrella), pokud je na liště.</summary>
public sealed record SessionNode(string Id, string State, long Updated, string? ParentId)
{
    public SessionState OwnState => State switch
    {
        "attention" => SessionState.Attention,
        "running" => SessionState.Running,
        _ => SessionState.Done,
    };
}

/// <summary>
/// Volně stojící session nebo umbrella se všemi svými subsessions (i vnořenými).
/// Umbrella je hotová, jen když jsou nečinné i všechny subsessions; subsession, která potřebuje
/// pozornost, je přímo na liště a umbrella mezitím zůstává šedá (běží).
/// </summary>
public sealed record SessionGroup(SessionNode Root, IReadOnlyList<SessionNode> Children)
{
    public bool IsUmbrella => Children.Count > 0;

    public SessionState State =>
        Root.OwnState == SessionState.Attention ? SessionState.Attention
        : Root.OwnState == SessionState.Done && Children.All(c => c.OwnState == SessionState.Done) ? SessionState.Done
        : SessionState.Running;

    /// <summary>Poslední změna v celé skupině; u hotové skupiny čas, kdy doběhla poslední session.</summary>
    public long Updated => Children.Select(c => c.Updated).Append(Root.Updated).Max();
}

/// <summary>
/// Rozdělení sessions na liště: připnuté, potřebující pozornost (obě části přímo na liště)
/// a záložky Running a Hotovo, které obsahují umbrelly a volně stojící sessions.
/// </summary>
public sealed record BarLayout(
    IReadOnlyList<SessionGroup> Pinned,
    IReadOnlyList<SessionNode> Attention,
    IReadOnlyList<SessionGroup> Running,
    IReadOnlyList<SessionGroup> Done,
    int DoneNew)
{
    public static BarLayout Build(IEnumerable<SessionNode> sessions, ISet<string> pins, long counterResetAt)
    {
        var byId = new Dictionary<string, SessionNode>();
        foreach (var s in sessions) byId[s.Id] = s;

        // Kořen = nejvyšší předek, který je na liště. Session v cyklu vazeb stojí sama za sebe.
        string RootOf(SessionNode s)
        {
            var seen = new HashSet<string> { s.Id };
            var cur = s;
            while (cur.ParentId is { } p && byId.TryGetValue(p, out var parent))
            {
                if (!seen.Add(p)) return s.Id;
                cur = parent;
            }
            return cur.Id;
        }

        var roots = byId.Values.ToDictionary(s => s.Id, RootOf);
        var children = byId.Values.Where(s => roots[s.Id] != s.Id).ToLookup(s => roots[s.Id]);
        SessionGroup GroupOf(SessionNode s) =>
            new(s, roots[s.Id] == s.Id ? children[s.Id].OrderBy(c => c.Id).ToList() : new List<SessionNode>());

        var pinned = byId.Values.Where(s => pins.Contains(s.Id)).Select(GroupOf).OrderBy(g => g.Root.Id).ToList();
        var attention = byId.Values
            .Where(s => !pins.Contains(s.Id) && s.OwnState == SessionState.Attention)
            .OrderBy(s => s.Id).ToList();

        // Do záložek jdou kořeny. Volně stojící session, která je už na liště (připnutá nebo potřebuje
        // pozornost), se neopakuje; umbrella v záložce zůstane vždy, jinak by její subsessions nešly najít.
        var tabbed = byId.Values
            .Where(s => roots[s.Id] == s.Id)
            .Select(GroupOf)
            .Where(g => g.IsUmbrella || (!pins.Contains(g.Root.Id) && g.Root.OwnState != SessionState.Attention))
            .OrderByDescending(g => g.Updated).ToList();
        var running = tabbed.Where(g => g.State != SessionState.Done).ToList();
        var done = tabbed.Where(g => g.State == SessionState.Done).ToList();

        return new BarLayout(pinned, attention, running, done, done.Count(g => g.Updated > counterResetAt));
    }
}
