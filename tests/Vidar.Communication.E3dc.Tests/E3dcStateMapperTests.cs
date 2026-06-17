using E3dc;
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

        var solar = updates.First(u => u.CapabilityKey == "solarProduction");
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

        var grid = updates.First(u => u.CapabilityKey == "gridPower");
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

        var consumption = updates.First(u => u.CapabilityKey == "consumption");
        Assert.Equal(2000, consumption.Value);
    }

    [Fact]
    public void MapSnapshot_ProducesBatteryChargeUpdate()
    {
        var snapshot = new EmsPowerSnapshot(
            PvWatts: 3500, BatteryWatts: -1000, GridWatts: -500,
            HomeWatts: 2000, AdditionalWatts: 0,
            Soc: 75.5f, Autarky: 90.0f, SelfConsumption: 85.0f);

        var updates = E3dcStateMapper.MapSnapshot(_deviceId, snapshot);

        var battery = updates.First(u => u.CapabilityKey == "batteryCharge");
        Assert.Equal(75.5f, (float)(double)battery.Value, 0.1f);
    }

    [Fact]
    public void MapSnapshot_ProducesBatteryPowerUpdate()
    {
        var snapshot = new EmsPowerSnapshot(
            PvWatts: 3500, BatteryWatts: -1000, GridWatts: -500,
            HomeWatts: 2000, AdditionalWatts: 0,
            Soc: 75.5f, Autarky: 90.0f, SelfConsumption: 85.0f);

        var updates = E3dcStateMapper.MapSnapshot(_deviceId, snapshot);

        var batteryPower = updates.First(u => u.CapabilityKey == "batteryPower");
        Assert.Equal(-1000, batteryPower.Value);
    }

    [Fact]
    public void MapSnapshot_ProducesAutarkyAndSelfConsumptionUpdates()
    {
        var snapshot = new EmsPowerSnapshot(
            PvWatts: 3500, BatteryWatts: -1000, GridWatts: -500,
            HomeWatts: 2000, AdditionalWatts: 100,
            Soc: 75.5f, Autarky: 90.0f, SelfConsumption: 85.0f);

        var updates = E3dcStateMapper.MapSnapshot(_deviceId, snapshot);

        var autarky = updates.First(u => u.CapabilityKey == "autarky");
        Assert.Equal(90.0f, (float)(double)autarky.Value, 0.1f);

        var selfConsumption = updates.First(u => u.CapabilityKey == "selfConsumption");
        Assert.Equal(85.0f, (float)(double)selfConsumption.Value, 0.1f);
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
    public void MapSnapshot_Returns8Updates()
    {
        var snapshot = new EmsPowerSnapshot(
            PvWatts: 0, BatteryWatts: 0, GridWatts: 0,
            HomeWatts: 0, AdditionalWatts: 0,
            Soc: 0f, Autarky: 0f, SelfConsumption: 0f);

        var updates = E3dcStateMapper.MapSnapshot(_deviceId, snapshot);

        Assert.Equal(8, updates.Count);
    }
}
