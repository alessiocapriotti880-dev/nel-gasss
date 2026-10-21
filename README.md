# PC Optimizer Pro

Ottimizzatore di latenza e frame time per Windows 10/11 (64 bit), basato su misure reali:
ogni intervento è documentato, misurato prima/dopo e annullabile con un clic.

> Stato: sviluppo per moduli. Completato: **modulo 1 – Core** (contratti, giornale, rollback, backup, log).
> Istruzioni complete di compilazione e test arriveranno con il modulo 9.

## Struttura

| Progetto | Contenuto |
|---|---|
| `src/PcOptimizer.Core` | `ITweak`, `TweakEngine` (applicazione transazionale e rollback), `ChangeJournal`, backup `.reg`, log, punto di ripristino, provider Windows (registro, servizi, piano energetico) |
| `tests/PcOptimizer.Core.Tests` | Test unitari con un sistema finto in memoria |

## Compilazione rapida

Richiede .NET 8 SDK.

```
dotnet build
dotnet test
```

## Dove salva i dati

`%ProgramData%\PcOptimizerPro\`
- `journal\changes.jsonl`: giornale delle modifiche (fonte per il rollback)
- `backups\<data>_<intervento>_<id>\original.reg`: valori originali, ripristinabili anche senza l'app
- `logs\pcoptimizer-<data>.log`: log giornaliero (30 giorni)
