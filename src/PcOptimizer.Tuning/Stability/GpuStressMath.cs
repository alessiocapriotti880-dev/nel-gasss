namespace PcOptimizer.Tuning.Stability;

/// <summary>
/// Calcoli del test della GPU, identici in HLSL e in C#: la GPU esegue hash interi (bit per bit verificabili dalla CPU)
/// e una parte in virgola mobile confrontata fra due esecuzioni consecutive sulla stessa GPU.
/// </summary>
public static class GpuStressMath
{
    public const int ThreadsPerGroup = 256;

    /// <summary>Shader di calcolo (cs_5_0): Output[i] = hash intero, FloatOut[i] = accumulo in virgola mobile.</summary>
    public const string ComputeShader = """
        cbuffer Params : register(b0) { uint Seed; uint Iterations; uint Count; uint Pad; };
        RWStructuredBuffer<uint> Output : register(u0);
        RWStructuredBuffer<uint> FloatOut : register(u1);

        [numthreads(256, 1, 1)]
        void main(uint3 id : SV_DispatchThreadID)
        {
            uint i = id.x;
            if (i >= Count) return;
            uint x = i * 747796405u + Seed;
            float f = (float)(i & 1023u) * 0.001f + 1.0f;
            for (uint k = 0; k < Iterations; k++)
            {
                x = x * 747796405u + 2891336453u;
                uint w = ((x >> ((x >> 28u) + 4u)) ^ x) * 277803737u;
                x ^= (w >> 22u) ^ w;
                f = f * 0.9999999f + 0.0000001f;
            }
            Output[i] = x;
            FloatOut[i] = asuint(f);
        }
        """;

    /// <summary>Shader della VRAM: Fill scrive lo schema, Verify conta i valori diversi (contatore atomico).</summary>
    public const string VramShader = """
        cbuffer Params : register(b0) { uint Seed; uint Count; uint Pad0; uint Pad1; };
        RWStructuredBuffer<uint> Data : register(u0);
        RWStructuredBuffer<uint> Errors : register(u1);

        uint Pattern(uint i)
        {
            uint x = i * 747796405u + Seed;
            uint w = ((x >> ((x >> 28u) + 4u)) ^ x) * 277803737u;
            return (w >> 22u) ^ w;
        }

        [numthreads(256, 1, 1)]
        void Fill(uint3 id : SV_DispatchThreadID)
        {
            if (id.x < Count) Data[id.x] = Pattern(id.x);
        }

        [numthreads(256, 1, 1)]
        void Verify(uint3 id : SV_DispatchThreadID)
        {
            if (id.x < Count && Data[id.x] != Pattern(id.x)) InterlockedAdd(Errors[0], 1u);
        }
        """;

    /// <summary>Riferimento CPU della parte intera dello shader di calcolo.</summary>
    public static uint Hash(uint index, uint seed, uint iterations)
    {
        unchecked
        {
            var x = index * 747796405u + seed;
            for (uint k = 0; k < iterations; k++)
            {
                x = x * 747796405u + 2891336453u;
                var w = ((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u;
                x ^= (w >> 22) ^ w;
            }

            return x;
        }
    }

    /// <summary>Riferimento CPU dello schema scritto nella VRAM.</summary>
    public static uint Pattern(uint index, uint seed)
    {
        unchecked
        {
            var x = index * 747796405u + seed;
            var w = ((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u;
            return (w >> 22) ^ w;
        }
    }

    /// <summary>
    /// Confronto dei risultati: la parte intera con il riferimento CPU (un indice ogni <paramref name="sampleStride"/>),
    /// la parte in virgola mobile con l'esecuzione precedente. Restituisce il numero di valori errati.
    /// </summary>
    public static long Check(ReadOnlySpan<uint> hashes, ReadOnlySpan<uint> floats, ReadOnlySpan<uint> previousFloats,
        uint seed, uint iterations, int sampleStride = 997)
    {
        long errors = 0;
        for (var i = 0; i < hashes.Length; i += sampleStride)
        {
            if (hashes[i] != Hash((uint)i, seed, iterations))
            {
                errors++;
            }
        }

        if (previousFloats.Length == floats.Length)
        {
            for (var i = 0; i < floats.Length; i++)
            {
                if (floats[i] != previousFloats[i])
                {
                    errors++;
                }
            }
        }

        return errors;
    }
}
