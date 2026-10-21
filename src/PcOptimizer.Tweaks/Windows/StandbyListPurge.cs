using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PcOptimizer.Core.Model;
using PcOptimizer.Core.Tweaks;

namespace PcOptimizer.Tweaks.Windows;

/// <summary>
/// Svuota la "standby list" (cache dei file in RAM) con la stessa funzione usata da RAMMap di Sysinternals.
/// Non modifica impostazioni e non installa nulla: è un'operazione una tantum.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StandbyListPurge : IMaintenanceAction
{
    private const int SystemMemoryListInformation = 80;
    private const int MemoryPurgeStandbyList = 4;
    private const string ProfilePrivilege = "SeProfileSingleProcessPrivilege";

    public string Id => "maintenance.standby-purge";

    public string Title => "Svuotamento della memoria in standby";

    public string Description =>
        "Libera la cache dei file tenuta in RAM. Windows lo fa già da solo quando serve memoria: utile solo come prova se " +
        "noti scatti con pochissima RAM libera. Dopo lo svuotamento i programmi possono caricarsi un po' più lentamente.";

    public Risk Risk => Risk.Low;

    public Task<string> PreviewAsync(CancellationToken cancellationToken) =>
        Task.FromResult($"Memoria disponibile ora: {AvailableMb()} MB (la standby list è conteggiata come disponibile).");

    public Task<string> RunAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        var before = AvailableMb();
        EnablePrivilege(ProfilePrivilege);
        var command = MemoryPurgeStandbyList;
        var status = NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int));
        if (status != 0)
        {
            throw new InvalidOperationException($"Svuotamento non riuscito (stato 0x{status:X8}): serve l'esecuzione come amministratore.");
        }

        return $"Standby list svuotata. Memoria disponibile prima: {before} MB, dopo: {AvailableMb()} MB.";
    }, cancellationToken);

    private static string AvailableMb()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status)
            ? (status.AvailPhys / 1024 / 1024).ToString(CultureInfo.InvariantCulture)
            : "n/d";
    }

    private static void EnablePrivilege(string name)
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x20 /* TOKEN_ADJUST_PRIVILEGES */ | 0x8 /* TOKEN_QUERY */, out var token))
        {
            throw new Win32Exception();
        }

        try
        {
            if (!LookupPrivilegeValueW(null, name, out var luid))
            {
                throw new Win32Exception();
            }

            var privileges = new TokenPrivileges { PrivilegeCount = 1, Luid = luid, Attributes = 0x2 /* SE_PRIVILEGE_ENABLED */ };
            if (!AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero) || Marshal.GetLastWin32Error() != 0)
            {
                throw new InvalidOperationException("Privilegio necessario non disponibile: serve l'esecuzione come amministratore.");
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    // TOKEN_PRIVILEGES con un solo privilegio: il LUID è allineato a 4 byte, non a 8.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public long Luid;
        public uint Attributes;
    }

    [DllImport("ntdll.dll")]
    private static extern uint NtSetSystemInformation(int infoClass, ref int info, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValueW(string? system, string name, out long luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, ref TokenPrivileges newState, int length, IntPtr previous, IntPtr returnLength);
}
