using HydroPulse.Domain.Engineering;
using Xunit;

namespace HydroPulse.Domain.Tests;

public class ElectrolyzerModelTests
{
    private static ElectrolyzerModel Model() => new(new ElectrolyzerParameters());

    [Fact]
    public void FullLoad_NewStack_UsesRatedSpecificEnergy()
    {
        var s = Model().Evaluate(1000.0);

        Assert.Equal(ElectrolyzerMode.PartLoad, s.Mode);
        Assert.Equal(50.0, s.SpecificEnergyKwhPerKg, 6);
        Assert.Equal(20.0, s.HydrogenKgPerHour, 6);
        Assert.Equal(190.0, s.WaterLitersPerHour, 6);
    }

    [Fact]
    public void HalfLoad_IsMoreEfficientPerKg()
    {
        var s = Model().Evaluate(500.0);

        Assert.Equal(48.0, s.SpecificEnergyKwhPerKg, 6);                 // 50 * (1 - 0.08 * 0.5)
        Assert.Equal(500.0 / 48.0, s.HydrogenKgPerHour, 6);
    }

    [Fact]
    public void BelowTurndown_IsStandby()
    {
        var s = Model().Evaluate(50.0);

        Assert.Equal(ElectrolyzerMode.Standby, s.Mode);
        Assert.Equal(0.0, s.HydrogenKgPerHour);
    }

    [Fact]
    public void ExactlyAtMinimumLoad_IsActive()
    {
        var s = Model().Evaluate(100.0);

        Assert.NotEqual(ElectrolyzerMode.Standby, s.Mode);
        Assert.True(s.HydrogenKgPerHour > 0);
    }

    [Fact]
    public void AboveRated_IsClampedAndCurtailed()
    {
        var s = Model().Evaluate(1050.0);

        Assert.Equal(ElectrolyzerMode.Saturated, s.Mode);
        Assert.Equal(1000.0, s.PowerUsedKw, 6);
        Assert.Equal(50.0, s.CurtailedKw, 6);
    }

    [Fact]
    public void OneYearOfOperation_RaisesSpecificEnergyByDegradationRate()
    {
        var s = Model().Evaluate(1000.0, operatingHours: 8760.0);

        Assert.Equal(50.5, s.SpecificEnergyKwhPerKg, 6);                 // 50 * 1.01
        Assert.Equal(1000.0 / 50.5, s.HydrogenKgPerHour, 6);
    }

    [Fact]
    public void InvalidParameters_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ElectrolyzerModel(new ElectrolyzerParameters { RatedPowerKw = 0 }));
    }
}

public class CompressionModelTests
{
    [Fact]
    public void NoPressureRise_CostsNothing()
        => Assert.Equal(0.0, CompressionModel.EnergyKwhPerKg(30, 30, 3));

    [Fact]
    public void ThirtyTo350Bar_ThreeStages_MatchesHandCalculation()
        => Assert.Equal(1.2712, CompressionModel.EnergyKwhPerKg(30, 350, 3), 3);

    [Fact]
    public void ThirtyTo700Bar_FourStages_MatchesHandCalculation()
        => Assert.Equal(1.6222, CompressionModel.EnergyKwhPerKg(30, 700, 4), 3);

    [Fact]
    public void HigherOutletPressure_CostsMoreEnergy()
        => Assert.True(CompressionModel.EnergyKwhPerKg(30, 700, 4) > CompressionModel.EnergyKwhPerKg(30, 350, 4));

    [Fact]
    public void OutletBelowInlet_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => CompressionModel.EnergyKwhPerKg(350, 30, 3));
}

public class LcohCalculatorTests
{
    [Fact]
    public void CapexOnly_TwoYears_MatchesHandCalculation()
    {
        var years = new[]
        {
            new YearlyProjection(0, 0, 0, 100),
            new YearlyProjection(0, 0, 0, 100)
        };

        var r = LcohCalculator.Calculate(1000, 0.10, years);

        // 1000 / (100/1.1 + 100/1.21)
        Assert.Equal(5.7619, r.LcohUsdPerKg, 3);
    }

    [Fact]
    public void WithOpexAndElectricity_MatchesHandCalculation()
    {
        var years = new[]
        {
            new YearlyProjection(50, 100, 0, 100),
            new YearlyProjection(50, 100, 0, 100)
        };

        var r = LcohCalculator.Calculate(1000, 0.10, years);

        Assert.Equal(7.2619, r.LcohUsdPerKg, 3);
    }

    [Fact]
    public void ZeroDiscountRate_IsPlainAverage()
    {
        var years = new[] { new YearlyProjection(10, 20, 0, 50), new YearlyProjection(10, 20, 30, 50) };

        var r = LcohCalculator.Calculate(100, 0.0, years);

        Assert.Equal((100 + 30 + 60) / 100.0, r.LcohUsdPerKg, 6);
    }

    [Fact]
    public void NoHydrogen_Throws()
        => Assert.Throws<InvalidOperationException>(() =>
            LcohCalculator.Calculate(1000, 0.08, new[] { new YearlyProjection(1, 1, 0, 0) }));

    [Fact]
    public void EmptyYears_Throws()
        => Assert.Throws<ArgumentException>(() => LcohCalculator.Calculate(1000, 0.08, Array.Empty<YearlyProjection>()));
}
