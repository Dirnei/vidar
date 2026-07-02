using Vidar.Communication.Ecowitt;
using Vidar.Core.Capabilities;

namespace Vidar.Communication.Ecowitt.Tests;

public sealed class EcowittStateMapperTests
{
    private readonly Guid _deviceId = Guid.NewGuid();

    private static string Sample() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gw3001-sample.txt"));

    private Dictionary<string, string> SampleFields() =>
        EcowittStateMapper.ParsePayload(Sample());

    private double ValueOf(string capability)
    {
        var updates = EcowittStateMapper.Map(_deviceId, SampleFields());
        return Convert.ToDouble(updates.First(u => u.CapabilityKey == capability).Value);
    }

    [Fact]
    public void ParsePayload_ExtractsPassKey()
    {
        Assert.Equal("0E59A026586710C74B198B2A636D8837",
            EcowittStateMapper.TryGetPassKey(SampleFields()));
    }

    [Fact]
    public void ParsePayload_UrlDecodesValues()
    {
        Assert.Equal("2026-07-02 07:43:06", SampleFields()["dateutc"]);
    }

    [Fact]
    public void ParsePayload_KeysAreCaseInsensitive()
    {
        Assert.True(SampleFields().ContainsKey("passkey"));
    }

    [Theory]
    [InlineData("outdoorTemperature", 19.70)]
    [InlineData("indoorTemperature", 26.60)]
    [InlineData("pressure", 956.1)]
    [InlineData("pressureAbsolute", 956.1)]
    [InlineData("vaporPressureDeficit", 8.03)]
    [InlineData("windSpeed", 0.72)]
    [InlineData("windGust", 9.37)]
    [InlineData("windGustMax", 11.15)]
    [InlineData("eventRain", 21.31)]
    [InlineData("weeklyRain", 21.31)]
    public void Map_ConvertsImperialToMetric(string capability, double expected)
    {
        Assert.Equal(expected, ValueOf(capability), 1);
    }

    [Theory]
    [InlineData("outdoorHumidity", 65)]
    [InlineData("indoorHumidity", 46)]
    [InlineData("windDirection", 75)]
    [InlineData("solarRadiation", 660.15)]
    [InlineData("uvIndex", 6)]
    [InlineData("rainRate", 0)]
    [InlineData("dailyRain", 0)]
    public void Map_PassesThroughUnconvertedValues(string capability, double expected)
    {
        Assert.Equal(expected, ValueOf(capability), 2);
    }

    [Fact]
    public void Map_OutdoorSensorBatteryLow_IsFalseWhenZero()
    {
        var updates = EcowittStateMapper.Map(_deviceId, SampleFields());
        var batt = updates.First(u => u.CapabilityKey == "outdoorSensorBatteryLow");
        Assert.Equal(false, batt.Value);
    }

    [Fact]
    public void Map_OutdoorSensorBatteryLow_IsTrueWhenOne()
    {
        var fields = SampleFields();
        fields["wh65batt"] = "1";
        var updates = EcowittStateMapper.Map(_deviceId, fields);
        var batt = updates.First(u => u.CapabilityKey == "outdoorSensorBatteryLow");
        Assert.Equal(true, batt.Value);
    }

    [Fact]
    public void Map_AllUpdatesCarryDeviceId()
    {
        var updates = EcowittStateMapper.Map(_deviceId, SampleFields());
        Assert.All(updates, u => Assert.Equal(_deviceId, u.DeviceId));
    }

    [Fact]
    public void Map_OmitsAbsentFields()
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["tempf"] = "50.0",
        };
        var updates = EcowittStateMapper.Map(_deviceId, fields);
        Assert.Single(updates);
        Assert.Equal("outdoorTemperature", updates[0].CapabilityKey);
    }

    [Fact]
    public void Map_IgnoresDiagnosticsAndUnknownFields()
    {
        var keys = EcowittStateMapper.Map(_deviceId, SampleFields())
            .Select(u => u.CapabilityKey).ToList();
        Assert.DoesNotContain("runtime", keys);
        Assert.DoesNotContain("heap", keys);
        Assert.DoesNotContain("interval", keys);
        Assert.DoesNotContain("freq", keys);
    }

    [Fact]
    public void Map_SupportsPiezoRainFieldNames()
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["rrain_piezo"] = "0.100",
            ["drain_piezo"] = "0.200",
        };
        var updates = EcowittStateMapper.Map(_deviceId, fields);
        Assert.Equal(2.54, Convert.ToDouble(updates.First(u => u.CapabilityKey == "rainRate").Value), 2);
        Assert.Equal(5.08, Convert.ToDouble(updates.First(u => u.CapabilityKey == "dailyRain").Value), 2);
    }

    [Fact]
    public void BuildCapabilities_EmitsOnlyPresentFields()
    {
        var caps = EcowittStateMapper.BuildCapabilities(SampleFields());
        Assert.Contains(caps, c => c.Key == "outdoorTemperature" && c.Unit == UnitType.Celsius);
        Assert.Contains(caps, c => c.Key == "windSpeed" && c.Unit == UnitType.KilometersPerHour);
        Assert.Contains(caps, c => c.Key == "outdoorSensorBatteryLow" && c.Unit == UnitType.YesNo);
        Assert.DoesNotContain(caps, c => c.Key == "soilMoisture1");
    }

    [Fact]
    public void BuildMetadata_IncludesModelAndStationType()
    {
        var meta = EcowittStateMapper.BuildMetadata(SampleFields());
        Assert.Equal("Ecowitt", meta["manufacturer"]);
        Assert.Equal("GW3001", meta["model"]);
        Assert.Equal("GW3000A_V1.0.9", meta["stationtype"]);
    }

    [Fact]
    public void ParsePayload_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(EcowittStateMapper.ParsePayload(""));
    }

    [Fact]
    public void TryGetPassKey_MissingPassKey_ReturnsNull()
    {
        Assert.Null(EcowittStateMapper.TryGetPassKey(new Dictionary<string, string>()));
    }
}
