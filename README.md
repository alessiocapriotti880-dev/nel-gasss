# PC Optimizer Pro

Ottimizzatore di latenza e frame time per Windows 10/11 (64 bit), basato su misure reali:
ogni intervento è documentato, misurato prima/dopo e annullabile con un clic.

> Stato: sviluppo per moduli.
> Completati: **1 – Core** (interventi transazionali, giornale, rollback, backup, log),
> **2 – Hardware** (rilevamento, classificazione in fascia, osservazioni) e
> **3 – Analisi del sistema** (configurazione di Windows in sola lettura, modifiche rispetto ai default).
> Istruzioni complete di compilazione e test arriveranno con il modulo 9.

## Struttura

| Progetto | Contenuto |
|---|---|
| `src/PcOptimizer.Core` | `ITweak`, `TweakEngine` (applicazione transazionale e rollback), `ChangeJournal`, backup `.reg`, log, punto di ripristino, provider Windows (registro, servizi, piano energetico), `Finding` |
| `src/PcOptimizer.Hardware` | Rilevamento hardware (`Windows/`), parser puri (`Parsing/`), tabelle aggiornabili (`Data/*.json`), valutazione: fascia, stima XMP, canali RAM, osservazioni (`Assessment/`) |
| `src/PcOptimizer.Analysis` | Analisi della configurazione (`Windows/`): servizi, avvio, attività, driver, processi, alimentazione, timer, interrupt/MSI, HAGS, impostazioni di gioco, sicurezza; valutazione pura (`Evaluation/`) e catalogo del software noto (`Data/known-software.json`) |
| `src/PcOptimizer.Cli` | `pcopt`, strumento a riga di comando per provare i moduli su un PC reale |
| `tests/*` | Test unitari (girano anche fuori da Windows: le API di sistema sono isolate) |

## Compilazione rapida

Richiede .NET 8 SDK.

```
dotnet build
dotnet test
```

## Provare la scansione hardware su Windows

Sola lettura, non modifica nulla e non richiede privilegi di amministratore.

```
dotnet run --project src/PcOptimizer.Cli -- scan
dotnet run --project src/PcOptimizer.Cli -- scan --json report.json
```

Eseguibile singolo da copiare su altri PC:

```
dotnet publish src/PcOptimizer.Cli -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
publish\pcopt.exe scan
```

Il report mostra fascia e punteggio con il dettaglio per componente, i dati rilevati e le osservazioni
(es. monitor sotto la frequenza massima, monitor collegato alla scheda madre, RAM in canale singolo,
XMP non attivo, microcode Intel 13ª/14ª gen da aggiornare). Con `--json` salva tutti i dati grezzi:
utile per segnalare componenti non riconosciuti.

## Provare l'analisi del sistema su Windows

Anche questa è in sola lettura. Da un terminale **come amministratore** (senza privilegi mancano
configurazione di avvio, TPM e parte delle attività pianificate):

```
dotnet run --project src/PcOptimizer.Cli -- analyze --json analisi.json
```

Mostra piano energetico, timer, modalità degli interrupt (MSI/line-based), impostazioni di gioco,
sicurezza, software riconosciuto (overlay, launcher, RGB, anti-cheat, driver vulnerabili) e le
impostazioni diverse dai valori predefiniti di Windows, con l'effetto noto di ciascuna
(nessun effetto, da misurare, sfavorevole). Per i processi legge solo nome e memoria
dall'elenco di sistema: non apre handle e non interagisce con giochi o anti-cheat.

## Tabelle dati

`src/PcOptimizer.Hardware/Data/` contiene architetture CPU, indici GPU, codici prodotto RAM, chipset
e parametri della classificazione. Sono incorporate nell'eseguibile; una versione aggiornata in
`<cartella dell'eseguibile>\data\` (o indicata con `--data`) ha la precedenza, senza ricompilare.
Gli indici di prestazione sono **approssimati** e servono solo a collocare il PC in una fascia.

## Dove salva i dati

`%ProgramData%\PcOptimizerPro\`
- `journal\changes.jsonl`: giornale delle modifiche (fonte per il rollback)
- `backups\<data>_<intervento>_<id>\original.reg`: valori originali, ripristinabili anche senza l'app
- `logs\pcoptimizer-<data>.log`: log giornaliero (30 giorni)
