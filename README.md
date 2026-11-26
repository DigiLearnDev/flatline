<h1>
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/logo-dark.png">
    <img src="assets/logo-light.png" alt="FlatLine" height="72">
  </picture>
</h1>

Tenká lišta na horním okraji obrazovky Windows, která ukazuje stav všech běžících sessions Claude Code a vyčerpání 5h a týdenního limitu. Stačí mrknout nahoru a vím, jestli po mně některý agent něco chce.

![FlatLine](docs/screenshot.png)

## Proč

Několik orchestračních agentů Claude zpracovává tickety z GitHubu. Většinu času jen čekám, až doběhnou, nebo až budou něco chtít. Bez lišty musím pořád přepínat okna a kontrolovat jednotlivé sessions.

## Co umí

- **Chová se jako hlavní panel Windows.** Lišta se registruje jako AppBar (`SHAppBarMessage`) a rezervuje si horních 30 px. Maximalizovaná okna končí pod ní a nic ji nepřekryje.
- **Stav každé session:** šedý puntík běží, žlutá položka potřebuje pozornost (se zvukem), zelená položka s modrým puntíkem je hotová. Při každé změně stavu lišta krátce blikne.
- **Klik na session** ji otevře v desktop aplikaci Claude (sessions z CLI odkaz nemají).
- **Názvy z levého panelu** desktop aplikace Claude. Sessions z CLI mají název složky.
- **Rozdělení lišty:** vlevo záložky **Hotovo** a **Running**, za nimi připnuté sessions a za svislou čarou sessions, které potřebují pozornost.
- **Špendlík:** připnuté sessions jsou vždy v části připnutých a nikdy se neschovají do záložek.
- **Záložka Hotovo:** sem spadne každá nepřipnutá hotová session. Číslo = kolik jich je v seznamu, modrý odznak = kolik doběhlo od vynulování (pravý klik).
- **Záložka Running:** nepřipnuté sessions, které ještě pracují.
- **Umbrelly:** session, která spustila další sessions, je v záložce jedna položka s počtem subsessions. Najetí myší rozbalí vpravo sloupec se subsessions. Umbrella je hotová, až když jsou nečinné i všechny subsessions; subsession, která potřebuje pozornost, je rovnou na liště.
- **Zaplnění kontextu:** tenká čára na spodní hraně položky. Plná čára = hranice kontextu (výchozí 150k tokenů), od hranice −10 % je žlutá, nad hranicí červená. Hranici změníš pravým klikem na volné místo lišty. Přesný počet je v tooltipu.
- **Limity:** ukazatele 5h a týdenního okna, modré / od 70 % oranžové / od 90 % červené.
- Pravý klik na session ji odebere z lišty (např. když skončila bez `SessionEnd`).

## Instalace

Potřebuješ Windows 10/11, [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) a Node.js.

1. Stáhni zip z [Releases](../../releases) a rozbal ho.
2. Nainstaluj hooky Claude Code (přidají se do `~/.claude/settings.json`, záloha vedle):
   ```
   node hooks/install.js
   ```
3. Spusť `FlatLine.exe`.
4. Pro ukazatele limitů se jednou přihlas v CLI: `claude` → `/login`.

Odinstalace hooků: `node hooks/install.js --remove`.

## Jak to funguje

- Hooky Claude Code (`hooks/progres-hook.js`) zapisují stav každé session do `~/.claude/progres-lista/<session_id>.json`. Lišta složku sleduje.
- Názvy a vazby umbrella → subsession (`spawnedFrom`, `lineageDetached`) čte z metadat desktop aplikace v `%LOCALAPPDATA%\Claude-Data`. Není to veřejné rozhraní, po aktualizaci aplikace se může změnit; lišta pak ukáže název složky.
- Limity čte z `api.anthropic.com/api/oauth/usage` tokenem z `~/.claude/.credentials.json`. Prošlý token obnoví a uloží zpět. Token jde jen na Anthropic.
- Zaplnění kontextu čte z transkriptu session (`~/.claude/projects/…/<session_id>.jsonl`), jehož cestu zapisuje hook. Čte jen nové řádky. Formát transkriptu není veřejné rozhraní; když se změní, čára se neukáže.
- Připnutí, počítadlo a hranice kontextu jsou v `~/.claude/progres-lista-state.json`.

## Vývoj

```
dotnet build src/ProgresLista -c Release
dotnet test tests/FlatLine.Tests
```

Příspěvky zvenku nepřijímám, viz [CONTRIBUTING.md](CONTRIBUTING.md).

## Licence

[CC BY-NC-SA 4.0](LICENSE)
