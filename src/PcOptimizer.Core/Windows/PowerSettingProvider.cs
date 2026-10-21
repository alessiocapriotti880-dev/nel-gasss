using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PcOptimizer.Core.Settings;

namespace PcOptimizer.Core.Windows;

/// <summary>
/// Impostazioni dei piani energetici tramite powrprof.dll (le stesse API di powercfg).
/// Dopo la scrittura, se il piano è quello attivo, viene riattivato perché il valore abbia effetto subito.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PowerSettingProvider : ISettingProvider
{
    public bool CanHandle(SettingTarget target) => target is PowerSettingTarget;

    public SettingValue Read(SettingTarget target)
    {
        var t = (PowerSettingTarget)target;
        var scheme = t.Scheme;
        var sub = t.SubGroup;
        var setting = t.Setting;
        var result = t.Source == PowerSource.Ac
            ? Native.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out var value)
            : Native.PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out value);

        // ERROR_FILE_NOT_FOUND: piano o impostazione inesistenti su questo sistema.
        if (result == 2)
        {
            return SettingValue.Absent;
        }

        Check(result, t);
        return SettingValue.Of(value);
    }

    public void Write(SettingTarget target, SettingValue value)
    {
        var t = (PowerSettingTarget)target;
        if (!value.Exists)
        {
            return; // un'impostazione energetica non si "cancella": se non esisteva non c'è nulla da ripristinare.
        }

        var scheme = t.Scheme;
        var sub = t.SubGroup;
        var setting = t.Setting;
        var data = value.AsUInt32();
        Check(t.Source == PowerSource.Ac
            ? Native.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, data)
            : Native.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, data), t);

        if (ActiveScheme() == t.Scheme)
        {
            Check(Native.PowerSetActiveScheme(IntPtr.Zero, ref scheme), t);
        }
    }

    /// <summary>GUID del piano energetico attivo.</summary>
    public static Guid ActiveScheme()
    {
        var error = Native.PowerGetActiveScheme(IntPtr.Zero, out var ptr);
        if (error != 0)
        {
            throw new Win32Exception((int)error);
        }

        try
        {
            return Marshal.PtrToStructure<Guid>(ptr);
        }
        finally
        {
            Native.LocalFree(ptr);
        }
    }

    private static void Check(uint error, PowerSettingTarget t)
    {
        if (error != 0)
        {
            throw new Win32Exception((int)error, $"{t.Describe()}: errore {error}.");
        }
    }

    private static class Native
    {
        [DllImport("powrprof.dll")]
        public static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid scheme, ref Guid subGroup, ref Guid setting, out uint value);

        [DllImport("powrprof.dll")]
        public static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid scheme, ref Guid subGroup, ref Guid setting, out uint value);

        [DllImport("powrprof.dll")]
        public static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid scheme, ref Guid subGroup, ref Guid setting, uint value);

        [DllImport("powrprof.dll")]
        public static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, ref Guid scheme, ref Guid subGroup, ref Guid setting, uint value);

        [DllImport("powrprof.dll")]
        public static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid scheme);

        [DllImport("powrprof.dll")]
        public static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activeScheme);

        [DllImport("kernel32.dll")]
        public static extern IntPtr LocalFree(IntPtr mem);
    }
}
