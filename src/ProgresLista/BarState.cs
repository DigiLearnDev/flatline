using System.IO;
using System.Text.Json;

namespace ProgresLista;

/// <summary>
/// Trvalý stav lišty: připnuté sessions, čas posledního vynulování počítadla Hotovo a limit kontextu.
/// Leží vedle složky se stavy sessions, aby ho lišta nečetla jako session.
/// </summary>
public sealed class BarState
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "progres-lista-state.json");

    public HashSet<string> Pins { get; set; } = new();
    public long CounterResetAt { get; set; }
    /// <summary>Počet tokenů, při kterém je čára zaplnění kontextu plná; nad ním zčervená.</summary>
    public long ContextLimit { get; set; } = 150_000;

    public static BarState Load()
    {
        try { return JsonSerializer.Deserialize<BarState>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this)); } catch { }
    }
}
