using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PcOptimizer.Tuning.Guides;
using PcOptimizer.Tuning.Stability;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace PcOptimizer.Tuning.Windows;

/// <summary>
/// Test della GPU con Direct3D 11: uno shader di calcolo intensivo verificato dalla CPU e, per la VRAM, la scrittura e
/// la verifica di schemi su buffer grandi. Usa la GPU predefinita come qualsiasi programma 3D; con <c>useWarp</c> usa il
/// rasterizzatore software di Windows (serve per provare il test senza scheda video, ad esempio in CI).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GpuStressTest : IStressWorkload, IDisposable
{
    private const uint Elements = 1 << 20;
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly long _vramBytes;
    private readonly uint _iterations;

    /// <param name="vramBytes">0 = test di calcolo; altrimenti test della VRAM su questa quantità di memoria.</param>
    public GpuStressTest(long vramBytes = 0, bool useWarp = false, uint iterations = 2048)
    {
        _vramBytes = vramBytes;
        _iterations = useWarp ? Math.Min(iterations, 64u) : iterations;
        var result = D3D11.D3D11CreateDevice(IntPtr.Zero, useWarp ? DriverType.Warp : DriverType.Hardware, DeviceCreationFlags.None,
            [FeatureLevel.Level_11_0], out var device, out var context);
        if (result.Failure || device is null || context is null)
        {
            throw new InvalidOperationException($"Direct3D 11 non disponibile (0x{result.Code:X8}).");
        }

        _device = device;
        _context = context;
    }

    public StabilityTestKind Kind => _vramBytes > 0 ? StabilityTestKind.Vram : StabilityTestKind.Gpu;

    public Task<(long WorkUnits, long Errors, IReadOnlyList<string> Notes)> RunAsync(CancellationToken cancellationToken) =>
        Task.Factory.StartNew(() => _vramBytes > 0 ? RunVram(cancellationToken) : RunCompute(cancellationToken),
            cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private (long, long, IReadOnlyList<string>) RunCompute(CancellationToken cancellationToken)
    {
        using var shader = CreateShader(GpuStressMath.ComputeShader, "main");
        using var output = StructuredBuffer(Elements);
        using var floats = StructuredBuffer(Elements);
        using var outputView = _device.CreateUnorderedAccessView(output, null);
        using var floatsView = _device.CreateUnorderedAccessView(floats, null);
        using var parameters = _device.CreateBuffer(16, BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0);
        using var staging = StagingBuffer(Elements);

        long units = 0, errors = 0;
        var notes = new List<string>();
        uint[]? previousFloats = null;
        var hashes = new uint[Elements];
        var floatValues = new uint[Elements];
        while (!cancellationToken.IsCancellationRequested)
        {
            // Stesso seme per due esecuzioni di fila: la parte in virgola mobile deve coincidere tra le due.
            var seed = (uint)(units / 2 * 2654435761u);
            _context.UpdateSubresource(new uint[] { seed, _iterations, Elements, 0 }, parameters, 0, 0, 0, null);
            _context.CSSetShader(shader);
            _context.CSSetConstantBuffer(0, parameters);
            _context.CSSetUnorderedAccessViews(0, [outputView, floatsView]);
            _context.Dispatch(Elements / GpuStressMath.ThreadsPerGroup, 1, 1);

            Read(output, staging, hashes);
            Read(floats, staging, floatValues);
            var bad = GpuStressMath.Check(hashes, floatValues, units % 2 == 1 ? previousFloats : [], seed, _iterations);
            if (bad > 0)
            {
                errors += bad;
                if (notes.Count < 20)
                {
                    notes.Add($"{bad} risultati errati al dispatch {units + 1}.");
                }
            }

            previousFloats = (uint[])floatValues.Clone();
            units++;
        }

        return (units, errors, notes);
    }

    private (long, long, IReadOnlyList<string>) RunVram(CancellationToken cancellationToken)
    {
        using var fill = CreateShader(GpuStressMath.VramShader, "Fill");
        using var verify = CreateShader(GpuStressMath.VramShader, "Verify");
        const uint chunkElements = 32 * 1024 * 1024 / sizeof(uint); // blocchi da 128 MB
        var chunks = new List<(ID3D11Buffer Buffer, ID3D11UnorderedAccessView View)>();
        var notes = new List<string>();
        using var errorsBuffer = StructuredBuffer(1);
        using var errorsView = _device.CreateUnorderedAccessView(errorsBuffer, null);
        using var parameters = _device.CreateBuffer(16, BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0);
        using var staging = StagingBuffer(1);
        try
        {
            for (long allocated = 0; allocated < _vramBytes; allocated += chunkElements * sizeof(uint))
            {
                try
                {
                    var buffer = StructuredBuffer(chunkElements);
                    chunks.Add((buffer, _device.CreateUnorderedAccessView(buffer, null)));
                }
                catch (SharpGen.Runtime.SharpGenException)
                {
                    notes.Add($"Allocati {allocated / 1024 / 1024} MB di VRAM (memoria disponibile esaurita).");
                    break;
                }
            }

            long passes = 0, errors = 0;
            var counter = new uint[1];
            while (!cancellationToken.IsCancellationRequested && chunks.Count > 0)
            {
                var seed = (uint)(passes * 2654435761u + 1);
                _context.UpdateSubresource(new uint[] { 0 }, errorsBuffer, 0, 0, 0, null);
                _context.UpdateSubresource(new uint[] { seed, chunkElements, 0, 0 }, parameters, 0, 0, 0, null);
                _context.CSSetConstantBuffer(0, parameters);
                foreach (var (_, view) in chunks)
                {
                    _context.CSSetShader(fill);
                    _context.CSSetUnorderedAccessViews(0, [view, errorsView]);
                    _context.Dispatch(chunkElements / GpuStressMath.ThreadsPerGroup, 1, 1);
                }

                foreach (var (_, view) in chunks)
                {
                    _context.CSSetShader(verify);
                    _context.CSSetUnorderedAccessViews(0, [view, errorsView]);
                    _context.Dispatch(chunkElements / GpuStressMath.ThreadsPerGroup, 1, 1);
                }

                Read(errorsBuffer, staging, counter);
                if (counter[0] > 0)
                {
                    errors += counter[0];
                    notes.Add($"{counter[0]} valori errati nella VRAM al passaggio {passes + 1}.");
                }

                passes++;
            }

            return (passes, errors, notes);
        }
        finally
        {
            foreach (var (buffer, view) in chunks)
            {
                view.Dispose();
                buffer.Dispose();
            }
        }
    }

    private ID3D11ComputeShader CreateShader(string source, string entry)
    {
        var bytecode = Compiler.Compile(source, entry, "pcopt-stress.hlsl", "cs_5_0", ShaderFlags.OptimizationLevel3, EffectFlags.None);
        return _device.CreateComputeShader(bytecode.Span, null);
    }

    private ID3D11Buffer StructuredBuffer(uint elements) =>
        _device.CreateBuffer(elements * sizeof(uint), BindFlags.UnorderedAccess, ResourceUsage.Default, CpuAccessFlags.None,
            ResourceOptionFlags.BufferStructured, sizeof(uint));

    private ID3D11Buffer StagingBuffer(uint elements) =>
        _device.CreateBuffer(elements * sizeof(uint), BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read,
            ResourceOptionFlags.BufferStructured, sizeof(uint));

    /// <summary>Copia un buffer della GPU in memoria attraverso il buffer di staging (stessa dimensione).</summary>
    private void Read(ID3D11Buffer source, ID3D11Buffer staging, uint[] destination)
    {
        _context.CopyResource(staging, source);
        var mapped = _context.Map(staging, MapMode.Read, MapFlags.None);
        try
        {
            Marshal.Copy(mapped.DataPointer, (int[])(object)destination, 0, destination.Length);
        }
        finally
        {
            _context.Unmap(staging);
        }
    }

    public void Dispose()
    {
        _context.Dispose();
        _device.Dispose();
    }
}
