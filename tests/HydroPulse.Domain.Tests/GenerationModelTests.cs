using HydroPulse.Domain.Engineering;
using Xunit;

namespace HydroPulse.Domain.Tests;

public class NasaPowerCsvParserTests
{
    private static readonly string[] Sample =
    {
        "-BEGIN HEADER-",
        "NASA/POWER Source Native Resolution Hourly Data ",
        "Location: Latitude  2.5   Longitude 36.75 ",
        "Elevation from MERRA-2: Average for 0.5 x 0.625 degree lat/lon region = 873.14 meters",
        "The value for missing source data that cannot be computed or is outside of the sources availability range:-999 ",
        "-END HEADER-",
        "YEAR,MO,DY,HR,ALLSKY_SFC_SW_DWN,WS50M,WS10M,T2M  ",
        "2024,1,1,0,0.0,5.6,3.07,22.5",
        "2024,1,1,1,0.0,5.55,3.04,22.03",
        "2024,1,1,2,-999,5.35,-999,21.55",
        "2024,1,1,3,3.42,5.1,2.89,21.44",
        ""
    };

    [Fact]
    public void Parse_ReadsHeaderElevationAndRows()
    {
        var w = NasaPowerCsvParser.Parse("Turkana", Sample);

        Assert.Equal("Turkana", w.Name);
        Assert.Equal(873.14, w.ElevationM, 2);
        Assert.Equal(4, w.Samples.Count);
        Assert.Equal(new DateTime(2024, 1, 1, 3, 0, 0, DateTimeKind.Utc), w.Samples[3].TimestampUtc);
        Assert.Equal(3.42, w.Samples[3].IrradianceWhPerM2, 6);
        Assert.Equal(5.1, w.Samples[3].WindSpeed50mMs, 6);
        Assert.Equal(22.5, w.Samples[0].AirTemperatureC, 6);
    }

    [Fact]
    public void Parse_CarriesForwardMissingValues()
    {
        var w = NasaPowerCsvParser.Parse("Turkana", Sample);

        Assert.Equal(2, w.FilledMissingValues);
        Assert.Equal(0.0, w.Samples[2].IrradianceWhPerM2, 6);   // previous valid irradiance
        Assert.Equal(3.04, w.Samples[2].WindSpeed10mMs, 6);     // previous valid 10 m wind
    }

    [Fact]
    public void Parse_WithoutHeaderRow_Throws() =>
        Assert.Throws<FormatException>(() => NasaPowerCsvParser.Parse("x", new[] { "garbage", "1,2,3" }));

    [Fact]
    public void Parse_MissingRequiredColumn_Throws() =>
        Assert.Throws<FormatException>(() =>
            NasaPowerCsvParser.Parse("x", new[] { "YEAR,MO,DY,HR,T2M", "2024,1,1,0,20" }));
}

public class PvModelTests
{
    [Fact]
    public void FullSun_AtStandardAirTemp_MatchesHandCalculation()
    {
        // Tcell = 25 + (45-20)/800*1000 = 56.25 C; temp factor = 1 - 0.0035*31.25 = 0.890625
        var pv = new PvParameters { CapacityKwp = 1000 };
        Assert.Equal(1000 * 0.86 * 0.890625, PvModel.PowerKw(pv, 1000, 25), 3);
    }

    [Fact]
    public void NoSun_GivesZero() =>
        Assert.Equal(0.0, PvModel.PowerKw(new PvParameters { CapacityKwp = 1000 }, 0, 25));

    [Fact]
    public void ZeroCapacity_GivesZero() =>
        Assert.Equal(0.0, PvModel.PowerKw(new PvParameters(), 800, 25));

    [Fact]
    public void HotterCells_ProduceLess()
    {
        var pv = new PvParameters { CapacityKwp = 1000 };
        Assert.True(PvModel.PowerKw(pv, 800, 35) < PvModel.PowerKw(pv, 800, 15));
    }
}

public class WindModelTests
{
    private static readonly WindParameters Turbine = new() { CapacityKw = 1000 };

    [Fact]
    public void AirDensity_AtSeaLevel15C_IsStandard() =>
        Assert.Equal(1.225, WindModel.AirDensity(0, 15), 3);

    [Fact]
    public void AirDensity_FallsWithElevation() =>
        Assert.True(WindModel.AirDensity(1600, 20) < WindModel.AirDensity(0, 20));

    [Fact]
    public void HubHeightSpeed_WithNoShear_EqualsSpeedAt50m() =>
        // v10 == v50 gives alpha 0, clamped to the 0.05 minimum, so only a small increase is allowed.
        Assert.InRange(WindModel.HubHeightSpeed(Turbine, 8.0, 8.0), 8.0, 8.0 * Math.Pow(1.6, 0.05) + 1e-9);

    [Fact]
    public void BelowCutIn_GivesZero() =>
        Assert.Equal(0.0, WindModel.PowerKw(Turbine, 1.0, 0.8, 0, 15));

    [Fact]
    public void AboveCutOut_GivesZero() =>
        Assert.Equal(0.0, WindModel.PowerKw(Turbine, 40.0, 30.0, 0, 15));

    [Fact]
    public void AboveRated_GivesCapacityTimesLosses() =>
        Assert.Equal(1000 * 0.85, WindModel.PowerKw(Turbine, 14.0, 11.0, 0, 15), 1);

    [Fact]
    public void ThinAir_ReducesOutputInTheRampRegion()
    {
        double sea = WindModel.PowerKw(Turbine, 7.0, 5.5, 0, 15);
        double high = WindModel.PowerKw(Turbine, 7.0, 5.5, 2000, 15);
        Assert.True(high < sea);
    }
}
