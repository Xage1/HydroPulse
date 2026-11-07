using HydroPulse.Domain.Entities;
using Xunit;

namespace HydroPulse.Domain.Tests;

public class ElectrolyzerTests
{
    [Fact]
    public void EvaluateOperation_WhenPowerAboveThreshold_CalculatesCorrectYield()
    {
        var stack = new ElectrolyzerStack("PEM-1MW", 1000.0, 50.0);
        double incomingPower = 500.0;
        double tariff = 0.05;

        var result = stack.EvaluateOperation(incomingPower, tariff);

        Assert.Equal("Active - Optimal Generation", result.Status);
        Assert.Equal(10.0, result.HydrogenProducedKgPerHour);
        Assert.Equal(95.0, result.WaterConsumedLitersPerHour);
        Assert.Equal(25.0, result.ElectricityCostPerHour);
    }

    [Fact]
    public void EvaluateOperation_WhenPowerBelowTurndown_ReturnsStandby()
    {
        var stack = new ElectrolyzerStack("PEM-1MW", 1000.0, 50.0, minLoadPercent: 10.0);
        double incomingPower = 50.0;

        var result = stack.EvaluateOperation(incomingPower, 0.05);

        Assert.Equal("Standby / Insufficient Power", result.Status);
        Assert.Equal(0, result.HydrogenProducedKgPerHour);
    }
}