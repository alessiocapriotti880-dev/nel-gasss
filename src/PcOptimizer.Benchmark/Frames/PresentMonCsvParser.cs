using System.Globalization;

namespace PcOptimizer.Benchmark.Frames;

/// <summary>Un frame presentato dal gioco, nei nomi di PresentMon normalizzati fra le versioni.</summary>
public sealed record FrameRecord
{
    public string Application { get; init; } = "";

    public double TimeSeconds { get; init; }

    /// <summary>Tempo fra l'inizio di due frame consecutivi lato CPU (MsBetweenPresents / FrameTime).</summary>
    public double FrameTimeMs { get; init; }

    /// <summary>Tempo fra due cambi di immagine sullo schermo; null se il frame non è stato mostrato.</summary>
    public double? DisplayedTimeMs { get; init; }

    public double? DisplayLatencyMs { get; init; }

    public double? GpuBusyMs { get; init; }

    public double? CpuBusyMs { get; init; }

    public double? ClickToPhotonMs { get; init; }

    public bool Dropped { get; init; }

    public string? PresentMode { get; init; }

    public int? SyncInterval { get; init; }

    public bool? AllowsTearing { get; init; }
}

public sealed record PresentMonCapture(string Format, IReadOnlyList<FrameRecord> Frames)
{
    /// <summary>Applicazioni catturate con il numero di frame, dalla più presente.</summary>
    public IReadOnlyList<(string Application, int Frames)> Applications =>
        Frames.GroupBy(f => f.Application, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, g.Count()))
            .OrderByDescending(x => x.Item2)
            .ToList();
}

/// <summary>
/// Lettura dei CSV di PresentMon 1.x ("MsBetweenPresents"...) e 2.x ("FrameTime", "CPUBusy", "GPUBusy"...),
/// compresi quelli salvati da FrameView e CapFrameX che usano le stesse colonne.
/// Le colonne si cercano per nome, senza distinzione di maiuscole: l'ordine e le colonne in più non contano.
/// </summary>
public static class PresentMonCsvParser
{
    private static readonly string[] FrameTimeColumns = ["FrameTime", "MsBetweenPresents", "MsBetweenAppStart", "MsBetweenSimulationStart"];
    private static readonly string[] DisplayedColumns = ["DisplayedTime", "MsBetweenDisplayChange"];
    private static readonly string[] DisplayLatencyColumns = ["DisplayLatency", "MsUntilDisplayed"];
    private static readonly string[] GpuBusyColumns = ["GPUBusy", "MsGPUBusy", "MsGPUActive"];
    private static readonly string[] CpuBusyColumns = ["CPUBusy", "MsCPUBusy"];
    private static readonly string[] TimeColumns = ["TimeInSeconds", "CPUStartTime", "TimeInSec"];
    private static readonly string[] ClickColumns = ["ClickToPhotonLatency", "MsClickToPhotonLatency", "AllInputToPhotonLatency", "MsAllInputToPhotonLatency"];

    public static PresentMonCapture Parse(TextReader reader)
    {
        var headerLine = reader.ReadLine() ?? throw new InvalidDataException("CSV vuoto.");
        var header = SplitCsv(headerLine);
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
        {
            columns.TryAdd(header[i].Trim(), i);
        }

        int Col(params string[] names)
        {
            foreach (var n in names)
            {
                if (columns.TryGetValue(n, out var i))
                {
                    return i;
                }
            }

            return -1;
        }

        var frameTime = Col(FrameTimeColumns);
        if (frameTime < 0)
        {
            throw new InvalidDataException("Il CSV non contiene una colonna di frame time (FrameTime o MsBetweenPresents): non sembra un file PresentMon.");
        }

        var format = columns.ContainsKey("MsBetweenPresents") ? "PresentMon 1.x" : "PresentMon 2.x";
        var app = Col("Application");
        var time = Col(TimeColumns);
        var displayed = Col(DisplayedColumns);
        var latency = Col(DisplayLatencyColumns);
        var gpuBusy = Col(GpuBusyColumns);
        var cpuBusy = Col(CpuBusyColumns);
        var click = Col(ClickColumns);
        var dropped = Col("Dropped");
        var presentMode = Col("PresentMode");
        var syncInterval = Col("SyncInterval");
        var tearing = Col("AllowsTearing");

        var frames = new List<FrameRecord>();
        double clock = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var cells = SplitCsv(line);
            if (Number(cells, frameTime) is not { } ft || ft <= 0)
            {
                continue;
            }

            clock += ft / 1000;
            var displayedMs = Number(cells, displayed);
            frames.Add(new FrameRecord
            {
                Application = Text(cells, app) ?? "",
                TimeSeconds = Number(cells, time) ?? clock,
                FrameTimeMs = ft,
                DisplayedTimeMs = displayedMs,
                DisplayLatencyMs = Number(cells, latency),
                GpuBusyMs = Number(cells, gpuBusy),
                CpuBusyMs = Number(cells, cpuBusy),
                ClickToPhotonMs = Number(cells, click),
                // 1.x: colonna Dropped; 2.x: un frame mai mostrato ha DisplayedTime "NA".
                Dropped = dropped >= 0 ? Number(cells, dropped) is > 0 : displayed >= 0 && displayedMs is null,
                PresentMode = Text(cells, presentMode),
                SyncInterval = Number(cells, syncInterval) is { } si ? (int)si : null,
                AllowsTearing = Number(cells, tearing) is { } t ? t > 0 : null,
            });
        }

        return new PresentMonCapture(format, CheckTimes(frames));
    }

    /// <summary>
    /// Se la colonna del tempo non è in secondi (es. contatori QPC con --qpc_time) l'intervallo non torna con la
    /// somma dei frame time: in quel caso il tempo si ricostruisce sommando i frame time.
    /// </summary>
    private static List<FrameRecord> CheckTimes(List<FrameRecord> frames)
    {
        if (frames.Count < 2)
        {
            return frames;
        }

        var span = frames.Max(f => f.TimeSeconds) - frames.Min(f => f.TimeSeconds);
        var perApp = frames.GroupBy(f => f.Application).Max(g => g.Sum(f => f.FrameTimeMs)) / 1000;
        if (perApp <= 0 || (span > 0.5 * perApp && span < 2 * perApp + 5))
        {
            return frames;
        }

        double clock = 0;
        return frames.Select(f => f with { TimeSeconds = clock += f.FrameTimeMs / 1000 }).ToList();
    }

    public static PresentMonCapture ParseFile(string path)
    {
        using var reader = new StreamReader(path);
        return Parse(reader);
    }

    private static double? Number(IReadOnlyList<string> cells, int index)
    {
        if (index < 0 || index >= cells.Count)
        {
            return null;
        }

        var text = cells[index].Trim();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;
    }

    private static string? Text(IReadOnlyList<string> cells, int index) =>
        index >= 0 && index < cells.Count && cells[index].Trim() is { Length: > 0 } t && t != "NA" ? t : null;

    /// <summary>Divide una riga CSV rispettando i campi fra virgolette (con "" come virgoletta letterale).</summary>
    internal static List<string> SplitCsv(string line)
    {
        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        cells.Add(current.ToString());
        return cells;
    }
}
