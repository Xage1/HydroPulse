using HydroPulse.Domain.Engineering;
using Xunit;

namespace HydroPulse.Domain.Tests;

public class RenewableReplayTests
{
    private static readonly WeatherSeries Weather = TestWeather.Year();
    private static readonly PvParameters Solar = new() { CapacityKwp = 2000 };
    private static readonly WindParameters Wind = new() { CapacityKw = 1500 };
    private static readonly WindParameters NoWind = new();
    private static readonly PvParameters NoSolar = new();

    [Fact]
    public void AtASampleTime_MatchesTheModelOnThatSample()
    {
        var replay = new RenewableReplay(Weather, Solar, NoWind);
        var s = Weather.Samples[12];                       // midday of day one in the synthetic year
        double expected = PvModel.PowerKw(Solar, s.IrradianceWhPerM2, s.AirTemperatureC);

        Assert.True(expected > 0);
        Assert.Equal(expected, replay.PowerKw(s.TimestampUtc), 6);
    }

    [Fact]
    public void HalfwayBetweenSamples_InterpolatesWeatherLinearly()
    {
        var replay = new RenewableReplay(Weather, Solar, Wind);
        var a = Weather.Samples[10];
        var b = Weather.Samples[11];

        var mid = replay.At(a.TimestampUtc.AddMinutes(30));

        Assert.Equal((a.IrradianceWhPerM2 + b.IrradianceWhPerM2) / 2, mid.IrradianceWhPerM2, 6);
        Assert.Equal((a.WindSpeed50mMs + b.WindSpeed50mMs) / 2, mid.WindSpeed50mMs, 6);
        Assert.Equal((a.WindSpeed10mMs + b.WindSpeed10mMs) / 2, mid.WindSpeed10mMs, 6);
        Assert.Equal((a.AirTemperatureC + b.AirTemperatureC) / 2, mid.AirTemperatureC, 6);
    }

    [Fact]
    public void TheSeriesWrapsAround_InBothDirections()
    {
        var replay = new RenewableReplay(Weather, Solar, Wind);
        var t = replay.FirstUtc.AddHours(12.25);

        Assert.Equal(replay.PowerKw(t), replay.PowerKw(t.AddHours(replay.Hours)), 6);
        Assert.Equal(replay.PowerKw(t), replay.PowerKw(t.AddHours(-replay.Hours)), 6);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ThreeSecondSteps_AreSmooth(bool solar)
    {
        var replay = solar ? new RenewableReplay(Weather, Solar, NoWind) : new RenewableReplay(Weather, NoSolar, Wind);
        var t = replay.FirstUtc;
        double previous = replay.PowerKw(t);
        double largestStep = 0;

        for (int i = 1; i <= 24 * 1200; i++)               // one day in 3-second steps
        {
            double p = replay.PowerKw(t.AddSeconds(i * 3));
            largestStep = Math.Max(largestStep, Math.Abs(p - previous));
            previous = p;
        }

        Assert.True(largestStep < 1.0, $"largest 3-second change was {largestStep:F3} kW");
    }

    [Fact]
    public void ZeroCapacity_ProducesNothing()
    {
        var replay = new RenewableReplay(Weather, NoSolar, NoWind);
        Assert.Equal(0.0, replay.PowerKw(replay.FirstUtc.AddHours(12)), 9);
    }

    [Fact]
    public void FewerThanTwoSamples_IsRejected()
    {
        var one = new WeatherSeries("one", 0, Weather.Samples.Take(1).ToList(), 0);
        Assert.Throws<ArgumentException>(() => new RenewableReplay(one, Solar, Wind));
    }
}
