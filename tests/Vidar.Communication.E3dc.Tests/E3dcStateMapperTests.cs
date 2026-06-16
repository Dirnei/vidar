using E3dc;
using Vidar.Core.Capabilities;
using Vidar.Communication.E3dc;

namespace Vidar.Communication.E3dc.Tests;

public sealed class E3dcStateMapperTests
{
    private readonly Guid _deviceId = Guid.NewGuid();

    [Fact]
    public void MapSnapshot_ProducesSolarProductionUpdate()
    {
        var snapshot = new EmsPowerSnapshot(
            PvWatts: 3500, BatteryWatts: -1000, GridWatts: -500,
            HomeWatts: 2000, AdditionalWatts: 0,
            Soc: 75.5f, Autarky: 90.0f, SelfConsumption: 85.0f);

        var updates = E3dcStateMapper.MapSnapshot(_deviceId, snapshot);

        var solar = updates.First(u => u.Capability == CapabilityType.SolarProduction);
        Assert.Equal(3500, solar.Value);
    }

    [Fact]
    public void MapSnapshot_ProducesGridPowerUpdate()
    {
        var snapshot = new EmsPowerSnapshot(
            PvWatts: 3500, BatteryWatts: -1000, GridWatts: -500,
            HomeWatts: 2000, AdditionalWatts: 0,
            Soc: 75.5f, Autarky: 90.0f, SelfConsumption: 85.0f);

        var updates = E3dcStateMapper.MapSnapshot(_deviceId, snapshot);

        var grid = updates.First(u => u.Capability == CapabilityType.GridPower);
        Assert.Equal(-500, grid.Value);
    }

    [Fact]
    public void MapSnapshot_ProducesConsumptionUpdate()
    {
        var snapshot = new EmsPowerSnapshot(
            PvWatts: 3500, BatteryWatts: -1000, GridWatts: -500,
            HomeWatts: 2000, AdditionalWatts: 0,
            Soc: 75.5f, Autarky: 90.0f, SelfConsumption: 85.0f);

        var updates = E3dcStateMapper.MapSnapshot(_deviceId, snapshot);

        var consumption = updates.First(u => u.Capability == CapabilityType.Consumption);
        Assert.Equal(2000, consumption.Value);
    }

    [Fact]
    public void MapSnapshot_ProducesBatteryUpdate()
    {
        var snapshot = new EmsPowerSnapshot(
            PvWatts: 3500, BatteryWatts: -1000, GridWatts: -500,
            HomeWatts: 2000, AdditionalWatts: 0,
            Soc: 75.5f, Autarky: 90.0f, SelfConsumption: 85.0f);

        var updates = E3dcStateMapper.MapSnapshot(_deviceId, snapshot);

        var battery = updates.First(u => u.Capability == CapabilityType.Battery);
        Assert.Equal(75.5f, (float)(double)battery.Value, 0.1f);
    }

    [Fact]
    public void MapSnapshot_ProducesExtrasWithAllValues()
    {
        var snapshot = new EmsPowerSnapshot(
            PvWatts: 3500, BatteryWatts: -1000, GridWatts: -500,
            HomeWatts: 2000, AdditionalWatts: 100,
            Soc: 75.5f, Autarky: 90.0f, SelfConsumption: 85.0f);

        var updates = E3dcStateMapper.MapSnapshot(_deviceId, snapshot);

        var extras = updates.First(u => u.Capability == CapabilityType.Extras);
        var dict = Assert.IsType<Dictionary<string, object>>(extras.Value);
        Assert.Equal(-1000, dict["batteryWatts"]);
        Assert.Equal(100, dict["additionalWatts"]);
        Assert.Equal(90.0f, (float)(double)dict["autarky"], 0.1f);
        Assert.Equal(85.0f, (float)(double)dict["selfConsumption"], 0.1f);
    }

    [Fact]
    public void MapSnapshot_AllUpdatesHaveCorrectDeviceId()
    {
        var snapshot = new EmsPowerSnapshot(
            PvWatts: 1000, BatteryWatts: 0, GridWatts: 0,
            HomeWatts: 1000, AdditionalWatts: 0,
            Soc: 50f, Autarky: 100f, SelfConsumption: 100f);

        var updates = E3dcStateMapper.MapSnapshot(_deviceId, snapshot);

        Assert.All(updates, u => Assert.Equal(_deviceId, u.DeviceId));
    }

    [Fact]
    public void MapSnapshot_Returns5Updates()
    {
        var snapshot = new EmsPowerSnapshot(
            PvWatts: 0, BatteryWatts: 0, GridWatts: 0,
            HomeWatts: 0, AdditionalWatts: 0,
            Soc: 0f, Autarky: 0f, SelfConsumption: 0f);

        var updates = E3dcStateMapper.MapSnapshot(_deviceId, snapshot);

        Assert.Equal(5, updates.Count);
    }
}
