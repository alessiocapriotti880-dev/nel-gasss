# PC Optimizer Pro

Ottimizzatore di latenza e frame time per Windows 10/11 (64 bit), basato su misure reali:
ogni intervento è documentato, misurato prima/dopo e annullabile con un clic.

> Stato: sviluppo per moduli.
> Completati: **1 – Core** (interventi transazionali, giornale, rollback, backup, log),
> **2 – Hardware** (rilevamento, classificazione in fascia, osservazioni),
> **3 – Analisi del sistema** (configurazione di Windows in sola lettura, modifiche rispetto ai default),
> **4 – Benchmark** (DPC/ISR via ETW, ritardo del timer, frame time con PresentMon, rete, avvio, sensori, confronto statistico),
> **5 – Interventi** (catalogo per fascia costruito dai dati reali, indicazioni guidate, manutenzione con anteprima),
> **6 – Sessione di ottimizzazione** (punto di ripristino obbligatorio, misura prima/dopo, decisione automatica, ripresa dopo il riavvio, rollback),
> **7 – Overclock e undervolt guidati** (checklist del BIOS per produttore, limiti di sicurezza fissi, procedure a passi, test di stabilità CPU/RAM/GPU/VRAM) e
> **8 – Interfaccia grafica** (WPF: panoramica, analisi, misure con grafici prima/dopo, ottimizzazione con interruttori e badge di rischio, overclock e undervolt, registro).
> Istruzioni complete di compilazione e test arriveranno con il modulo 9.

## Struttura

| Progetto | Contenuto |
|---|---|
| `src/PcOptimizer.Core` | `ITweak`, `TweakEngine` (applicazione transazionale e rollback), `ChangeJournal`, backup `.reg`, log, punto di ripristino, provider Windows (registro, servizi, piano energetico), `Finding` |
| `src/PcOptimizer.Hardware` | Rilevamento hardware (`Windows/`), parser puri (`Parsing/`), tabelle aggiornabili (`Data/*.json`), valutazione: fascia, stima XMP, canali RAM, osservazioni (`Assessment/`) |
| `src/PcOptimizer.Analysis` | Analisi della configurazione (`Windows/`): servizi, avvio, attività, driver, processi, alimentazione, timer, interrupt/MSI, HAGS, impostazioni di gioco, sicurezza; valutazione pura (`Evaluation/`) e catalogo del software noto (`Data/known-software.json`) |
| `src/PcOptimizer.Benchmark` | Misure (`Windows/`): sessione ETW del kernel per DPC/ISR, timer ad alta risoluzione, PresentMon, ping, avvio, uso CPU, sensori (NVML, D3DKMT, HWiNFO); calcolo puro (`Statistics/`, `Latency/`, `Frames/`, `Comparison/`): percentili, Mann-Whitney, punteggio, confronto prima/dopo |
| `src/PcOptimizer.Tweaks` | Catalogo degli interventi (`Catalog/`): alimentazione, servizi, avvio, attività, privacy, effetti visivi, impostazioni di gioco, monitor, MSI, rete, archiviazione, sicurezza, ripristino dei default, giochi; indicazioni guidate (`Guidance/`); manutenzione (`Maintenance/`) |
| `src/PcOptimizer.Optimization` | Piano (`Planning/`), giudizio prima/dopo (`Judging/`), sessione con stato persistente e ripresa dopo il riavvio (`Session/`), servizi Windows (`Windows/`) |
| `src/PcOptimizer.Tuning` | Checklist del BIOS (`Bios/`, `Data/bios-menus.json`), limiti di sicurezza (`Safety/`), procedure guidate e registro dei valori provati (`Guides/`), test di stabilità (`Stability/`; GPU con Direct3D 11 in `Windows/`) |
| `src/PcOptimizer.Presentation` | View model MVVM (CommunityToolkit.Mvvm) e dati dei grafici, provati senza Windows; `Services/WindowsOptimizerServices` collega tutti i moduli |
| `src/PcOptimizer.App` | Applicazione WPF `PcOptimizerPro.exe` (richiede l'amministratore): viste, tema, grafici |
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

## Misurare (benchmark)

Da un terminale **come amministratore** (la misura DPC/ISR usa una sessione ETW del kernel, come LatencyMon):

```
dotnet run --project src/PcOptimizer.Cli -- benchmark --label "prima"
dotnet run --project src/PcOptimizer.Cli -- benchmark list
dotnet run --project src/PcOptimizer.Cli -- compare --before <id> --after <id>
```

- **Latenza** (60 s, non usare il PC): durata di DPC e ISR per secondo e per driver, ritardo di risveglio di un
  timer ad alta risoluzione da 1 ms, uso CPU e processi più attivi (dall'elenco di sistema, senza aprire processi),
  sensori. `--load` ripete la misura con un carico CPU sintetico.
- **Rete**: 200 ping a 10 Hz verso il router e verso `1.1.1.1` (`--ping <host>` o `--ping none`): mediana, P95, jitter, perdita.
- **Avvio**: tempi degli ultimi avvii registrati da Windows (evento 100 di Diagnostics-Performance).
- **Gioco**: `benchmark frames --process cs2.exe --seconds 60` con il gioco già avviato. Serve
  [PresentMon](https://github.com/GameTechDev/PresentMon/releases) (strumento open source di Intel che legge gli eventi
  ETW di DirectX: non si inietta nel gioco ed è compatibile con gli anti-cheat) nella cartella `tools` accanto
  all'app o in `%ProgramData%\PcOptimizerPro\tools`. In alternativa `--csv` importa un file di PresentMon, FrameView o CapFrameX.
- **Sensori**: GPU da NVML (NVIDIA) e dal kernel grafico di Windows (tutte le marche); CPU (temperatura, potenza, Vcore,
  throttling) da [HWiNFO](https://www.hwinfo.com) se aperto con "Shared Memory Support". L'app non installa driver propri.

Il **punteggio di latenza** (0-100) è indicativo e serve a confrontare misure dello stesso PC.
Il **confronto** dichiara "migliorato" o "peggiorato" solo se la differenza è statisticamente significativa
(Mann-Whitney sui valori per secondo, p < 0,05), supera la soglia minima della grandezza e supera la variabilità
osservata fra misure "prima" ripetute: nessun FPS promesso, solo differenze misurate.

## Piano degli interventi

```
dotnet run --project src/PcOptimizer.Cli -- games add "C:\Giochi\cs2\game\bin\win64\cs2.exe" --name "Counter-Strike 2"
dotnet run --project src/PcOptimizer.Cli -- plan
```

Mostra, sezione per sezione, gli interventi possibili su questo PC con lo stato attuale (già a posto / da applicare),
il rischio, l'evidenza (documentato, dipende dal sistema, da verificare con la misura, ripristino, privacy), se richiede
il riavvio e se l'effetto è misurabile; `[x]` indica quelli consigliati per la fascia. Gli interventi nascono dai dati
reali: servizi e programmi in avvio riconosciuti, dispositivi con interrupt a linea, parametri che il driver di rete
dichiara, impostazioni lasciate da altri ottimizzatori (con il ripristino del valore di Windows).

Regole del catalogo, verificate dai test:
- non tocca mai servizi, attività o chiavi di Windows Update, Defender, anti-cheat, launcher dei giochi, aggiornamenti dei browser;
- gli interventi che riducono una protezione (integrità della memoria) non sono mai preselezionati e mostrano un avviso esplicito;
- le impostazioni per gioco sono standard di Windows (preferenza GPU, compatibilità, priorità all'avvio): nessun accesso al processo del gioco;
- ogni intervento si applica e si annulla tramite il motore transazionale (testato sull'intero catalogo).

## Applicare e annullare

```
dotnet run --project src/PcOptimizer.Cli -- apply --recommended
dotnet run --project src/PcOptimizer.Cli -- apply --ids power.plan.high-performance,gaming.background-recording-off
dotnet run --project src/PcOptimizer.Cli -- session status
dotnet run --project src/PcOptimizer.Cli -- rollback --all
```

Una sessione:
1. ripristina eventuali modifiche rimaste a metà da una chiusura imprevista;
2. crea un **punto di ripristino** (Windows ne permette uno ogni 24 ore: se ne esiste uno recente lo riusa e lo dice).
   Senza punto di ripristino la sessione si ferma e **non modifica nulla**;
3. misura il sistema (2 misure, per stimare la variabilità);
4. applica gli interventi senza riavvio **uno alla volta** (giornale e backup `.reg` prima di ogni scrittura), rimisura e decide:
   - peggiora una grandezza → annullato;
   - migliora (statisticamente e oltre la soglia) → tenuto;
   - nessuna differenza → tenuto solo se il comportamento è documentato, altrimenti annullato;
   - non misurabile (privacy, frequenza del monitor) → tenuto come scelto;
5. applica insieme quelli che richiedono il riavvio e crea un'attività pianificata che, al prossimo accesso, attende
   3 minuti, misura e decide allo stesso modo; l'attività si cancella da sola.

`--no-measure` applica senza misure né annullamenti automatici. Gli interventi che riducono la sicurezza richiedono
`--confirm-security <id>`. `pcopt history` mostra ogni impostazione toccata con il valore originale; i backup `.reg`
in `%ProgramData%\PcOptimizerPro\backups` permettono il ripristino anche senza l'app.

## Overclock e undervolt (modalità guidata)

```
dotnet run --project src/PcOptimizer.Cli -- tune bios
dotnet run --project src/PcOptimizer.Cli -- tune methods
dotnet run --project src/PcOptimizer.Cli -- tune step cpu.intel-offset
dotnet run --project src/PcOptimizer.Cli -- tune test gpu --minutes 10
```

L'app **non scrive mai nel BIOS, non flasha firmware e non cambia frequenze o tensioni**: indica cosa impostare e dove,
poi verifica.
- **Checklist del BIOS** con i percorsi dei menu di ASUS, MSI, Gigabyte, ASRock e Biostar (`Data/bios-menus.json`):
  aggiornamento del BIOS, XMP/EXPO, Resizable BAR, profilo Intel Default Settings, TPM/Secure Boot, C-state, ventole.
- **Intel Core 13ª/14ª gen (Raptor Lake)**: se il microcode è precedente a 0x12B l'undervolt è bloccato e la prima
  voce è l'aggiornamento del BIOS (problema di degrado "Vmin shift").
- **Procedure a passi**: undervolt CPU Intel (offset, passi di -10 mV fino a -100 mV) e AMD (Curve Optimizer, passi di
  5 fino a -30), undervolt GPU sulla curva tensione/frequenza (passi di 25 mV), overclock del core (+15 MHz) e della
  memoria della GPU (+50 MHz) senza mai aumentare la tensione, profilo XMP/EXPO. Ogni valore viene registrato prima del
  test: se il PC si blocca, al riavvio il passo risulta "crash". Valore finale = ultimo stabile meno un passo di margine.
- **Test di stabilità**: CPU (calcoli deterministici interi, vettoriali e SHA-256 confrontati con il risultato atteso),
  RAM (schemi scritti e riletti su blocchi da 64 MB), GPU (shader di calcolo verificato dalla CPU) e VRAM, più gli
  errori hardware WHEA registrati da Windows. Il test si interrompe da solo oltre i limiti fissi: CPU 100 °C (o 95 °C
  per 10 s), GPU 90 °C, hotspot GPU 105 °C. Per le temperature della CPU serve HWiNFO con "Shared Memory Support".
- Undervolt della CPU solo dal BIOS: con "Undervolt Protection" attiva gli strumenti di Windows non hanno effetto.

## Tabelle dati

`src/PcOptimizer.Hardware/Data/` contiene architetture CPU, indici GPU, codici prodotto RAM, chipset
e parametri della classificazione. Sono incorporate nell'eseguibile; una versione aggiornata in
`<cartella dell'eseguibile>\data\` (o indicata con `--data`) ha la precedenza, senza ricompilare.
Gli indici di prestazione sono **approssimati** e servono solo a collocare il PC in una fascia.
`src/PcOptimizer.Benchmark/Data/known-drivers.json` dà nome e consiglio per i driver con DPC/ISR lunghi.

## Dove salva i dati

`%ProgramData%\PcOptimizerPro\`
- `journal\changes.jsonl`: giornale delle modifiche (fonte per il rollback)
- `backups\<data>_<intervento>_<id>\original.reg`: valori originali, ripristinabili anche senza l'app
- `logs\pcoptimizer-<data>.log`: log giornaliero (30 giorni)
- `benchmarks\<data>_<etichetta>_<id>.json`: sessioni di misura
- `sessions\current.json` e storico: sessioni di ottimizzazione
- `profiles\games.json`: giochi indicati dall'utente
- `profiles\tuning.json`: procedure di overclock/undervolt con i valori provati e gli esiti
