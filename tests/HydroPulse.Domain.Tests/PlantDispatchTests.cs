using HydroPulse.Domain.Engineering;
using Xunit;

namespace HydroPulse.Domain.Tests;

public class PlantDispatchTests
{
    private static readonly ElectrolyzerModel Model = new(new ElectrolyzerParameters { RatedPowerKw = 1000 });
    private static readonly double Ec = CompressionModel.EnergyKwhPerKg(30, 350, 3);

    [Fact]
    public void BelowMinimumLoad_PlantIsOnStandby()
    {
        var d = PlantDispatch.Run(Model, 50, 0, Ec);

        Assert.Equal(ElectrolyzerMode.Standby, d.Mode);
        Assert.Equal(0.0, d.TotalKw);
        Assert.Equal(0.0, d.H2KgPerHour);
    }

    [Fact]
    public void WithPlentyOfPower_StackRunsAtRating_AndCompressionIsOnTop()
    {
        var d = PlantDispatch.Run(Model, 5000, 0, Ec);

        Assert.Equal(ElectrolyzerMode.Saturated, d.Mode);
        Assert.Equal(1000.0, d.StackKw, 6);
        Assert.True(d.CompressionKw > 0);
        Assert.True(d.TotalKw < 5000);
    }

    [Fact]
    public void WhenPowerIsShort_StackAndCompressionShareIt()
    {
        var d = PlantDispatch.Run(Model, 600, 0, Ec);

        Assert.Equal(ElectrolyzerMode.PartLoad, d.Mode);
        Assert.InRange(d.TotalKw, 599.5, 600.0001);          // everything available is used
        Assert.True(d.StackKw < 600);                         // the compressor took a share
    }

    [Fact]
    public void EnergyPerKilogram_IsStackPlusCompression()
    {
        var d = PlantDispatch.Run(Model, 700, 0, Ec);
        double sec = Model.SpecificEnergy(d.StackKw / 1000.0, 0);

        Assert.Equal(sec + Ec, d.TotalKw / d.H2KgPerHour, 6);
    }

    [Fact]
    public void ScenarioRunner_AndTheLiveWorker_MakeTheSameHydrogenHourByHour()
    {
        var weather = TestWeather.Year();
        var cfg = TestWeather.WindPlant(OperatingMode.OffGrid);
        var year1 = ScenarioRunner.Run(cfg, weather).Hourly;

        double ec = CompressionModel.EnergyKwhPerKg(cfg.Compression.InletBar, cfg.Compression.OutletBar, cfg.Compression.Stages);
        var model = new ElectrolyzerModel(cfg.Electrolyzer);
        double opHours = 0;

        for (int i = 0; i < weather.Samples.Count; i++)
        {
            var s = weather.Samples[i];
            double r = WindModel.PowerKw(cfg.Wind, s.WindSpeed50mMs, s.WindSpeed10mMs, weather.ElevationM, s.AirTemperatureC);
            var d = PlantDispatch.Run(model, r, opHours, ec);
            if (d.StackKw > 0) opHours += 1.0;

            // the exported series is rounded (hydrogen to 0.01 kg/h, power to 0.1 kW)
            Assert.InRange(d.H2KgPerHour, year1.HydrogenKgPerHour[i] - 0.006, year1.HydrogenKgPerHour[i] + 0.006);
            Assert.InRange(d.TotalKw, year1.ElectrolyzerKw[i] - 0.06, year1.ElectrolyzerKw[i] + 0.06);
        }
    }
}
