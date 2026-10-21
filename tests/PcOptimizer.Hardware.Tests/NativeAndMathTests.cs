using System.Runtime.InteropServices;
using PcOptimizer.Hardware.Assessment;
using PcOptimizer.Hardware.Windows;

namespace PcOptimizer.Hardware.Tests;

public sealed class NativeAndMathTests
{
    /// <summary>
    /// Le strutture passate alle API di Windows devono avere esattamente la dimensione attesa:
    /// un errore qui produrrebbe dati spazzatura o errori "parametro non valido" solo su Windows.
    /// </summary>
    [Fact]
    public void Native_structures_match_windows_sizes()
    {
        Assert.Equal(72, Marshal.SizeOf<DisplayConfigPathInfo>());
        Assert.Equal(64, Marshal.SizeOf<DisplayConfigModeInfo>());
        Assert.Equal(20, Marshal.SizeOf<DisplayConfigDeviceInfoHeader>());
        Assert.Equal(420, Marshal.SizeOf<DisplayConfigTargetDeviceName>());
        Assert.Equal(84, Marshal.SizeOf<DisplayConfigSourceDeviceName>());
        Assert.Equal(276, Marshal.SizeOf<DisplayConfigAdapterName>());
        Assert.Equal(220, Marshal.SizeOf<DevMode>());
        Assert.Equal(12, Marshal.SizeOf<SystemPowerStatus>());
    }

    [Fact]
    public void Score_scales_behave_as_documented()
    {
        Assert.Equal(0, ScoreMath.Linear(0.5, 0.6, 2.0));
        Assert.Equal(50, ScoreMath.Linear(1.3, 0.6, 2.0), 6);
        Assert.Equal(100, ScoreMath.Linear(3, 0.6, 2.0));

        Assert.Equal(0, ScoreMath.Log(10, 15, 500));
        Assert.Equal(100, ScoreMath.Log(500, 15, 500), 6);
        Assert.Equal(50, ScoreMath.Log(Math.Sqrt(15 * 500), 15, 500), 6); // media geometrica = metà scala

        double[][] points = [[4, 10], [8, 40], [16, 70], [32, 100]];
        Assert.Equal(5, ScoreMath.Piecewise(2, points), 6);
        Assert.Equal(55, ScoreMath.Piecewise(12, points), 6);
        Assert.Equal(100, ScoreMath.Piecewise(64, points), 6);
    }

    [Fact]
    public void Numbers_are_formatted_with_italian_decimal_comma() =>
        Assert.Equal("15,9", ScoreMath.Fmt(15.94));
}
