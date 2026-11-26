# FlatLine

Lišta na horním okraji obrazovky, která na první pohled ukazuje, jestli některá session Claude běží, je hotová nebo po mně něco chce, a kolik zbývá z limitů.

## Lišta

**Lišta**:
Pruh přes celou šířku horního okraje obrazovky, který si od systému vyhradí místo jako hlavní panel, takže ho okna nepřekrývají.
_Avoid_: panel, toolbar, AppBar (to je jen technika, kterou lišta používá)

**Část Připnuté**:
Levá část lišty za záložkami se všemi připnutými sessions.
_Avoid_: oblíbené, pinned

**Část Pozornost**:
Pravá část lišty, oddělená od Připnutých svislou čarou, se všemi nepřipnutými sessions, které potřebují pozornost (i subsessions).
_Avoid_: fronta, needs attention

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

**Umbrella**:
Session, která spustila další sessions (dispečer). V záložce ukazuje počet subsessions a šipku; po najetí myší se vpravo rozbalí sloupec s nimi. Je hotová, jen když jsou nečinné i všechny její subsessions; jinak je šedá.
_Avoid_: rodič, dispečer, orchestrátor

**Subsession**:
Session, kterou spustila jiná session na liště (podle `spawnedFrom` v metadatech desktop aplikace). Vnořené subsessions patří k nejvyšší umbrelle na liště; session odpojená od rodiče (`lineageDetached`) stojí sama.
_Avoid_: dítě, podřízená session, side session

**Volně stojící session**:
Session, která není umbrellou ani subsession žádné session na liště.
_Avoid_: samostatná, osamocená

**Odebrání**:
Ruční odstranění položky z lišty, typicky u session, která skončila, aniž by to lišta poznala.
_Avoid_: smazání, zavření session

## Třídění

**Špendlík**:
Přepínač u položky, který session připne; připnutý je bíle vyplněný.
_Avoid_: pin, oblíbené

**Připnutá session**:
Session, která je vždy v části Připnuté a nikdy nespadne do záložky Hotovo ani Running.
_Avoid_: oblíbená, sledovaná

**Záložka Hotovo**:
Sbalené místo na liště s hotovými umbrellami a volně stojícími sessions, které nejsou připnuté. Session sem spadne hned, jak doběhne.
_Avoid_: archiv, koš, seznam hotových

**Záložka Running**:
Sbalené místo na liště s umbrellami a volně stojícími sessions, které ještě nejsou hotové (kromě těch v části Pozornost a připnutých).
_Avoid_: běžící, seznam běžících

**Počítadlo**:
Číslo na záložce: kolik položek je v jejím seznamu.
_Avoid_: badge, čítač

**Nové**:
Modrý odznak u záložky Hotovo: kolik položek v ní doběhlo od posledního vynulování.
_Avoid_: nepřečtené, badge

**Vynulování**:
Pravý klik na záložku Hotovo, po kterém odznak Nové zmizí a počítá znovu od nuly; sessions v záložce zůstanou.
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

## Kontext

**Zaplnění kontextu**:
Kolik tokenů má session právě v kontextu: vstupní tokeny včetně cache u poslední odpovědi hlavního agenta. Subagenti se nepočítají, mají vlastní kontext.
_Avoid_: spotřeba tokenů (to je součet za celou session), usage

**Čára kontextu**:
Tenká čára na spodní hraně položky, jejíž délka ukazuje zaplnění kontextu vůči hranici kontextu. Od hranice −10 % je žlutá, nad hranicí červená.
_Avoid_: progressbar, ukazatel limitu (to je pruh limitu vpravo)

**Hranice kontextu**:
Počet tokenů, při kterém je čára kontextu plná (výchozí 150k). Mění se pravým klikem na volné místo lišty.
_Avoid_: limit kontextu (Limit je část předplatného)
