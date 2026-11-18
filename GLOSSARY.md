# Progres lišta

Lišta na horním okraji obrazovky, která na první pohled ukazuje, jestli některá session Claude běží, je hotová nebo po mně něco chce, a kolik zbývá z limitů.

## Lišta

**Lišta**:
Pruh přes celou šířku horního okraje obrazovky, který si od systému vyhradí místo jako hlavní panel, takže ho okna nepřekrývají.
_Avoid_: panel, toolbar, AppBar (to je jen technika, kterou lišta používá)

**Souhrn**:
Text na levém okraji lišty s počtem sessions, které běží a které čekají.

**Bliknutí**:
Krátké rozsvícení a zhasnutí celé lišty, když přibude nová session nebo když session přejde do stavu hotovo či potřebuje pozornost.
_Avoid_: notifikace, upozornění

## Sessions

**Session**:
Jedna konverzace s Claude Code, ať běží v desktop aplikaci, nebo v CLI.
_Avoid_: agent, okno, úloha

**Položka**:
Zobrazení jedné session na liště: špendlík, puntík stavu, název a jak dlouho je ve stavu.
_Avoid_: dlaždice, chip, karta

**Název session**:
Jméno, které session dostala ručně v levém panelu desktop aplikace; session bez něj se jmenuje podle své složky.
_Avoid_: titulek, label

**Stav**:
To, v jaké fázi session je: běží, potřebuje pozornost, nebo hotovo.
_Avoid_: status

**Běží**:
Stav session, která právě pracuje a nic ode mě nepotřebuje. Šedý puntík.
_Avoid_: aktivní, zpracovává

**Potřebuje pozornost**:
Stav session, která čeká na mé povolení nebo vstup. Žlutá položka.
_Avoid_: čeká, blokovaná, attention

**Hotovo**:
Stav session, která dokončila práci a čeká na další zadání. Zelená položka s modrým puntíkem.
_Avoid_: done, idle, nečinná

**Odebrání**:
Ruční odstranění položky z lišty, typicky u session, která skončila, aniž by to lišta poznala.
_Avoid_: smazání, zavření session

## Třídění

**Špendlík**:
Přepínač u položky, který session připne; připnutý je bíle vyplněný.
_Avoid_: pin, oblíbené

**Připnutá session**:
Session, která je vždy vlevo na liště a nikdy nespadne do záložky Hotovo.
_Avoid_: oblíbená, sledovaná

**Záložka Hotovo**:
Sbalené místo na liště, kam spadnou nepřipnuté sessions, které jsou hotové déle než 10 minut.
_Avoid_: archiv, koš, seznam hotových

**Počítadlo**:
Číslo na záložce Hotovo: kolik sessions do ní spadlo od posledního vynulování.
_Avoid_: badge, čítač

**Vynulování**:
Pravý klik na záložku Hotovo, po kterém počítadlo začne počítat znovu od nuly; sessions v záložce zůstanou.
_Avoid_: reset, smazání

## Limity

**Limit**:
Část předplatného Claude, kterou lze vyčerpat za určité období; lišta ukazuje dva.
_Avoid_: kvóta, usage

**5h okno**:
Limit, který se obnovuje po pěti hodinách.
_Avoid_: session limit, krátký limit

**Týdenní okno**:
Limit, který se obnovuje jednou za týden.
_Avoid_: week, dlouhý limit

**Ukazatel limitu**:
Pruh s procenty, kolik z okna je vyčerpáno; od 70 % oranžový, od 90 % červený.
_Avoid_: progressbar, graf
