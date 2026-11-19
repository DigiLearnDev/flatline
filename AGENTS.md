# FlatLine

Windows AppBar (C#, WPF, .NET 9) ukazující stav sessions Claude Code a 5h/týdenní limity. Popis a instalace: `README.md`.

## Příkazy

- `dotnet build src/ProgresLista -c Release` – sestavení. Běžící `FlatLine.exe` drží soubory v `bin/`, před buildem ho ukonči (`taskkill /IM FlatLine.exe /F`).
- `dotnet test tests/FlatLine.Tests` – testy čisté logiky (MSTest); projekt kompiluje jen vybrané soubory ze `src/`, ne WPF.
- `node hooks/install.js` / `--remove` – přidá / odebere hooky v `~/.claude/settings.json`.

## Struktura

- `src/ProgresLista/AppBar.cs` – registrace AppBaru (`SHAppBarMessage`), souřadnice ve fyzických pixelech (manifest PerMonitorV2).
- `src/ProgresLista/MainWindow.xaml(.cs)` – lišta, načítání stavů, špendlíky, záložka Hotovo.
- `src/ProgresLista/ContextMeter.cs` – zaplnění kontextu z transkriptu `.jsonl` (čte jen přírůstky) a barva čáry kontextu.
- `src/ProgresLista/TitleIndex.cs` – názvy sessions z metadat desktop aplikace (`cliSessionId` → `title`).
- `src/ProgresLista/UsageClient.cs` – limity z `/api/oauth/usage`, obnova OAuth tokenu.
- `src/ProgresLista/BarState.cs` – připnutí, počítadlo a hranice kontextu (`~/.claude/progres-lista-state.json`).
- `hooks/progres-hook.js` – hook: zapisuje `~/.claude/progres-lista/<session_id>.json`. Nesmí nic vypsat na stdout (výstup `UserPromptSubmit` jde do kontextu) a vždy končí kódem 0.

## Agent skills

### Issue tracker

Issues a specifikace jsou v GitHub Issues repa `DigiLearnDev/flatline` (přes `gh`). See `docs/agents/issue-tracker.md`.

### Triage labels

Výchozích pět rolí (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`). See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: `GLOSSARY.md` a `docs/adr/` v kořeni repa (vznikají průběžně). See `docs/agents/domain.md`.
