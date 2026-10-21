using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using PcOptimizer.Benchmark.Sensors;

namespace PcOptimizer.Benchmark.Windows;

/// <summary>Una lettura NVML della GPU NVIDIA.</summary>
public sealed record NvmlReading(
    double? TemperatureC, double? PowerW, double? PowerLimitW, double? GraphicsClockMHz, double? MemoryClockMHz,
    double? UsagePercent, double? FanPercent, IReadOnlyList<string> Limits);

/// <summary>
/// Lettura dei sensori NVIDIA tramite NVML (nvml.dll, installata con il driver; la stessa usata da nvidia-smi).
/// Solo lettura: l'app non scrive frequenze o tensioni della GPU.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Nvml : IDisposable
{
    private const int Success = 0;
    private const int TemperatureGpu = 0;
    private const int ClockGraphics = 0;
    private const int ClockMemory = 2;

    private readonly IntPtr _library;
    private readonly Fn.Shutdown _shutdown;
    private readonly Fn.GetName _getName;
    private readonly Fn.GetUInt _getTemperature;
    private readonly Fn.GetUIntSimple _getPower;
    private readonly Fn.GetUIntSimple _getPowerLimit;
    private readonly Fn.GetUInt _getClock;
    private readonly Fn.GetUtilization _getUtilization;
    private readonly Fn.GetUIntSimple _getFan;
    private readonly Fn.GetULong? _getLimits;
    private bool _disposed;

    private Nvml(IntPtr library, IReadOnlyList<(IntPtr Handle, string Name)> devices)
    {
        _library = library;
        _shutdown = Get<Fn.Shutdown>("nvmlShutdown");
        _getName = Get<Fn.GetName>("nvmlDeviceGetName");
        _getTemperature = Get<Fn.GetUInt>("nvmlDeviceGetTemperature");
        _getPower = Get<Fn.GetUIntSimple>("nvmlDeviceGetPowerUsage");
        _getPowerLimit = Get<Fn.GetUIntSimple>("nvmlDeviceGetEnforcedPowerLimit");
        _getClock = Get<Fn.GetUInt>("nvmlDeviceGetClockInfo");
        _getUtilization = Get<Fn.GetUtilization>("nvmlDeviceGetUtilizationRates");
        _getFan = Get<Fn.GetUIntSimple>("nvmlDeviceGetFanSpeed");
        _getLimits = TryGet<Fn.GetULong>("nvmlDeviceGetCurrentClocksEventReasons") ?? TryGet<Fn.GetULong>("nvmlDeviceGetCurrentClocksThrottleReasons");
        Devices = devices;
    }

    public IReadOnlyList<(IntPtr Handle, string Name)> Devices { get; }

    /// <summary>Apre NVML se il driver NVIDIA è installato; null altrimenti.</summary>
    public static Nvml? TryOpen()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.SystemDirectory, "nvml.dll"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll"),
        };
        foreach (var path in candidates)
        {
            if (!File.Exists(path) || !NativeLibrary.TryLoad(path, out var library))
            {
                continue;
            }

            try
            {
                if (!NativeLibrary.TryGetExport(library, "nvmlInit_v2", out var initPtr) ||
                    Marshal.GetDelegateForFunctionPointer<Fn.Init>(initPtr)() != Success)
                {
                    NativeLibrary.Free(library);
                    continue;
                }

                var count = Marshal.GetDelegateForFunctionPointer<Fn.GetCount>(NativeLibrary.GetExport(library, "nvmlDeviceGetCount_v2"));
                var byIndex = Marshal.GetDelegateForFunctionPointer<Fn.GetHandle>(NativeLibrary.GetExport(library, "nvmlDeviceGetHandleByIndex_v2"));
                var getName = Marshal.GetDelegateForFunctionPointer<Fn.GetName>(NativeLibrary.GetExport(library, "nvmlDeviceGetName"));
                var devices = new List<(IntPtr, string)>();
                if (count(out var n) == Success)
                {
                    for (uint i = 0; i < n; i++)
                    {
                        if (byIndex(i, out var handle) == Success)
                        {
                            devices.Add((handle, ReadName(getName, handle) ?? $"GPU NVIDIA {i}"));
                        }
                    }
                }

                return new Nvml(library, devices);
            }
            catch (EntryPointNotFoundException)
            {
                NativeLibrary.Free(library);
            }
        }

        return null;
    }

    public NvmlReading Read(IntPtr device)
    {
        uint? Value(Func<uint?> read)
        {
            try
            {
                return read();
            }
            catch (SEHException)
            {
                return null;
            }
        }

        var temperature = Value(() => _getTemperature(device, TemperatureGpu, out var v) == Success ? v : null);
        var power = Value(() => _getPower(device, out var v) == Success ? v : null);
        var limit = Value(() => _getPowerLimit(device, out var v) == Success ? v : null);
        var clock = Value(() => _getClock(device, ClockGraphics, out var v) == Success ? v : null);
        var memory = Value(() => _getClock(device, ClockMemory, out var v) == Success ? v : null);
        var usage = Value(() => _getUtilization(device, out var u) == Success ? u.Gpu : null);
        var fan = Value(() => _getFan(device, out var v) == Success ? v : null);
        ulong reasons = 0;
        if (_getLimits is not null && _getLimits(device, out var r) == Success)
        {
            reasons = r;
        }

        return new NvmlReading(temperature, power / 1000.0, limit / 1000.0, clock, memory, usage, fan, NvmlLimits.Describe(reasons));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown();
        NativeLibrary.Free(_library);
    }

    private static string? ReadName(Fn.GetName getName, IntPtr device)
    {
        var buffer = new byte[96];
        if (getName(device, buffer, (uint)buffer.Length) != Success)
        {
            return null;
        }

        var end = Array.IndexOf(buffer, (byte)0);
        return Encoding.UTF8.GetString(buffer, 0, end < 0 ? buffer.Length : end);
    }

    private T Get<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

    private T? TryGet<T>(string name) where T : Delegate =>
        NativeLibrary.TryGetExport(_library, name, out var ptr) ? Marshal.GetDelegateForFunctionPointer<T>(ptr) : null;

    [StructLayout(LayoutKind.Sequential)]
    private struct Utilization
    {
        public uint Gpu;
        public uint Memory;
    }

    private static class Fn
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int Init();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int Shutdown();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int GetCount(out uint count);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int GetHandle(uint index, out IntPtr device);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int GetName(IntPtr device, byte[] name, uint length);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int GetUInt(IntPtr device, int kind, out uint value);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int GetUIntSimple(IntPtr device, out uint value);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int GetULong(IntPtr device, out ulong value);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int GetUtilization(IntPtr device, out Utilization value);
    }
}
