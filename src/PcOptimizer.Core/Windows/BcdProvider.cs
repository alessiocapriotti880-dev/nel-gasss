using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using PcOptimizer.Core.Parsing;
using PcOptimizer.Core.Settings;

namespace PcOptimizer.Core.Windows;

/// <summary>
/// Elementi della voce di avvio corrente con lo strumento ufficiale bcdedit (richiede amministratore).
/// I valori booleani si scrivono in inglese ("Yes"/"No"), accettati da bcdedit in qualsiasi lingua.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class BcdProvider : ISettingProvider
{
    public bool CanHandle(SettingTarget target) => target is BcdElementTarget;

    public SettingValue Read(SettingTarget target)
    {
        var element = ((BcdElementTarget)target).Element;
        var (exit, output) = Run("/enum", "{current}");
        if (exit != 0)
        {
            throw new InvalidOperationException($"bcdedit non ha letto la configurazione di avvio (codice {exit}): {output.Trim()}");
        }

        return BcdParser.ParseElements(output, [element]).TryGetValue(element.ToLowerInvariant(), out var raw)
            ? SettingValue.Of(BcdParser.Normalize(raw))
            : SettingValue.Absent;
    }

    public void Write(SettingTarget target, SettingValue value)
    {
        var element = ((BcdElementTarget)target).Element;
        var (exit, output) = value.Exists
            ? Run("/set", "{current}", element, value.Data ?? "")
            : Run("/deletevalue", "{current}", element);
        if (exit != 0)
        {
            throw new InvalidOperationException($"bcdedit non ha modificato {element} (codice {exit}): {output.Trim()}");
        }
    }

    private static (int ExitCode, string Output) Run(params string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "bcdedit.exe"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(info) ?? throw new InvalidOperationException("bcdedit non avviato.");
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            if (!process.WaitForExit(15_000))
            {
                process.Kill();
                throw new TimeoutException("bcdedit non ha risposto.");
            }

            return (process.ExitCode, output);
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"bcdedit non disponibile: {ex.Message}", ex);
        }
    }
}
