using PcOptimizer.Analysis.Model;
using PcOptimizer.Core.Model;
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Hardware.Model;
using PcOptimizer.Tweaks.Catalog;
using PcOptimizer.Tweaks.Model;

namespace PcOptimizer.Tweaks.Guidance;

/// <summary>
/// Indicazione guidata: impostazioni che l'app non modifica da sola (pannelli dei driver, programmi di terze parti,
/// reinstallazione dei driver) e che l'utente esegue seguendo i passaggi.
/// </summary>
public sealed record GuidanceItem(
    string Id,
    string Title,
    TweakGroup Group,
    string Summary,
    IReadOnlyList<string> Steps,
    string? Warning = null,
    Risk Risk = Risk.Low);

public static class GuidanceCatalog
{
    public static IReadOnlyList<GuidanceItem> Build(TweakContext context)
    {
        var items = new List<GuidanceItem>();
        var profile = context.Profile();
        var vendors = profile.Gpus.Where(g => g.IsPhysicalPciDevice || g.Vendor != GpuVendor.Microsoft).Select(g => g.Vendor).ToHashSet();

        if (vendors.Contains(GpuVendor.Nvidia))
        {
            items.Add(new("gpu.nvidia.panel", "Pannello di controllo NVIDIA", TweakGroup.Gpu,
                "Impostazioni del driver che riducono la latenza. Meglio nel profilo del singolo gioco che in quello globale.",
                [
                    "Apri Pannello di controllo NVIDIA > Gestisci impostazioni 3D > Impostazioni programma e scegli il gioco.",
                    "Se il gioco ha NVIDIA Reflex, attivalo nel gioco (\"On + Boost\") e lascia \"Modalità bassa latenza\" su Off: Reflex la sostituisce.",
                    "Senza Reflex: \"Modalità bassa latenza\" su On (Ultra solo se la GPU è sempre al 99%).",
                    "\"Modalità di gestione risparmio energia\": \"Prediligi prestazioni massime\" solo per il gioco (a livello globale aumenta i consumi a riposo).",
                    "Con monitor G-SYNC: G-SYNC attivo, \"Sincronizzazione verticale\" On nel pannello, Off nel gioco, limite FPS 3-4 sotto la frequenza del monitor.",
                    "Senza G-SYNC, per il competitivo: \"Sincronizzazione verticale\" Off.",
                    "\"Frequenza di aggiornamento preferita\": \"La più alta disponibile\".",
                ]));
        }

        if (vendors.Contains(GpuVendor.Amd))
        {
            items.Add(new("gpu.amd.panel", "AMD Software: Adrenalin Edition", TweakGroup.Gpu,
                "Impostazioni del driver AMD per la latenza, nel profilo del gioco.",
                [
                    "Apri AMD Software > Gioco > scegli il gioco.",
                    "Radeon Anti-Lag: attivo (o Anti-Lag 2 se il gioco lo integra).",
                    "Radeon Chill: disattivo (limita gli FPS per risparmiare energia).",
                    "Enhanced Sync: disattivo se causa scatti; con FreeSync usa FreeSync più un limite FPS sotto la frequenza del monitor.",
                    "Radeon Boost: disattivo per il competitivo (abbassa la risoluzione durante i movimenti rapidi).",
                ],
                "Non usare versioni del driver con \"Anti-Lag+\" del 2023 nei giochi online: era stato ritirato perché alcuni anti-cheat lo segnalavano."));
        }

        if (vendors.Contains(GpuVendor.Intel))
        {
            items.Add(new("gpu.intel.panel", "Intel Graphics Software / Arc Control", TweakGroup.Gpu,
                "Impostazioni del driver Intel per la latenza.",
                [
                    "Apri Intel Graphics Software (o Arc Control) > Giochi > scegli il gioco.",
                    "\"Modalità bassa latenza\": On.",
                    "Con monitor VRR: Adaptive Sync attivo e limite FPS sotto la frequenza del monitor.",
                ]));
        }

        if (vendors.Overlaps([GpuVendor.Nvidia, GpuVendor.Amd, GpuVendor.Intel]))
        {
            items.Add(new("gpu.ddu", "Reinstallazione pulita del driver video (DDU)", TweakGroup.Gpu,
                "Da fare solo se ci sono problemi (DPC lunghi del driver video, crash, scatti dopo un aggiornamento): non è una manutenzione da ripetere.",
                [
                    "Scarica l'ultimo driver dal sito del produttore della GPU e Display Driver Uninstaller (DDU) da wagnardsoft.com.",
                    "Crea un punto di ripristino e scollega Internet (Windows Update non deve installare un driver nel frattempo).",
                    "Riavvia in modalità provvisoria (Impostazioni > Sistema > Ripristino > Avvio avanzato).",
                    "Avvia DDU, scegli il tipo di GPU e \"Pulisci e riavvia\".",
                    "Installa il driver scaricato, poi ricollega Internet.",
                    "Ripeti la misura di latenza per verificare il risultato.",
                ],
                "DDU rimuove completamente il driver: segui i passaggi in ordine.", Risk.Medium));
        }

        items.Add(new("system.notifications", "Notifiche disattivate durante il gioco", TweakGroup.Gaming,
            "Windows può sospendere automaticamente le notifiche mentre giochi (impostazione dell'utente, non modificata dall'app).",
            context.IsWindows11
                ? ["Impostazioni > Sistema > Notifiche > Attiva automaticamente \"Non disturbare\".", "Seleziona \"Quando giochi\"."]
                : ["Impostazioni > Sistema > Assistente notifiche.", "In \"Regole automatiche\" attiva \"Quando gioco\"."]));

        foreach (var overlay in context.Report()?.Software.Known.Where(k => k.Overlay && !k.NeverTouch) ?? [])
        {
            items.Add(new($"overlay.{ServiceTweaks.Slug(overlay.Id)}", $"Overlay di {overlay.Name}", TweakGroup.Gaming,
                "Gli overlay disegnano sopra il gioco e possono costringerlo a passare dal compositore di Windows (più latenza). " +
                "Disattivali per i giochi competitivi se non li usi.",
                OverlaySteps(overlay)));
        }

        if (context.SystemOnHdd())
        {
            items.Add(new("storage.ssd", "Windows su disco meccanico", TweakGroup.Storage,
                "Il disco meccanico è il collo di bottiglia principale: avvio, caricamenti e scatti durante il gioco. Nessuna " +
                "impostazione software lo compensa.",
                [
                    "Un SSD SATA è compatibile con quasi tutti i PC e costa poco.",
                    "Clona il disco con lo strumento gratuito del produttore dell'SSD, oppure reinstalla Windows sull'SSD.",
                    "Installa sull'SSD almeno Windows e i giochi che usi di più.",
                ]));
        }

        if (profile.NetworkAdapters.FirstOrDefault(a => a.IsPrimary) is { Medium: NetworkMedium.WiFi })
        {
            items.Add(new("network.cable", "Connessione via cavo", TweakGroup.Network,
                "Il Wi-Fi aggiunge latenza variabile e i suoi driver causano spesso DPC lunghi.",
                [
                    "Collega il PC al router con un cavo Ethernet (o un adattatore Powerline se il router è lontano).",
                    "Se resti su Wi-Fi: usa la banda 5 GHz o 6 GHz e posiziona il router in vista del PC.",
                ]));
        }

        items.Add(new("bios.guide", "BIOS: XMP/EXPO, Resizable BAR e limiti", TweakGroup.Latency,
            "Le impostazioni del BIOS (profilo della RAM, Resizable BAR, aggiornamento del firmware) spesso valgono più di qualsiasi tweak di Windows.",
            ["Apri la sezione \"Overclock e BIOS\": l'app mostra i passaggi con i nomi dei menu della tua scheda madre."]));

        return items;
    }

    private static IReadOnlyList<string> OverlaySteps(SoftwareMatch overlay) => overlay.Id switch
    {
        "discord" => ["Discord > Impostazioni utente > Overlay di gioco > disattiva \"Abilita overlay in gioco\"."],
        "steam" => ["Steam > Impostazioni > In gioco > disattiva \"Abilita l'overlay di Steam durante il gioco\" (o solo per il gioco: Proprietà > Generali)."],
        "nvidia-app" => ["Premi Alt+Z > Impostazioni > disattiva \"Overlay in gioco\" o almeno \"Replay istantaneo\" se non lo usi."],
        "amd-adrenalin" => ["AMD Software > Impostazioni > Preferenze > disattiva \"Overlay in gioco\"; in Registrazione disattiva il \"Replay istantaneo\" se non lo usi."],
        "xbox-gamebar" => ["Impostazioni > Giochi > Xbox Game Bar: disattiva l'apertura con il pulsante del controller se non la usi."],
        "overwolf" => ["Overwolf > Impostazioni > Overlay e tasti di scelta rapida: disattiva l'overlay per i giochi competitivi."],
        "medal" => ["Medal > Impostazioni > Overlay: disattivalo; valuta di disattivare la registrazione continua."],
        "rtss" => ["Se usi RivaTuner solo per limitare gli FPS va bene; altrimenti chiudilo quando giochi."],
        _ => [overlay.Note ?? "Disattiva l'overlay dalle impostazioni del programma o chiudilo quando giochi."],
    };
}
