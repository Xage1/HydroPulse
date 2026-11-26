using HydroPulse.Domain.Engineering;
using Xunit;

namespace HydroPulse.Domain.Tests;

public class MeasuredWeatherTests
{
    private static List<string> Rows(int hours, Func<int, string> row, string header = "timestamp_utc,wind_speed_ms,ghi_w_m2,temp_c")
    {
        var lines = new List<string> { "# met mast export", header };
        var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < hours; i++) lines.Add(start.AddHours(i).ToString("yyyy-MM-ddTHH:mm:ssZ") + "," + row(i));
        return lines;
    }

    [Fact]
    public void Parse_ReadsHourlyRows_AndOptionalColumns()
    {
        var w = MeasuredWeatherCsvParser.Parse("Mast", Rows(48, i => $"8.0,{i % 24 * 10},22.5"), elevationM: 900);

        Assert.Equal(48, w.Samples.Count);
        Assert.Equal(900, w.ElevationM);
        Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), w.Samples[0].TimestampUtc);
        Assert.Equal(22.5, w.Samples[5].AirTemperatureC);
        Assert.Equal(50.0, w.Samples[5].IrradianceWhPerM2);
        Assert.Equal(0, w.FilledMissingValues);
    }

    [Fact]
    public void Parse_WithoutOptionalColumns_UsesDefaults()
    {
        var w = MeasuredWeatherCsvParser.Parse("Mast",
            Rows(24, _ => "7.0", header: "timestamp_utc,wind_speed_ms"), elevationM: 0);
        Assert.Equal(0.0, w.Samples[0].IrradianceWhPerM2);
        Assert.Equal(25.0, w.Samples[0].AirTemperatureC);
    }

    [Theory]
    [InlineData(80.0, 0.14)]
    [InlineData(100.0, 0.20)]
    [InlineData(60.0, 0.10)]
    public void Parse_ConvertsMeasuredHeight_SoTheModelRecoversTheHubHeightSpeed(double height, double alpha)
    {
        const double measured = 8.0;
        var w = MeasuredWeatherCsvParser.Parse("Mast", Rows(24, _ => $"{measured},0,25"),
            elevationM: 0, measuredHeightM: height, shearExponent: alpha);

        var s = w.Samples[0];
        double hub = WindModel.HubHeightSpeed(new WindParameters { HubHeightM = 80.0 }, s.WindSpeed50mMs, s.WindSpeed10mMs);
        double expected = measured * Math.Pow(80.0 / height, alpha);
        Assert.InRange(hub, expected - 1e-6, expected + 1e-6);
    }

    [Fact]
    public void Parse_FillsBlankAndMinus999FromThePreviousHour()
    {
        var lines = Rows(24, i => i == 3 ? ",,," : i == 4 ? "-999,-999,-999" : "9.0,100,20");
        var w = MeasuredWeatherCsvParser.Parse("Mast", lines, elevationM: 0, measuredHeightM: 50.0, shearExponent: 0.14);

        Assert.Equal(6, w.FilledMissingValues);
        Assert.Equal(w.Samples[2].WindSpeed50mMs, w.Samples[3].WindSpeed50mMs);
        Assert.Equal(w.Samples[2].WindSpeed50mMs, w.Samples[4].WindSpeed50mMs);
        Assert.Equal(100.0, w.Samples[4].IrradianceWhPerM2);
    }

    [Fact]
    public void Parse_RejectsGapsInTheHourlySequence()
    {
        var lines = Rows(30, _ => "8,0,25");
        lines.RemoveAt(10);
        Assert.Throws<FormatException>(() => MeasuredWeatherCsvParser.Parse("Mast", lines, elevationM: 0));
    }

    [Fact]
    public void Parse_RejectsMissingRequiredColumn()
    {
        var lines = Rows(24, _ => "8", header: "timestamp_utc,speed");
        Assert.Throws<FormatException>(() => MeasuredWeatherCsvParser.Parse("Mast", lines, elevationM: 0));
    }

    [Fact]
    public void Parse_RejectsTooFewRows()
    {
        Assert.Throws<FormatException>(() => MeasuredWeatherCsvParser.Parse("Mast", Rows(5, _ => "8,0,25"), elevationM: 0));
    }

    [Fact]
    public void MeasuredSeries_RunsThroughTheScenarioEngine()
    {
        // A year of measured-style data must work end to end, like the NASA series.
        var lines = Rows(8760, i => $"{7.0 + 5.0 * Math.Sin(2 * Math.PI * i / 37.0):F2},0,25");
        var w = MeasuredWeatherCsvParser.Parse("Mast", lines, elevationM: 800, measuredHeightM: 80);

        var r = ScenarioRunner.Run(TestWeather.WindPlant(OperatingMode.OffGrid), w);
        Assert.True(r.Year1.HydrogenTonnes > 0);
    }
}
