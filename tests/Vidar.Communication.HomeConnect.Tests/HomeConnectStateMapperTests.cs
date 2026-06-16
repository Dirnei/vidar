using System.Text.Json;
using TurboHomeConnect.Model;

namespace Vidar.Communication.HomeConnect.Tests;

public class HomeConnectStateMapperTests
{
    [Fact]
    public void MapEventItems_StatusKeys_StripsCommonPrefixes()
    {
        var items = new List<EventItem>
        {
            new("BSH.Common.Status.OperationState",
                JsonDocument.Parse("\"BSH.Common.EnumType.OperationState.Run\"").RootElement),
            new("BSH.Common.Status.DoorState",
                JsonDocument.Parse("\"BSH.Common.EnumType.DoorState.Closed\"").RootElement),
        };

        var result = HomeConnectStateMapper.MapEventItems(items);

        Assert.Equal("Run", result["OperationState"]);
        Assert.Equal("Closed", result["DoorState"]);
    }

    [Fact]
    public void MapEventItems_NumericValues_PreservesAsNumbers()
    {
        var items = new List<EventItem>
        {
            new("BSH.Common.Option.RemainingProgramTime",
                JsonDocument.Parse("1800").RootElement),
            new("Cooking.Oven.Status.CurrentCavityTemperature",
                JsonDocument.Parse("185").RootElement),
        };

        var result = HomeConnectStateMapper.MapEventItems(items);

        Assert.Equal(1800, ((JsonElement)result["RemainingProgramTime"]).GetInt32());
        Assert.Equal(185, ((JsonElement)result["CurrentCavityTemperature"]).GetInt32());
    }

    [Fact]
    public void MapEventItems_ProgramKey_StripsPrefix()
    {
        var items = new List<EventItem>
        {
            new("BSH.Common.Root.ActiveProgram",
                JsonDocument.Parse("\"Dishcare.Dishwasher.Program.Eco50\"").RootElement),
        };

        var result = HomeConnectStateMapper.MapEventItems(items);

        Assert.Equal("Dishcare.Dishwasher.Program.Eco50", result["ActiveProgram"]);
    }

    [Fact]
    public void MapEventItems_BooleanValues_PreservesAsBooleans()
    {
        var items = new List<EventItem>
        {
            new("BSH.Common.Status.RemoteControlActive",
                JsonDocument.Parse("true").RootElement),
        };

        var result = HomeConnectStateMapper.MapEventItems(items);

        Assert.True(((JsonElement)result["RemoteControlActive"]).GetBoolean());
    }

    [Fact]
    public void SimplifyKey_RemovesLastDottedSegmentPrefix()
    {
        Assert.Equal("OperationState", HomeConnectStateMapper.SimplifyKey("BSH.Common.Status.OperationState"));
        Assert.Equal("CurrentCavityTemperature", HomeConnectStateMapper.SimplifyKey("Cooking.Oven.Status.CurrentCavityTemperature"));
        Assert.Equal("Simple", HomeConnectStateMapper.SimplifyKey("Simple"));
    }

    [Fact]
    public void SimplifyEnumValue_StripsEnumTypePrefix()
    {
        Assert.Equal("Run", HomeConnectStateMapper.SimplifyEnumValue("BSH.Common.EnumType.OperationState.Run"));
        Assert.Equal("Closed", HomeConnectStateMapper.SimplifyEnumValue("BSH.Common.EnumType.DoorState.Closed"));
        Assert.Equal("NotAnEnum", HomeConnectStateMapper.SimplifyEnumValue("NotAnEnum"));
    }
}
