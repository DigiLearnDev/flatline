# Session Bar – vize

## Problém

Několik orchestračních agentů Claude zpracovává tickety z GitHubu. Většinu času jen čekám, až doběhnou, nebo až po mně něco budou chtít. Abych to zjistil, musím pořád přepínat okna a kontrolovat jednotlivé sessions.

## Řešení

Tenká lišta na horním okraji obrazovky. Ukazuje všechny spuštěné sessions Claude a jejich stav:

- 🔵 **běží**
- 🟠 **potřebuje pozornost** (čeká na povolení nebo na vstup)
- 🟢 **hotovo**

Jednou očí mrknu nahoru a vím, jestli mám něco udělat.

## Klíčové vlastnosti

- **Chová se jako hlavní panel Windows.** Lišta se zaregistruje jako AppBar (`SHAppBarMessage`) a rezervuje si horní okraj obrazovky. Ostatní okna ji nepřekryjí a maximalizovaná okna končí pod ní.
- **Stav přes Claude Code hooks.** `UserPromptSubmit`, `Notification`, `Stop` a `SessionEnd` zapisují stav každé session do souboru a lišta složku sleduje. Nic se nemusí dotazovat ani pollovat.
- **U každé session název** (repo nebo ticket), ať je jasné, která je která.

## Otevřené otázky

- Chování při fullscreenu (video na celou obrazovku): zůstat navrchu, nebo se schovat a ukázat se jen při stavu 🟠?
- Klik na položku přepne na danou session?
- Zvukové nebo vizuální upozornění při stavu 🟠?
- Technologie: C# (WPF/WinForms) jako první volba.
