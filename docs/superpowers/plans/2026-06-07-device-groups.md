# Device Groups Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add device groups to Vidar so collections of lights can be controlled together and appear as a single card in room views.

**Architecture:** Groups are persisted in MongoDB via a new MongoGroupRepository, exposed through a new GroupsController and a rooms sub-endpoint, and rendered in the frontend as GroupRows inside RoomCards with a dedicated GroupDetailPage. Groups appear instead of their individual member devices in room cards; member devices are still individually accessible.

**Tech Stack:** C# / .NET 10 / ASP.NET Core Minimal Controllers / MongoDB.Driver / Akka.NET cluster sharding for fan-out commands / React 18 / TypeScript / react-router-dom

---

## File Map

### New backend files
- `src/Vidar.Core/Model/GroupConfiguration.cs` — group domain model
- `src/Vidar.Host/Persistence/IGroupRepository.cs` — repository interface
- `src/Vidar.Host/Persistence/MongoGroupRepository.cs` — MongoDB implementation
- `src/Vidar.Host/Api/Dto/GroupResponse.cs` — group API response DTO
- `src/Vidar.Host/Api/Dto/CreateGroupRequest.cs` — create request DTO
- `src/Vidar.Host/Api/Dto/UpdateGroupRequest.cs` — update request DTO
- `src/Vidar.Host/Api/GroupsController.cs` — groups CRUD + command + room sub-endpoint

### Modified backend files
- `src/Vidar.Host/Persistence/BsonClassMapRegistration.cs` — register GroupConfiguration BSON map
- `src/Vidar.Host/Program.cs` — register IGroupRepository singleton
- `src/Vidar.Host/Api/RoomsController.cs` — add GET /api/rooms/{id}/groups; exclude grouped devices from GET /api/rooms/{id}/devices
- `src/Vidar.Host/Api/Dto/DeviceResponse.cs` — add GroupId? and GroupName? fields
- `src/Vidar.Host/Api/DevicesController.cs` — annotate devices with group info in GetAll

### New frontend files
- `frontend/src/components/GroupRow.tsx` — group row (link + inline controls + member count badge)
- `frontend/src/components/CreateGroupModal.tsx` — modal: name + room + device checkboxes
- `frontend/src/pages/GroupDetailPage.tsx` — group detail with edit mode, capability cards, member list

### Modified frontend files
- `frontend/src/types/index.ts` — add DeviceGroup interface; add groupId/groupName to Device
- `frontend/src/api/client.ts` — group CRUD + sendGroupCommand + getRoomGroups
- `frontend/src/pages/RoomsPage.tsx` — load groups per room; pass to RoomCard; show Create Group button
- `frontend/src/components/RoomCard.tsx` — accept groups prop; render GroupRows; filter grouped devices
- `frontend/src/pages/DevicesPage.tsx` — show group label on devices that belong to a group
- `frontend/src/App.tsx` — add `/groups/:id` route

---

## Task 1: Group domain model + repository interface

**Files:**
- Create: `src/Vidar.Core/Model/GroupConfiguration.cs`
- Create: `src/Vidar.Host/Persistence/IGroupRepository.cs`

- [ ] **Step 1: Create GroupConfiguration model**

  Create `src/Vidar.Core/Model/GroupConfiguration.cs`:
  ```csharp
  namespace Vidar.Core.Model;
  
  public sealed class GroupConfiguration
  {
      public Guid Id { get; init; }
      public required string Name { get; set; }
      public Guid RoomId { get; set; }
      public required List<Guid> DeviceIds { get; init; }
  }
  ```

- [ ] **Step 2: Create IGroupRepository interface**

  Create `src/Vidar.Host/Persistence/IGroupRepository.cs`:
  ```csharp
  using Vidar.Core.Model;
  
  namespace Vidar.Host.Persistence;
  
  public interface IGroupRepository
  {
      Task<List<GroupConfiguration>> GetAllAsync();
      Task<GroupConfiguration?> GetByIdAsync(Guid id);
      Task<List<GroupConfiguration>> GetByRoomIdAsync(Guid roomId);
      Task CreateAsync(GroupConfiguration group);
      Task UpdateAsync(GroupConfiguration group);
      Task DeleteAsync(Guid id);
  }
  ```

- [ ] **Step 3: Verify it compiles**

  Run: `dotnet build C:\code\vidar\Vidar.slnx`
  Expected: Build succeeded, 0 errors

- [ ] **Step 4: Commit**

  ```
  git add src/Vidar.Core/Model/GroupConfiguration.cs src/Vidar.Host/Persistence/IGroupRepository.cs
  git commit -m "feat: add GroupConfiguration model and IGroupRepository interface"
  ```

---

## Task 2: MongoDB group repository + BSON registration + DI

**Files:**
- Create: `src/Vidar.Host/Persistence/MongoGroupRepository.cs`
- Modify: `src/Vidar.Host/Persistence/BsonClassMapRegistration.cs`
- Modify: `src/Vidar.Host/Program.cs`

- [ ] **Step 1: Create MongoGroupRepository**

  Create `src/Vidar.Host/Persistence/MongoGroupRepository.cs`:
  ```csharp
  using MongoDB.Driver;
  using Vidar.Core.Model;
  
  namespace Vidar.Host.Persistence;
  
  public sealed class MongoGroupRepository : IGroupRepository
  {
      private readonly IMongoCollection<GroupConfiguration> _collection;
  
      public MongoGroupRepository(IMongoDatabase database)
      {
          _collection = database.GetCollection<GroupConfiguration>("groups");
      }
  
      public async Task<List<GroupConfiguration>> GetAllAsync() =>
          await _collection.Find(Builders<GroupConfiguration>.Filter.Empty).ToListAsync();
  
      public async Task<GroupConfiguration?> GetByIdAsync(Guid id) =>
          await _collection.Find(Builders<GroupConfiguration>.Filter.Eq(g => g.Id, id))
              .FirstOrDefaultAsync();
  
      public async Task<List<GroupConfiguration>> GetByRoomIdAsync(Guid roomId) =>
          await _collection.Find(Builders<GroupConfiguration>.Filter.Eq(g => g.RoomId, roomId))
              .ToListAsync();
  
      public async Task CreateAsync(GroupConfiguration group) =>
          await _collection.InsertOneAsync(group);
  
      public async Task UpdateAsync(GroupConfiguration group) =>
          await _collection.ReplaceOneAsync(
              Builders<GroupConfiguration>.Filter.Eq(g => g.Id, group.Id),
              group);
  
      public async Task DeleteAsync(Guid id) =>
          await _collection.DeleteOneAsync(
              Builders<GroupConfiguration>.Filter.Eq(g => g.Id, id));
  }
  ```

- [ ] **Step 2: Register BSON class map for GroupConfiguration**

  Read `src/Vidar.Host/Persistence/BsonClassMapRegistration.cs` (already done above). Add inside the `Register()` method, after the `DeviceConfiguration` registration:
  ```csharp
  BsonClassMap.RegisterClassMap<GroupConfiguration>(cm =>
  {
      cm.AutoMap();
      cm.MapIdProperty(g => g.Id);
  });
  ```

  The `Register()` method after the change (showing relevant region around line 38):
  ```csharp
              BsonClassMap.RegisterClassMap<DeviceConfiguration>(cm =>
              {
                  cm.AutoMap();
                  cm.MapIdProperty(d => d.Id);
              });
  
              BsonClassMap.RegisterClassMap<GroupConfiguration>(cm =>
              {
                  cm.AutoMap();
                  cm.MapIdProperty(g => g.Id);
              });
  
              BsonClassMap.RegisterClassMap<DiscoveredDevice>(cm =>
  ```

  Also add `using Vidar.Core.Model;` at the top if not already present (it already is in the file via the existing code).

- [ ] **Step 3: Register IGroupRepository in DI**

  In `src/Vidar.Host/Program.cs`, add after line 28 (`AddSingleton<IDeviceStateRepository>`):
  ```csharp
  builder.Services.AddSingleton<IGroupRepository>(new MongoGroupRepository(database));
  ```

- [ ] **Step 4: Verify build**

  Run: `dotnet build C:\code\vidar\Vidar.slnx`
  Expected: Build succeeded, 0 errors

- [ ] **Step 5: Commit**

  ```
  git add src/Vidar.Host/Persistence/MongoGroupRepository.cs src/Vidar.Host/Persistence/BsonClassMapRegistration.cs src/Vidar.Host/Program.cs
  git commit -m "feat: add MongoGroupRepository and wire up DI + BSON"
  ```

---

## Task 3: Group DTOs

**Files:**
- Create: `src/Vidar.Host/Api/Dto/GroupResponse.cs`
- Create: `src/Vidar.Host/Api/Dto/CreateGroupRequest.cs`
- Create: `src/Vidar.Host/Api/Dto/UpdateGroupRequest.cs`

- [ ] **Step 1: Create GroupResponse DTO**

  Create `src/Vidar.Host/Api/Dto/GroupResponse.cs`:
  ```csharp
  namespace Vidar.Host.Api.Dto;
  
  public sealed record GroupResponse(
      Guid Id,
      string Name,
      Guid RoomId,
      string? RoomName,
      List<Guid> DeviceIds,
      List<string> Capabilities,
      Dictionary<string, object>? State,
      bool? Online);
  ```

- [ ] **Step 2: Create CreateGroupRequest DTO**

  Create `src/Vidar.Host/Api/Dto/CreateGroupRequest.cs`:
  ```csharp
  namespace Vidar.Host.Api.Dto;
  
  public sealed record CreateGroupRequest(string Name, Guid RoomId, List<Guid> DeviceIds);
  ```

- [ ] **Step 3: Create UpdateGroupRequest DTO**

  Create `src/Vidar.Host/Api/Dto/UpdateGroupRequest.cs`:
  ```csharp
  namespace Vidar.Host.Api.Dto;
  
  public sealed record UpdateGroupRequest(string Name, Guid RoomId, List<Guid> DeviceIds);
  ```

- [ ] **Step 4: Verify build**

  Run: `dotnet build C:\code\vidar\Vidar.slnx`
  Expected: Build succeeded, 0 errors

- [ ] **Step 5: Commit**

  ```
  git add src/Vidar.Host/Api/Dto/GroupResponse.cs src/Vidar.Host/Api/Dto/CreateGroupRequest.cs src/Vidar.Host/Api/Dto/UpdateGroupRequest.cs
  git commit -m "feat: add group DTOs"
  ```

---

## Task 4: GroupsController

**Files:**
- Create: `src/Vidar.Host/Api/GroupsController.cs`

- [ ] **Step 1: Create GroupsController**

  Create `src/Vidar.Host/Api/GroupsController.cs`:
  ```csharp
  using Akka.Hosting;
  using Microsoft.AspNetCore.Mvc;
  using Vidar.Core.Capabilities;
  using Vidar.Core.Messages;
  using Vidar.Core.Model;
  using Vidar.Core.Sharding;
  using Vidar.Host.Api.Dto;
  using Vidar.Host.Persistence;
  
  namespace Vidar.Host.Api;
  
  [ApiController]
  [Route("api/groups")]
  public sealed class GroupsController : ControllerBase
  {
      private readonly IGroupRepository _groupRepo;
      private readonly IDeviceRepository _deviceRepo;
      private readonly IDeviceStateRepository _stateRepo;
      private readonly IRoomRepository _roomRepo;
      private readonly IRequiredActor<DeviceTwinRegion> _twinRegion;
      private readonly ILogger<GroupsController> _logger;
  
      public GroupsController(
          IGroupRepository groupRepo,
          IDeviceRepository deviceRepo,
          IDeviceStateRepository stateRepo,
          IRoomRepository roomRepo,
          IRequiredActor<DeviceTwinRegion> twinRegion,
          ILogger<GroupsController> logger)
      {
          _groupRepo = groupRepo;
          _deviceRepo = deviceRepo;
          _stateRepo = stateRepo;
          _roomRepo = roomRepo;
          _twinRegion = twinRegion;
          _logger = logger;
      }
  
      [HttpGet]
      public async Task<IActionResult> GetAll()
      {
          var groups = await _groupRepo.GetAllAsync();
          var rooms = await _roomRepo.GetAllAsync();
          var roomMap = rooms.ToDictionary(r => r.Id, r => r.Name);
          var devices = await _deviceRepo.GetAllAsync();
          var deviceMap = devices.ToDictionary(d => d.Id);
          var states = await _stateRepo.GetAllAsync();
          var stateMap = states.ToDictionary(s => s.DeviceId);
  
          var response = groups.Select(g => BuildGroupResponse(g, roomMap, deviceMap, stateMap)).ToList();
          return Ok(response);
      }
  
      [HttpGet("{id:guid}")]
      public async Task<IActionResult> GetById(Guid id)
      {
          var group = await _groupRepo.GetByIdAsync(id);
          if (group == null) return NotFound();
  
          var rooms = await _roomRepo.GetAllAsync();
          var roomMap = rooms.ToDictionary(r => r.Id, r => r.Name);
          var devices = await _deviceRepo.GetAllAsync();
          var deviceMap = devices.ToDictionary(d => d.Id);
          var states = await _stateRepo.GetAllAsync();
          var stateMap = states.ToDictionary(s => s.DeviceId);
  
          return Ok(BuildGroupResponse(group, roomMap, deviceMap, stateMap));
      }
  
      [HttpGet("by-room/{roomId:guid}")]
      public async Task<IActionResult> GetByRoom(Guid roomId)
      {
          var room = await _roomRepo.GetByIdAsync(roomId);
          if (room == null) return NotFound();
  
          var groups = await _groupRepo.GetByRoomIdAsync(roomId);
          var roomMap = new Dictionary<Guid, string> { [roomId] = room.Name };
          var devices = await _deviceRepo.GetAllAsync();
          var deviceMap = devices.ToDictionary(d => d.Id);
          var states = await _stateRepo.GetAllAsync();
          var stateMap = states.ToDictionary(s => s.DeviceId);
  
          var response = groups.Select(g => BuildGroupResponse(g, roomMap, deviceMap, stateMap)).ToList();
          return Ok(response);
      }
  
      [HttpPost]
      public async Task<IActionResult> Create([FromBody] CreateGroupRequest request)
      {
          var group = new GroupConfiguration
          {
              Id = Guid.NewGuid(),
              Name = request.Name,
              RoomId = request.RoomId,
              DeviceIds = request.DeviceIds,
          };
          await _groupRepo.CreateAsync(group);
  
          var rooms = await _roomRepo.GetAllAsync();
          var roomMap = rooms.ToDictionary(r => r.Id, r => r.Name);
          var devices = await _deviceRepo.GetAllAsync();
          var deviceMap = devices.ToDictionary(d => d.Id);
          var states = await _stateRepo.GetAllAsync();
          var stateMap = states.ToDictionary(s => s.DeviceId);
  
          return CreatedAtAction(nameof(GetById), new { id = group.Id },
              BuildGroupResponse(group, roomMap, deviceMap, stateMap));
      }
  
      [HttpPut("{id:guid}")]
      public async Task<IActionResult> Update(Guid id, [FromBody] UpdateGroupRequest request)
      {
          var group = await _groupRepo.GetByIdAsync(id);
          if (group == null) return NotFound();
  
          group.Name = request.Name;
          group.RoomId = request.RoomId;
          // DeviceIds is init-only; reconstruct the group to change it
          var updated = new GroupConfiguration
          {
              Id = group.Id,
              Name = request.Name,
              RoomId = request.RoomId,
              DeviceIds = request.DeviceIds,
          };
          await _groupRepo.UpdateAsync(updated);
          return NoContent();
      }
  
      [HttpDelete("{id:guid}")]
      public async Task<IActionResult> Delete(Guid id)
      {
          var group = await _groupRepo.GetByIdAsync(id);
          if (group == null) return NotFound();
          await _groupRepo.DeleteAsync(id);
          return NoContent();
      }
  
      [HttpPost("{id:guid}/command")]
      public async Task<IActionResult> SendCommand(Guid id, [FromBody] DeviceCommandRequest request)
      {
          var group = await _groupRepo.GetByIdAsync(id);
          if (group == null) return NotFound();
  
          var devices = await _deviceRepo.GetAllAsync();
          var deviceMap = devices.ToDictionary(d => d.Id);
          var region = _twinRegion.ActorRef;
          var value = UnwrapJsonElement(request.Value) ?? request.Value;
  
          foreach (var deviceId in group.DeviceIds)
          {
              if (!deviceMap.TryGetValue(deviceId, out var device)) continue;
              var command = new DeviceCommand(deviceId, device.CommunicationType, device.NativeId, request.Capability, value);
              _logger.LogInformation("Group {GroupId}: sending command {Capability}={Value} to device {DeviceId}",
                  id, request.Capability, value, deviceId);
              region.Tell(command);
          }
  
          return Accepted();
      }
  
      // --- helpers ---
  
      private static GroupResponse BuildGroupResponse(
          GroupConfiguration group,
          Dictionary<Guid, string> roomMap,
          Dictionary<Guid, DeviceConfiguration> deviceMap,
          Dictionary<Guid, DeviceState> stateMap)
      {
          roomMap.TryGetValue(group.RoomId, out var roomName);
  
          // Capabilities = intersection of all member device capabilities
          List<string> capabilities;
          if (group.DeviceIds.Count == 0)
          {
              capabilities = [];
          }
          else
          {
              var first = true;
              HashSet<string>? capSet = null;
              foreach (var did in group.DeviceIds)
              {
                  if (!deviceMap.TryGetValue(did, out var dev)) continue;
                  var devCaps = dev.Capabilities.Select(c => c.ToString()).ToHashSet();
                  if (first) { capSet = devCaps; first = false; }
                  else capSet!.IntersectWith(devCaps);
              }
              capabilities = capSet?.ToList() ?? [];
          }
  
          // Leader = first resolvable device
          DeviceState? leaderState = null;
          bool? online = null;
          foreach (var did in group.DeviceIds)
          {
              if (!deviceMap.ContainsKey(did)) continue;
              stateMap.TryGetValue(did, out leaderState);
              online = leaderState?.Online;
              break;
          }
  
          var stateDict = leaderState?.States.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value);
  
          return new GroupResponse(group.Id, group.Name, group.RoomId, roomName,
              group.DeviceIds, capabilities, stateDict, online);
      }
  
      private static object? UnwrapJsonElement(object? value)
      {
          if (value is not System.Text.Json.JsonElement el) return value;
          return el.ValueKind switch
          {
              System.Text.Json.JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
              System.Text.Json.JsonValueKind.True => true,
              System.Text.Json.JsonValueKind.False => false,
              System.Text.Json.JsonValueKind.String => el.GetString(),
              _ => value
          };
      }
  }
  ```

  Note: `DeviceState` is used from `Vidar.Core.Model` — check the namespace. If it's in a different namespace, add the correct using. Look at `MongoDeviceStateRepository.cs` for guidance if needed.

- [ ] **Step 2: Verify build**

  Run: `dotnet build C:\code\vidar\Vidar.slnx`
  Expected: Build succeeded, 0 errors. Fix any namespace issues (e.g., add `using Vidar.Core.Model;` or the correct namespace for `DeviceState`).

- [ ] **Step 3: Commit**

  ```
  git add src/Vidar.Host/Api/GroupsController.cs
  git commit -m "feat: add GroupsController with CRUD, room filter, and fan-out command"
  ```

---

## Task 5: Update RoomsController — groups sub-endpoint + exclude grouped devices

**Files:**
- Modify: `src/Vidar.Host/Api/RoomsController.cs`

- [ ] **Step 1: Inject IGroupRepository and add /api/rooms/{id}/groups endpoint**

  Read `src/Vidar.Host/Api/RoomsController.cs` (already done — 80 lines). Replace the entire file content:

  ```csharp
  using Microsoft.AspNetCore.Mvc;
  using Vidar.Core.Model;
  using Vidar.Host.Api.Dto;
  using Vidar.Host.Persistence;
  
  namespace Vidar.Host.Api;
  
  [ApiController]
  [Route("api/rooms")]
  public sealed class RoomsController : ControllerBase
  {
      private readonly IRoomRepository _roomRepo;
      private readonly IDeviceRepository _deviceRepo;
      private readonly IDeviceStateRepository _stateRepo;
      private readonly IGroupRepository _groupRepo;
  
      public RoomsController(IRoomRepository roomRepo, IDeviceRepository deviceRepo, IDeviceStateRepository stateRepo, IGroupRepository groupRepo)
      {
          _roomRepo = roomRepo;
          _deviceRepo = deviceRepo;
          _stateRepo = stateRepo;
          _groupRepo = groupRepo;
      }
  
      [HttpGet]
      public async Task<IActionResult> GetAll()
      {
          var rooms = await _roomRepo.GetAllAsync();
          var response = new List<RoomResponse>();
          foreach (var room in rooms)
          {
              var devices = await _deviceRepo.GetByRoomIdAsync(room.Id);
              response.Add(new RoomResponse(room.Id, room.Name, devices.Count));
          }
          return Ok(response);
      }
  
      [HttpPost]
      public async Task<IActionResult> Create([FromBody] CreateRoomRequest request)
      {
          var room = new RoomConfiguration { Id = Guid.NewGuid(), Name = request.Name };
          await _roomRepo.CreateAsync(room);
          var response = new RoomResponse(room.Id, room.Name, 0);
          return CreatedAtAction(nameof(GetAll), new { id = room.Id }, response);
      }
  
      [HttpPut("{id:guid}")]
      public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRoomRequest request)
      {
          var room = await _roomRepo.GetByIdAsync(id);
          if (room == null) return NotFound();
          room.Name = request.Name;
          await _roomRepo.UpdateAsync(room);
          return NoContent();
      }
  
      [HttpDelete("{id:guid}")]
      public async Task<IActionResult> Delete(Guid id)
      {
          var room = await _roomRepo.GetByIdAsync(id);
          if (room == null) return NotFound();
          await _roomRepo.DeleteAsync(id);
          return NoContent();
      }
  
      [HttpGet("{id:guid}/devices")]
      public async Task<IActionResult> GetDevices(Guid id)
      {
          var room = await _roomRepo.GetByIdAsync(id);
          if (room == null) return NotFound();
          var devices = await _deviceRepo.GetByRoomIdAsync(id);
          var groups = await _groupRepo.GetByRoomIdAsync(id);
  
          // Collect all device IDs that belong to any group in this room
          var groupedDeviceIds = groups.SelectMany(g => g.DeviceIds).ToHashSet();
  
          var states = await _stateRepo.GetAllAsync();
          var stateMap = states.ToDictionary(s => s.DeviceId);
  
          var response = devices
              .Where(d => !groupedDeviceIds.Contains(d.Id))
              .Select(d =>
              {
                  stateMap.TryGetValue(d.Id, out var state);
                  var stateDict = state?.States.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value);
                  return new DeviceResponse(d.Id, d.Name, d.RoomId, room.Name, d.CommunicationType, d.Capabilities, stateDict, state?.Online, d.Settings, null, null);
              }).ToList();
          return Ok(response);
      }
  
      [HttpGet("{id:guid}/groups")]
      public async Task<IActionResult> GetGroups(Guid id)
      {
          var room = await _roomRepo.GetByIdAsync(id);
          if (room == null) return NotFound();
  
          var groups = await _groupRepo.GetByRoomIdAsync(id);
          var devices = await _deviceRepo.GetAllAsync();
          var deviceMap = devices.ToDictionary(d => d.Id);
          var states = await _stateRepo.GetAllAsync();
          var stateMap = states.ToDictionary(s => s.DeviceId);
          var roomMap = new Dictionary<Guid, string> { [id] = room.Name };
  
          var response = groups.Select(g => BuildGroupResponse(g, roomMap, deviceMap, stateMap)).ToList();
          return Ok(response);
      }
  
      private static GroupResponse BuildGroupResponse(
          Vidar.Core.Model.GroupConfiguration group,
          Dictionary<Guid, string> roomMap,
          Dictionary<Guid, Vidar.Core.Model.DeviceConfiguration> deviceMap,
          Dictionary<Guid, Vidar.Core.Model.DeviceState> stateMap)
      {
          roomMap.TryGetValue(group.RoomId, out var roomName);
  
          List<string> capabilities;
          if (group.DeviceIds.Count == 0)
          {
              capabilities = [];
          }
          else
          {
              var first = true;
              HashSet<string>? capSet = null;
              foreach (var did in group.DeviceIds)
              {
                  if (!deviceMap.TryGetValue(did, out var dev)) continue;
                  var devCaps = dev.Capabilities.Select(c => c.ToString()).ToHashSet();
                  if (first) { capSet = devCaps; first = false; }
                  else capSet!.IntersectWith(devCaps);
              }
              capabilities = capSet?.ToList() ?? [];
          }
  
          Vidar.Core.Model.DeviceState? leaderState = null;
          bool? online = null;
          foreach (var did in group.DeviceIds)
          {
              if (!deviceMap.ContainsKey(did)) continue;
              stateMap.TryGetValue(did, out leaderState);
              online = leaderState?.Online;
              break;
          }
  
          var stateDict = leaderState?.States.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value);
          return new GroupResponse(group.Id, group.Name, group.RoomId, roomName,
              group.DeviceIds, capabilities, stateDict, online);
      }
  }
  ```

  Note: `DeviceResponse` now takes two extra nullable parameters at the end (`null, null`) for `GroupId` and `GroupName`. This will only compile after Task 6 updates DeviceResponse. If building before Task 6, add `GroupId` and `GroupName` to DeviceResponse first.

- [ ] **Step 2: Verify build**

  Run: `dotnet build C:\code\vidar\Vidar.slnx`
  Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

  ```
  git add src/Vidar.Host/Api/RoomsController.cs
  git commit -m "feat: add /api/rooms/{id}/groups endpoint, exclude grouped devices from /devices"
  ```

---

## Task 6: Update DeviceResponse and DevicesController

**Files:**
- Modify: `src/Vidar.Host/Api/Dto/DeviceResponse.cs`
- Modify: `src/Vidar.Host/Api/DevicesController.cs`

- [ ] **Step 1: Add GroupId and GroupName to DeviceResponse**

  Replace `src/Vidar.Host/Api/Dto/DeviceResponse.cs` with:
  ```csharp
  using Vidar.Core.Capabilities;
  
  namespace Vidar.Host.Api.Dto;
  
  public sealed record DeviceResponse(
      Guid Id,
      string Name,
      Guid RoomId,
      string? RoomName,
      string CommunicationType,
      List<CapabilityType> Capabilities,
      Dictionary<string, object>? State,
      bool? Online,
      Dictionary<string, string>? Settings,
      Guid? GroupId,
      string? GroupName);
  ```

- [ ] **Step 2: Update DevicesController.GetAll to annotate devices with group info**

  In `src/Vidar.Host/Api/DevicesController.cs`:

  1. Add `IGroupRepository _groupRepo` field and inject it in the constructor.
  2. In `GetAll()`, load all groups and build a lookup map from deviceId → group.
  3. Pass `GroupId` and `GroupName` when constructing `DeviceResponse`.
  4. Update `GetById` to also pass `null, null` for the new parameters.
  5. In `Update`, no change needed.

  Full updated file content:
  ```csharp
  using Akka.Actor;
  using Akka.Cluster.Tools.PublishSubscribe;
  using Akka.Hosting;
  using Microsoft.AspNetCore.Mvc;
  using Vidar.Core.Messages;
  using Vidar.Core.Sharding;
  using Vidar.Host.Api.Dto;
  using Vidar.Host.Persistence;
  
  namespace Vidar.Host.Api;
  
  [ApiController]
  [Route("api/devices")]
  public sealed class DevicesController : ControllerBase
  {
      private readonly IDeviceRepository _deviceRepo;
      private readonly IDeviceStateRepository _stateRepo;
      private readonly IRoomRepository _roomRepo;
      private readonly IGroupRepository _groupRepo;
      private readonly IRequiredActor<DeviceTwinRegion> _twinRegion;
      private readonly ActorSystem _actorSystem;
      private readonly ILogger<DevicesController> _logger;
  
      public DevicesController(
          IDeviceRepository deviceRepo,
          IDeviceStateRepository stateRepo,
          IRoomRepository roomRepo,
          IGroupRepository groupRepo,
          IRequiredActor<DeviceTwinRegion> twinRegion,
          ActorSystem actorSystem,
          ILogger<DevicesController> logger)
      {
          _deviceRepo = deviceRepo;
          _stateRepo = stateRepo;
          _roomRepo = roomRepo;
          _groupRepo = groupRepo;
          _twinRegion = twinRegion;
          _actorSystem = actorSystem;
          _logger = logger;
      }
  
      [HttpGet]
      public async Task<IActionResult> GetAll()
      {
          var devices = await _deviceRepo.GetAllAsync();
          var states = await _stateRepo.GetAllAsync();
          var rooms = await _roomRepo.GetAllAsync();
          var groups = await _groupRepo.GetAllAsync();
          var stateMap = states.ToDictionary(s => s.DeviceId);
          var roomMap = rooms.ToDictionary(r => r.Id, r => r.Name);
  
          // Build deviceId → group lookup
          var deviceGroupMap = new Dictionary<Guid, (Guid GroupId, string GroupName)>();
          foreach (var g in groups)
          {
              foreach (var did in g.DeviceIds)
                  deviceGroupMap[did] = (g.Id, g.Name);
          }
  
          var response = devices.Select(d =>
          {
              stateMap.TryGetValue(d.Id, out var state);
              roomMap.TryGetValue(d.RoomId, out var roomName);
              var stateDict = state?.States.ToDictionary(
                  kvp => kvp.Key.ToString(),
                  kvp => kvp.Value);
              deviceGroupMap.TryGetValue(d.Id, out var groupInfo);
              return new DeviceResponse(d.Id, d.Name, d.RoomId, roomName, d.CommunicationType, d.Capabilities,
                  stateDict, state?.Online, d.Settings,
                  groupInfo == default ? null : groupInfo.GroupId,
                  groupInfo == default ? null : groupInfo.GroupName);
          }).ToList();
  
          return Ok(response);
      }
  
      [HttpGet("{id:guid}")]
      public async Task<IActionResult> GetById(Guid id)
      {
          var device = await _deviceRepo.GetByIdAsync(id);
          if (device == null) return NotFound();
  
          var state = await _stateRepo.GetByDeviceIdAsync(id);
          var room = await _roomRepo.GetByIdAsync(device.RoomId);
          var stateDict = state?.States.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value);
  
          return Ok(new DeviceResponse(
              device.Id, device.Name, device.RoomId, room?.Name,
              device.CommunicationType, device.Capabilities, stateDict, state?.Online, device.Settings,
              null, null));
      }
  
      [HttpPost("{id:guid}/command")]
      public async Task<IActionResult> SendCommand(Guid id, [FromBody] DeviceCommandRequest request)
      {
          var device = await _deviceRepo.GetByIdAsync(id);
          if (device == null) return NotFound();
  
          var value = UnwrapJsonElement(request.Value) ?? request.Value;
          var command = new DeviceCommand(id, device.CommunicationType, device.NativeId, request.Capability, value);
          _logger.LogInformation("Sending command {Capability}={Value} ({Type}) to device {DeviceId}",
              request.Capability, value, value?.GetType().Name ?? "null", id);
          var region = _twinRegion.ActorRef;
          region.Tell(command);
          return Accepted();
      }
  
      private static object? UnwrapJsonElement(object? value)
      {
          if (value is not System.Text.Json.JsonElement el) return value;
          return el.ValueKind switch
          {
              System.Text.Json.JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
              System.Text.Json.JsonValueKind.True => true,
              System.Text.Json.JsonValueKind.False => false,
              System.Text.Json.JsonValueKind.String => el.GetString(),
              _ => value
          };
      }
  
      [HttpPut("{id:guid}")]
      public async Task<IActionResult> Update(Guid id, [FromBody] ConfigureDeviceRequest request)
      {
          var device = await _deviceRepo.GetByIdAsync(id);
          if (device == null) return NotFound();
          device.Name = request.Name;
          device.RoomId = request.RoomId;
          await _deviceRepo.UpdateAsync(device);
          return NoContent();
      }
  
      [HttpPut("{id:guid}/settings")]
      public async Task<IActionResult> UpdateSettings(Guid id, [FromBody] UpdateDeviceSettingsRequest request)
      {
          var device = await _deviceRepo.GetByIdAsync(id);
          if (device == null) return NotFound();
  
          var oldHost = device.Settings.GetValueOrDefault("host");
  
          foreach (var kvp in request.Settings)
              device.Settings[kvp.Key] = kvp.Value;
  
          await _deviceRepo.UpdateAsync(device);
  
          var newHost = device.Settings.GetValueOrDefault("host");
          if (newHost != null && newHost != oldHost && device.CommunicationType == "shelly")
          {
              int.TryParse(device.Settings.GetValueOrDefault("generation", "2"), out var generation);
              var msg = new RegisterDeviceForPolling(
                  device.Id,
                  device.CommunicationType,
                  device.NativeId,
                  newHost,
                  generation,
                  device.Capabilities);
              var mediator = DistributedPubSub.Get(_actorSystem).Mediator;
              mediator.Tell(new Publish("register.shelly", msg));
              _logger.LogInformation("Republished RegisterDeviceForPolling for device {DeviceId} with new host {Host}", id, newHost);
          }
  
          return NoContent();
      }
  
      [HttpDelete("{id:guid}")]
      public async Task<IActionResult> Delete(Guid id)
      {
          var device = await _deviceRepo.GetByIdAsync(id);
          if (device == null) return NotFound();
          await _deviceRepo.DeleteAsync(id);
          return NoContent();
      }
  }
  ```

- [ ] **Step 3: Fix any remaining callers of DeviceResponse with the old arity**

  Search for `new DeviceResponse(` in the codebase. Any place with 9 arguments needs two more `null, null` appended. The affected file is `RoomsController.cs` (already handled in Task 5 with `null, null`). Run the build to confirm.

  Run: `dotnet build C:\code\vidar\Vidar.slnx`
  Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

  ```
  git add src/Vidar.Host/Api/Dto/DeviceResponse.cs src/Vidar.Host/Api/DevicesController.cs
  git commit -m "feat: add GroupId/GroupName to DeviceResponse, annotate in GetAll"
  ```

---

## Task 7: Frontend types and API client

**Files:**
- Modify: `frontend/src/types/index.ts`
- Modify: `frontend/src/api/client.ts`

- [ ] **Step 1: Add DeviceGroup interface and extend Device**

  Replace `frontend/src/types/index.ts` with:
  ```typescript
  export interface Room {
    id: string;
    name: string;
  }
  
  export interface Device {
    id: string;
    name: string;
    roomId: string | null;
    communicationType: string;
    capabilities: string[];
    state: Record<string, unknown>;
    metadata: Record<string, string>;
    online?: boolean;
    settings?: Record<string, string>;
    groupId?: string;
    groupName?: string;
  }
  
  export interface DeviceGroup {
    id: string;
    name: string;
    roomId: string;
    roomName: string | null;
    deviceIds: string[];
    capabilities: string[];
    state: Record<string, unknown> | null;
    online: boolean | null;
  }
  
  export interface DiscoveredDevice {
    id: string;
    nativeId: string;
    communicationType: string;
    capabilities: string[];
    metadata: Record<string, string>;
  }
  
  export interface CommandPayload {
    capability: string;
    value: unknown;
  }
  
  export interface ConfigurePayload {
    name: string;
    roomId: string;
  }
  
  export type Capability =
    | 'Switch'
    | 'Dimmer'
    | 'Cover'
    | 'Temperature'
    | 'Motion'
    | 'Power'
    | 'Energy'
    | 'Humidity';
  ```

- [ ] **Step 2: Add group API functions to client.ts**

  Append to `frontend/src/api/client.ts` after `discoverShellyDevice`:
  ```typescript
  // --- Groups ---
  
  export function getGroups(): Promise<DeviceGroup[]> {
    return request('/groups');
  }
  
  export function getGroup(id: string): Promise<DeviceGroup> {
    return request(`/groups/${id}`);
  }
  
  export function getRoomGroups(roomId: string): Promise<DeviceGroup[]> {
    return request(`/rooms/${roomId}/groups`);
  }
  
  export function createGroup(data: { name: string; roomId: string; deviceIds: string[] }): Promise<DeviceGroup> {
    return request('/groups', { method: 'POST', body: JSON.stringify(data) });
  }
  
  export function updateGroup(id: string, data: { name: string; roomId: string; deviceIds: string[] }): Promise<void> {
    return request(`/groups/${id}`, { method: 'PUT', body: JSON.stringify(data) });
  }
  
  export function deleteGroup(id: string): Promise<void> {
    return request(`/groups/${id}`, { method: 'DELETE' });
  }
  
  export function sendGroupCommand(id: string, payload: CommandPayload): Promise<void> {
    return request(`/groups/${id}/command`, {
      method: 'POST',
      body: JSON.stringify(payload),
    });
  }
  ```

  Also update the import at line 1 of client.ts to include `DeviceGroup`:
  ```typescript
  import type { Room, Device, DeviceGroup, DiscoveredDevice, CommandPayload, ConfigurePayload } from '../types';
  ```

- [ ] **Step 3: Verify frontend build**

  Run: `cd C:\code\vidar\frontend && npm run build`
  Expected: Build completed successfully, no TypeScript errors.

- [ ] **Step 4: Commit**

  ```
  git add frontend/src/types/index.ts frontend/src/api/client.ts
  git commit -m "feat: add DeviceGroup type and group API client functions"
  ```

---

## Task 8: GroupRow component

**Files:**
- Create: `frontend/src/components/GroupRow.tsx`

- [ ] **Step 1: Create GroupRow**

  Create `frontend/src/components/GroupRow.tsx`:
  ```tsx
  import React from 'react';
  import { Link } from 'react-router-dom';
  import type { DeviceGroup, CommandPayload } from '../types';
  import { sendGroupCommand } from '../api/client';
  import { ToggleSwitch } from './ToggleSwitch';
  import { SliderControl } from './SliderControl';
  
  interface Props {
    group: DeviceGroup;
    onStateChange?: () => void;
  }
  
  function GroupIcon({ size = 20, color = 'var(--accent-primary)' }: { size?: number; color?: string }) {
    const s = { width: size, height: size, flexShrink: 0 } as const;
    return (
      <svg style={s} viewBox="0 0 24 24" fill="none" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <rect x="2" y="14" width="20" height="6" rx="2" />
        <rect x="4" y="8" width="16" height="6" rx="2" />
        <rect x="6" y="2" width="12" height="6" rx="2" />
      </svg>
    );
  }
  
  export function GroupRow({ group, onStateChange }: Props) {
    const state = group.state ?? {};
  
    async function handleCommand(payload: CommandPayload) {
      await sendGroupCommand(group.id, payload);
      onStateChange?.();
    }
  
    const controls: React.CSSProperties = {
      display: 'flex',
      alignItems: 'center',
      gap: 12,
      flexShrink: 0,
      flexWrap: 'wrap',
      justifyContent: 'flex-end',
    };
  
    function renderControls() {
      const items: React.ReactNode[] = [];
  
      if (group.capabilities.includes('Light')) {
        const lightState = state['Light'] as Record<string, unknown> | undefined;
        const isOn = lightState?.on === true;
        items.push(
          <ToggleSwitch key="light" checked={isOn} onChange={v => handleCommand({ capability: 'Light', value: v })} />
        );
      }
  
      if (group.capabilities.includes('Switch') && !group.capabilities.includes('Light')) {
        const isOn = Boolean(state['Switch']);
        items.push(
          <ToggleSwitch key="switch" checked={isOn} onChange={v => handleCommand({ capability: 'Switch', value: v })} />
        );
      }
  
      if (group.capabilities.includes('Dimmer') && !group.capabilities.includes('Light')) {
        const level = typeof state['Dimmer'] === 'number' ? (state['Dimmer'] as number) : 0;
        items.push(
          <div key="dimmer" style={{ width: 110 }}>
            <SliderControl value={level} className="slider-dimmer" accentColor="var(--accent-primary)" onCommit={v => handleCommand({ capability: 'Dimmer', value: v })} />
          </div>
        );
      }
  
      return items;
    }
  
    const isOffline = group.online === false;
  
    return (
      <div className="device-row">
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, opacity: isOffline ? 0.6 : 1 }}>
          <GroupIcon size={20} color={isOffline ? 'var(--text-muted)' : 'var(--accent-primary)'} />
        </div>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <Link to={`/groups/${group.id}`} className="device-name-link">
              {group.name}
            </Link>
            {isOffline && (
              <span style={{ fontSize: 11, fontWeight: 600, color: 'var(--accent-red)', letterSpacing: '0.04em' }}>
                Offline
              </span>
            )}
            <span style={{
              fontSize: 11, fontWeight: 600, color: 'var(--text-muted)',
              backgroundColor: 'var(--bg-hover)', border: '1px solid var(--border-subtle)',
              padding: '1px 7px', borderRadius: 20,
            }}>
              {group.deviceIds.length} device{group.deviceIds.length !== 1 ? 's' : ''}
            </span>
          </div>
          {group.capabilities.length > 0 && (
            <div className="device-caps">
              {group.capabilities.join(' · ')}
            </div>
          )}
        </div>
        <div style={{ ...controls, opacity: isOffline ? 0.5 : 1 }}>{renderControls()}</div>
      </div>
    );
  }
  ```

- [ ] **Step 2: Verify frontend build**

  Run: `cd C:\code\vidar\frontend && npm run build`
  Expected: Build completed successfully.

- [ ] **Step 3: Commit**

  ```
  git add frontend/src/components/GroupRow.tsx
  git commit -m "feat: add GroupRow component with stacked layers icon and inline controls"
  ```

---

## Task 9: RoomCard and RoomsPage — render groups, filter grouped devices, Create Group button

**Files:**
- Modify: `frontend/src/components/RoomCard.tsx`
- Modify: `frontend/src/pages/RoomsPage.tsx`
- Create: `frontend/src/components/CreateGroupModal.tsx`

- [ ] **Step 1: Update RoomCard to accept and render groups**

  Replace `frontend/src/components/RoomCard.tsx` with:
  ```tsx
  import React from 'react';
  import type { Room, Device, DeviceGroup } from '../types';
  import { DeviceRow } from './DeviceRow';
  import { GroupRow } from './GroupRow';
  
  interface Props {
    room: Room;
    devices: Device[];
    groups: DeviceGroup[];
    onDeviceStateChange: () => void;
  }
  
  export function RoomCard({ room, devices, groups, onDeviceStateChange }: Props) {
    // Filter out devices that belong to a group in this room
    const groupedDeviceIds = new Set(groups.flatMap(g => g.deviceIds));
    const ungroupedDevices = devices.filter(d => !groupedDeviceIds.has(d.id));
    const totalItems = groups.length + ungroupedDevices.length;
  
    const card: React.CSSProperties = {
      background: 'var(--bg-elevated)',
      border: '1px solid var(--border-subtle)',
      borderRadius: 'var(--radius-lg)',
      padding: '20px 22px',
      display: 'flex',
      flexDirection: 'column',
      boxShadow: 'var(--shadow-card)',
      transition: 'border-color 0.2s, box-shadow 0.2s',
      minWidth: 0,
    };
  
    const header: React.CSSProperties = {
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      paddingBottom: 14,
      marginBottom: 4,
      borderBottom: '1px solid var(--border-subtle)',
    };
  
    const title: React.CSSProperties = {
      fontFamily: 'var(--font-heading)',
      fontSize: 16,
      fontWeight: 600,
      color: 'var(--text-primary)',
      letterSpacing: '-0.01em',
    };
  
    const badge: React.CSSProperties = {
      fontSize: 11,
      fontWeight: 600,
      color: 'var(--text-muted)',
      backgroundColor: 'var(--bg-hover)',
      border: '1px solid var(--border-subtle)',
      padding: '2px 9px',
      borderRadius: 20,
    };
  
    const empty: React.CSSProperties = {
      color: 'var(--text-muted)',
      fontSize: 13,
      textAlign: 'center',
      padding: '16px 0',
    };
  
    return (
      <div
        style={card}
        onMouseEnter={(e) => {
          (e.currentTarget as HTMLDivElement).style.borderColor = 'var(--border-hover)';
          (e.currentTarget as HTMLDivElement).style.boxShadow = 'var(--shadow-elevated)';
        }}
        onMouseLeave={(e) => {
          (e.currentTarget as HTMLDivElement).style.borderColor = 'var(--border-subtle)';
          (e.currentTarget as HTMLDivElement).style.boxShadow = 'var(--shadow-card)';
        }}
      >
        <div style={header}>
          <span style={title}>{room.name}</span>
          <span style={badge}>{totalItems} item{totalItems !== 1 ? 's' : ''}</span>
        </div>
        <div>
          {totalItems === 0 ? (
            <div style={empty}>No devices</div>
          ) : (
            <>
              {groups.map((g) => (
                <GroupRow key={g.id} group={g} onStateChange={onDeviceStateChange} />
              ))}
              {ungroupedDevices.map((d) => (
                <DeviceRow key={d.id} device={d} onStateChange={onDeviceStateChange} />
              ))}
            </>
          )}
        </div>
      </div>
    );
  }
  ```

- [ ] **Step 2: Create CreateGroupModal**

  Create `frontend/src/components/CreateGroupModal.tsx`:
  ```tsx
  import React, { useEffect, useState } from 'react';
  import type { Room, Device } from '../types';
  import { getDevicesInRoom } from '../api/client';
  
  interface Props {
    rooms: Room[];
    onConfirm: (name: string, roomId: string, deviceIds: string[]) => Promise<void>;
    onCancel: () => void;
  }
  
  export function CreateGroupModal({ rooms, onConfirm, onCancel }: Props) {
    const [name, setName] = useState('');
    const [roomId, setRoomId] = useState(rooms[0]?.id ?? '');
    const [devices, setDevices] = useState<Device[]>([]);
    const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
    const [submitting, setSubmitting] = useState(false);
    const [error, setError] = useState<string | null>(null);
  
    useEffect(() => {
      if (!roomId) return;
      setSelectedIds(new Set());
      getDevicesInRoom(roomId).then(setDevices).catch(() => setDevices([]));
    }, [roomId]);
  
    const inputStyle: React.CSSProperties = {
      backgroundColor: 'var(--bg-hover)',
      border: '1px solid var(--border-default)',
      borderRadius: 'var(--radius-sm)',
      padding: '9px 13px',
      color: 'var(--text-primary)',
      outline: 'none',
      width: '100%',
      fontFamily: 'var(--font-body)',
      fontSize: 14,
      transition: 'border-color 0.2s, box-shadow 0.2s',
    };
  
    function handleFocus(e: React.FocusEvent<HTMLInputElement | HTMLSelectElement>) {
      e.target.style.borderColor = 'var(--accent-primary)';
      e.target.style.boxShadow = '0 0 0 3px var(--accent-primary-dim)';
    }
  
    function handleBlur(e: React.FocusEvent<HTMLInputElement | HTMLSelectElement>) {
      e.target.style.borderColor = 'var(--border-default)';
      e.target.style.boxShadow = 'none';
    }
  
    function toggleDevice(id: string) {
      setSelectedIds(prev => {
        const next = new Set(prev);
        if (next.has(id)) next.delete(id);
        else next.add(id);
        return next;
      });
    }
  
    async function handleSubmit(e: React.FormEvent) {
      e.preventDefault();
      if (!name.trim() || !roomId) return;
      setSubmitting(true);
      setError(null);
      try {
        await onConfirm(name.trim(), roomId, Array.from(selectedIds));
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to create group');
        setSubmitting(false);
      }
    }
  
    return (
      <div className="modal-overlay" onClick={(e) => e.target === e.currentTarget && onCancel()}>
        <div className="modal-dialog">
          <div className="modal-title">Create Group</div>
          <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              <label className="form-label">Group Name</label>
              <input
                style={inputStyle}
                type="text"
                placeholder="e.g. Living Room Lights"
                value={name}
                onChange={(e) => setName(e.target.value)}
                onFocus={handleFocus}
                onBlur={handleBlur}
                autoFocus
              />
            </div>
  
            <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              <label className="form-label">Room</label>
              <select
                style={{ ...inputStyle, appearance: 'none' as const }}
                value={roomId}
                onChange={(e) => setRoomId(e.target.value)}
                onFocus={handleFocus}
                onBlur={handleBlur}
              >
                {rooms.map((r) => (
                  <option key={r.id} value={r.id}>{r.name}</option>
                ))}
              </select>
            </div>
  
            <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              <label className="form-label">Devices</label>
              {devices.length === 0 ? (
                <div style={{ fontSize: 13, color: 'var(--text-muted)' }}>No devices in this room</div>
              ) : (
                <div style={{
                  display: 'flex', flexDirection: 'column', gap: 6,
                  maxHeight: 200, overflowY: 'auto', padding: '4px 0',
                }}>
                  {devices.map((d) => (
                    <label key={d.id} style={{
                      display: 'flex', alignItems: 'center', gap: 10,
                      cursor: 'pointer', fontSize: 14, color: 'var(--text-primary)',
                      padding: '6px 8px', borderRadius: 'var(--radius-sm)',
                      background: selectedIds.has(d.id) ? 'var(--bg-hover)' : 'transparent',
                      transition: 'background 0.15s',
                    }}>
                      <input
                        type="checkbox"
                        checked={selectedIds.has(d.id)}
                        onChange={() => toggleDevice(d.id)}
                        style={{ accentColor: 'var(--accent-primary)', width: 15, height: 15 }}
                      />
                      <span>{d.name}</span>
                      <span style={{ fontSize: 11, color: 'var(--text-muted)', marginLeft: 'auto' }}>
                        {d.capabilities.join(', ')}
                      </span>
                    </label>
                  ))}
                </div>
              )}
            </div>
  
            {error && (
              <div style={{ color: 'var(--accent-red)', fontSize: 13 }}>{error}</div>
            )}
  
            <div style={{ display: 'flex', gap: 10, justifyContent: 'flex-end', marginTop: 4 }}>
              <button type="button" className="btn-secondary" onClick={onCancel}>Cancel</button>
              <button
                type="submit"
                className="btn-primary"
                style={{ opacity: submitting || !name.trim() ? 0.5 : 1 }}
                disabled={submitting || !name.trim() || !roomId}
              >
                {submitting ? 'Creating…' : 'Create Group'}
              </button>
            </div>
          </form>
        </div>
      </div>
    );
  }
  ```

- [ ] **Step 3: Update RoomsPage to load groups, pass to RoomCard, and add Create Group button**

  Replace `frontend/src/pages/RoomsPage.tsx` with:
  ```tsx
  import React, { useCallback, useEffect, useRef, useState } from 'react';
  import type { Room, Device, DeviceGroup } from '../types';
  import { getRooms, createRoom, getDevicesInRoom, getRoomGroups, createGroup } from '../api/client';
  import { subscribeDeviceState } from '../api/sse';
  import { RoomCard } from '../components/RoomCard';
  import { CreateGroupModal } from '../components/CreateGroupModal';
  
  export function RoomsPage() {
    const [rooms, setRooms] = useState<Room[]>([]);
    const [devicesByRoom, setDevicesByRoom] = useState<Record<string, Device[]>>({});
    const [groupsByRoom, setGroupsByRoom] = useState<Record<string, DeviceGroup[]>>({});
    const [newRoomName, setNewRoomName] = useState('');
    const [loading, setLoading] = useState(true);
    const [addingRoom, setAddingRoom] = useState(false);
    const [showCreateGroup, setShowCreateGroup] = useState(false);
    const inputRef = useRef<HTMLInputElement>(null);
  
    const loadData = useCallback(async () => {
      const roomList = await getRooms();
      setRooms(roomList);
      const deviceEntries = await Promise.all(
        roomList.map(async (r) => {
          const devices = await getDevicesInRoom(r.id);
          return [r.id, devices] as [string, Device[]];
        })
      );
      const groupEntries = await Promise.all(
        roomList.map(async (r) => {
          const groups = await getRoomGroups(r.id);
          return [r.id, groups] as [string, DeviceGroup[]];
        })
      );
      setDevicesByRoom(Object.fromEntries(deviceEntries));
      setGroupsByRoom(Object.fromEntries(groupEntries));
      setLoading(false);
    }, []);
  
    useEffect(() => {
      loadData();
      const unsub = subscribeDeviceState(() => loadData());
      return unsub;
    }, [loadData]);
  
    async function handleAddRoom(e: React.FormEvent) {
      e.preventDefault();
      if (!newRoomName.trim()) return;
      setAddingRoom(true);
      try {
        await createRoom(newRoomName.trim());
        setNewRoomName('');
        await loadData();
      } finally {
        setAddingRoom(false);
      }
    }
  
    async function handleCreateGroup(name: string, roomId: string, deviceIds: string[]) {
      await createGroup({ name, roomId, deviceIds });
      setShowCreateGroup(false);
      await loadData();
    }
  
    if (loading) {
      return <div style={{ color: 'var(--text-muted)', padding: 24, fontFamily: 'var(--font-body)' }}>Loading rooms…</div>;
    }
  
    return (
      <div className="page-content">
        <div className="page-title">Rooms</div>
  
        <form
          onSubmit={handleAddRoom}
          style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 28, flexWrap: 'wrap' }}
        >
          <input
            ref={inputRef}
            type="text"
            placeholder="New room name…"
            value={newRoomName}
            onChange={(e) => setNewRoomName(e.target.value)}
            style={{ minWidth: 240, fontFamily: 'var(--font-body)', fontSize: 14 }}
          />
          <button
            type="submit"
            className="btn-primary"
            disabled={addingRoom || !newRoomName.trim()}
          >
            Add Room
          </button>
          <button
            type="button"
            className="btn-secondary"
            onClick={() => setShowCreateGroup(true)}
            disabled={rooms.length === 0}
          >
            Create Group
          </button>
        </form>
  
        {rooms.length === 0 ? (
          <div style={{ color: 'var(--text-muted)', fontSize: 14, marginTop: 8 }}>
            No rooms yet. Add one above.
          </div>
        ) : (
          <div
            style={{
              display: 'grid',
              gridTemplateColumns: 'repeat(auto-fill, minmax(360px, 1fr))',
              gap: 18,
            }}
          >
            {rooms.map((room) => (
              <RoomCard
                key={room.id}
                room={room}
                devices={devicesByRoom[room.id] ?? []}
                groups={groupsByRoom[room.id] ?? []}
                onDeviceStateChange={loadData}
              />
            ))}
          </div>
        )}
  
        {showCreateGroup && (
          <CreateGroupModal
            rooms={rooms}
            onConfirm={handleCreateGroup}
            onCancel={() => setShowCreateGroup(false)}
          />
        )}
      </div>
    );
  }
  ```

- [ ] **Step 4: Verify frontend build**

  Run: `cd C:\code\vidar\frontend && npm run build`
  Expected: Build completed successfully.

- [ ] **Step 5: Commit**

  ```
  git add frontend/src/components/RoomCard.tsx frontend/src/pages/RoomsPage.tsx frontend/src/components/CreateGroupModal.tsx
  git commit -m "feat: render groups in RoomCard, add Create Group modal to RoomsPage"
  ```

---

## Task 10: GroupDetailPage

**Files:**
- Create: `frontend/src/pages/GroupDetailPage.tsx`

- [ ] **Step 1: Create GroupDetailPage**

  Create `frontend/src/pages/GroupDetailPage.tsx`:
  ```tsx
  import React, { useCallback, useEffect, useState } from 'react';
  import { useParams, useNavigate } from 'react-router-dom';
  import type { DeviceGroup, Room, Device } from '../types';
  import { getGroup, getRooms, updateGroup, deleteGroup, sendGroupCommand, getDevices } from '../api/client';
  import { subscribeDeviceState } from '../api/sse';
  import { ToggleSwitch } from '../components/ToggleSwitch';
  import { SliderControl } from '../components/SliderControl';
  import { DeviceRow } from '../components/DeviceRow';
  
  export function GroupDetailPage() {
    const { id } = useParams<{ id: string }>();
    const navigate = useNavigate();
    const [group, setGroup] = useState<DeviceGroup | null>(null);
    const [memberDevices, setMemberDevices] = useState<Device[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
  
    // Edit mode
    const [editing, setEditing] = useState(false);
    const [editName, setEditName] = useState('');
    const [editRoomId, setEditRoomId] = useState('');
    const [editDeviceIds, setEditDeviceIds] = useState<Set<string>>(new Set());
    const [rooms, setRooms] = useState<Room[]>([]);
    const [allDevices, setAllDevices] = useState<Device[]>([]);
    const [saving, setSaving] = useState(false);
    const [saveError, setSaveError] = useState<string | null>(null);
    const [deleting, setDeleting] = useState(false);
  
    const loadGroup = useCallback(async () => {
      if (!id) return;
      try {
        const g = await getGroup(id);
        setGroup(g);
        // Load member device details
        const all = await getDevices();
        setMemberDevices(all.filter(d => g.deviceIds.includes(d.id)));
      } catch (e) {
        setError(e instanceof Error ? e.message : 'Failed to load group');
      } finally {
        setLoading(false);
      }
    }, [id]);
  
    useEffect(() => {
      loadGroup();
      const unsub = subscribeDeviceState(() => loadGroup());
      return unsub;
    }, [loadGroup]);
  
    async function enterEditMode() {
      if (!group) return;
      setEditName(group.name);
      setEditRoomId(group.roomId);
      setEditDeviceIds(new Set(group.deviceIds));
      setSaveError(null);
      try {
        const [roomList, deviceList] = await Promise.all([getRooms(), getDevices()]);
        setRooms(roomList);
        setAllDevices(deviceList);
      } catch { /* use empty lists */ }
      setEditing(true);
    }
  
    function cancelEdit() {
      setEditing(false);
      setSaveError(null);
    }
  
    async function saveEdit() {
      if (!id) return;
      setSaving(true);
      setSaveError(null);
      try {
        await updateGroup(id, {
          name: editName.trim(),
          roomId: editRoomId,
          deviceIds: Array.from(editDeviceIds),
        });
        setEditing(false);
        await loadGroup();
      } catch (e) {
        setSaveError(e instanceof Error ? e.message : 'Failed to save');
      } finally {
        setSaving(false);
      }
    }
  
    async function handleDelete() {
      if (!id || !confirm(`Delete group "${group?.name}"?`)) return;
      setDeleting(true);
      try {
        await deleteGroup(id);
        navigate(-1);
      } catch (e) {
        setDeleting(false);
        alert(e instanceof Error ? e.message : 'Failed to delete');
      }
    }
  
    async function cmd(capability: string, value: unknown) {
      if (!id) return;
      await sendGroupCommand(id, { capability, value });
      await loadGroup();
    }
  
    function toggleEditDevice(did: string) {
      setEditDeviceIds(prev => {
        const next = new Set(prev);
        if (next.has(did)) next.delete(did);
        else next.add(did);
        return next;
      });
    }
  
    if (loading) {
      return <div style={{ color: 'var(--text-muted)', padding: 24, fontFamily: 'var(--font-body)' }}>Loading group…</div>;
    }
  
    if (error || !group) {
      return (
        <div className="page-content">
          <button style={backBtnStyle} onClick={() => navigate(-1)}>← Back</button>
          <div style={{ color: 'var(--accent-red)' }}>{error ?? 'Group not found'}</div>
        </div>
      );
    }
  
    const state = group.state ?? {};
    const devicesInRoom = allDevices.filter(d => d.roomId === editRoomId);
  
    return (
      <div className="page-content">
        <button
          style={backBtnStyle}
          onMouseEnter={(e) => (e.currentTarget.style.color = 'var(--accent-primary)')}
          onMouseLeave={(e) => (e.currentTarget.style.color = 'var(--text-muted)')}
          onClick={() => navigate(-1)}
        >
          ← Rooms
        </button>
  
        {/* Header */}
        {!editing ? (
          <div style={{ marginBottom: 28 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 14 }}>
              <GroupIcon size={28} />
              <div style={{
                fontFamily: 'var(--font-heading)', fontSize: 26, fontWeight: 700,
                color: 'var(--text-primary)', letterSpacing: '-0.02em',
              }}>
                {group.name}
              </div>
              <button
                onClick={enterEditMode}
                style={editBtnStyle}
                onMouseEnter={e => { e.currentTarget.style.borderColor = 'var(--accent-primary)'; e.currentTarget.style.color = 'var(--accent-primary)'; }}
                onMouseLeave={e => { e.currentTarget.style.borderColor = 'var(--border-default)'; e.currentTarget.style.color = 'var(--text-secondary)'; }}
              >
                Edit
              </button>
              <button
                onClick={handleDelete}
                disabled={deleting}
                style={{ ...editBtnStyle, marginLeft: 4 }}
                onMouseEnter={e => { e.currentTarget.style.borderColor = 'var(--accent-red)'; e.currentTarget.style.color = 'var(--accent-red)'; }}
                onMouseLeave={e => { e.currentTarget.style.borderColor = 'var(--border-default)'; e.currentTarget.style.color = 'var(--text-secondary)'; }}
              >
                {deleting ? 'Deleting…' : 'Delete'}
              </button>
            </div>
            <div style={{ fontSize: 13, color: 'var(--text-muted)', marginTop: 6 }}>
              {group.roomName ?? 'No room'} · {group.deviceIds.length} device{group.deviceIds.length !== 1 ? 's' : ''}
              {group.capabilities.length > 0 && ' · ' + group.capabilities.join(' · ')}
            </div>
          </div>
        ) : (
          <div style={{
            background: 'var(--bg-elevated)', border: '1px solid var(--border-default)',
            borderRadius: 'var(--radius-md)', padding: '20px 22px', marginBottom: 28,
            boxShadow: 'var(--shadow-card)',
          }}>
            <div style={{
              fontSize: 10, fontWeight: 600, textTransform: 'uppercase' as const,
              letterSpacing: '0.08em', color: 'var(--accent-primary)', marginBottom: 18,
            }}>
              Edit Group
            </div>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 5 }}>
                <label style={fieldLabelStyle}>Name</label>
                <input
                  type="text"
                  value={editName}
                  onChange={e => setEditName(e.target.value)}
                  style={fieldInputStyle}
                  onFocus={handleFocus}
                  onBlur={handleBlur}
                  autoFocus
                />
              </div>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 5 }}>
                <label style={fieldLabelStyle}>Room</label>
                <select
                  value={editRoomId}
                  onChange={e => setEditRoomId(e.target.value)}
                  style={{ ...fieldInputStyle, appearance: 'none' as const }}
                  onFocus={handleFocus}
                  onBlur={handleBlur}
                >
                  {rooms.map(r => <option key={r.id} value={r.id}>{r.name}</option>)}
                </select>
              </div>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 5 }}>
                <label style={fieldLabelStyle}>Member Devices</label>
                {devicesInRoom.length === 0 ? (
                  <div style={{ fontSize: 13, color: 'var(--text-muted)' }}>No devices in selected room</div>
                ) : (
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 4, maxHeight: 180, overflowY: 'auto' }}>
                    {devicesInRoom.map(d => (
                      <label key={d.id} style={{
                        display: 'flex', alignItems: 'center', gap: 10,
                        cursor: 'pointer', fontSize: 14, color: 'var(--text-primary)',
                        padding: '5px 8px', borderRadius: 'var(--radius-sm)',
                        background: editDeviceIds.has(d.id) ? 'var(--bg-hover)' : 'transparent',
                        transition: 'background 0.15s',
                      }}>
                        <input
                          type="checkbox"
                          checked={editDeviceIds.has(d.id)}
                          onChange={() => toggleEditDevice(d.id)}
                          style={{ accentColor: 'var(--accent-primary)', width: 15, height: 15 }}
                        />
                        <span>{d.name}</span>
                      </label>
                    ))}
                  </div>
                )}
              </div>
              {saveError && <div style={{ fontSize: 13, color: 'var(--accent-red)' }}>{saveError}</div>}
              <div style={{ display: 'flex', gap: 10, marginTop: 4 }}>
                <button className="btn-primary" disabled={saving || !editName.trim()} style={{ opacity: saving || !editName.trim() ? 0.5 : 1 }} onClick={saveEdit}>
                  {saving ? 'Saving…' : 'Save'}
                </button>
                <button className="btn-secondary" onClick={cancelEdit}>Cancel</button>
              </div>
            </div>
          </div>
        )}
  
        {/* Capability cards */}
        {group.capabilities.length > 0 && (
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: 14, marginBottom: 28 }}>
            {group.capabilities.map(cap => renderCapabilityCard(cap, state, cmd))}
          </div>
        )}
  
        {/* Members */}
        <div style={{
          fontSize: 10, fontWeight: 600, textTransform: 'uppercase' as const,
          letterSpacing: '0.08em', color: 'var(--text-muted)', marginBottom: 12,
        }}>
          Members
        </div>
        {memberDevices.length === 0 ? (
          <div style={{ color: 'var(--text-muted)', fontSize: 13 }}>No member devices</div>
        ) : (
          <div style={{
            background: 'var(--bg-elevated)', border: '1px solid var(--border-subtle)',
            borderRadius: 'var(--radius-lg)', padding: '4px 20px', boxShadow: 'var(--shadow-card)',
          }}>
            {memberDevices.map(d => (
              <DeviceRow key={d.id} device={d} onStateChange={loadGroup} />
            ))}
          </div>
        )}
      </div>
    );
  }
  
  function GroupIcon({ size = 20, color = 'var(--accent-primary)' }: { size?: number; color?: string }) {
    const s = { width: size, height: size, flexShrink: 0 } as const;
    return (
      <svg style={s} viewBox="0 0 24 24" fill="none" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <rect x="2" y="14" width="20" height="6" rx="2" />
        <rect x="4" y="8" width="16" height="6" rx="2" />
        <rect x="6" y="2" width="12" height="6" rx="2" />
      </svg>
    );
  }
  
  // --- Shared styles ---
  
  const backBtnStyle: React.CSSProperties = {
    display: 'inline-flex', alignItems: 'center', gap: 6,
    color: 'var(--text-muted)', fontSize: 13, cursor: 'pointer',
    marginBottom: 22, fontFamily: 'var(--font-body)', transition: 'color 0.15s',
    background: 'none', border: 'none', padding: 0,
  };
  
  const editBtnStyle: React.CSSProperties = {
    padding: '5px 14px', fontSize: 12, fontWeight: 600, fontFamily: 'var(--font-body)',
    background: 'var(--bg-hover)', border: '1px solid var(--border-default)',
    borderRadius: 'var(--radius-sm)', color: 'var(--text-secondary)',
    cursor: 'pointer', transition: 'all 0.15s', letterSpacing: '0.02em',
  };
  
  const fieldLabelStyle: React.CSSProperties = {
    fontSize: 11, fontWeight: 600, color: 'var(--text-muted)',
    textTransform: 'uppercase', letterSpacing: '0.06em',
  };
  
  const fieldInputStyle: React.CSSProperties = {
    background: 'var(--bg-surface)', border: '1px solid var(--border-default)',
    borderRadius: 'var(--radius-sm)', padding: '9px 13px',
    color: 'var(--text-primary)', fontFamily: 'var(--font-body)',
    fontSize: 14, outline: 'none', transition: 'border-color 0.15s, box-shadow 0.15s',
    maxWidth: 400,
  };
  
  function handleFocus(e: React.FocusEvent<HTMLInputElement | HTMLSelectElement>) {
    e.currentTarget.style.borderColor = 'var(--accent-primary)';
    e.currentTarget.style.boxShadow = '0 0 0 3px color-mix(in srgb, var(--accent-primary) 20%, transparent)';
  }
  
  function handleBlur(e: React.FocusEvent<HTMLInputElement | HTMLSelectElement>) {
    e.currentTarget.style.borderColor = 'var(--border-default)';
    e.currentTarget.style.boxShadow = 'none';
  }
  
  // --- Capability card renderer (mirrors DeviceDetailPage) ---
  
  const capCardStyle: React.CSSProperties = {
    background: 'var(--bg-elevated)', border: '1px solid var(--border-subtle)',
    borderRadius: 'var(--radius-md)', padding: '16px 18px',
    position: 'relative', overflow: 'hidden',
    boxShadow: 'var(--shadow-card)', transition: 'border-color 0.2s, box-shadow 0.2s',
  };
  
  const capLabelStyle: React.CSSProperties = {
    fontSize: 10, fontWeight: 600, textTransform: 'uppercase',
    letterSpacing: '0.08em', color: 'var(--text-muted)', marginBottom: 10,
    display: 'flex', alignItems: 'center', gap: 6,
  };
  
  function Indicator({ color }: { color: string }) {
    return <div style={{ position: 'absolute', top: 0, left: 0, width: '100%', height: 2, background: color, opacity: 0.7 }} />;
  }
  
  function renderCapabilityCard(
    cap: string,
    state: Record<string, unknown>,
    cmd: (capability: string, value: unknown) => void,
  ) {
    switch (cap) {
      case 'Switch': {
        const isOn = Boolean(state['Switch']);
        return (
          <div key={cap} style={capCardStyle}>
            <Indicator color="var(--accent-primary)" />
            <div style={capLabelStyle}>Switch</div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginTop: 6 }}>
              <ToggleSwitch checked={isOn} onChange={v => cmd('Switch', v)} />
              <span style={{ fontSize: 14, fontWeight: 500, color: isOn ? 'var(--accent-primary)' : 'var(--text-muted)' }}>
                {isOn ? 'On' : 'Off'}
              </span>
            </div>
          </div>
        );
      }
      case 'Light': {
        const lightState = state['Light'] as Record<string, unknown> | undefined;
        const isOn = lightState?.on === true;
        const brightness = typeof lightState?.brightness === 'number' ? (lightState.brightness as number) : 0;
        const hasBrightness = lightState?.brightness !== undefined;
        return (
          <div key={cap} style={capCardStyle}>
            <Indicator color="var(--accent-primary)" />
            <div style={capLabelStyle}>Light</div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginTop: 6 }}>
              <ToggleSwitch checked={isOn} onChange={v => cmd('Light', v)} />
              <span style={{ fontSize: 14, fontWeight: 500, color: isOn ? 'var(--accent-primary)' : 'var(--text-muted)' }}>
                {isOn ? (hasBrightness ? `${Math.round(brightness)}%` : 'On') : 'Off'}
              </span>
            </div>
            {hasBrightness && (
              <div style={{ marginTop: 14 }}>
                <SliderControl value={brightness} className="slider-dimmer" accentColor="var(--accent-primary)" onCommit={v => cmd('Light', v)} />
              </div>
            )}
          </div>
        );
      }
      case 'Dimmer': {
        const level = typeof state['Dimmer'] === 'number' ? (state['Dimmer'] as number) : 0;
        return (
          <div key={cap} style={capCardStyle}>
            <Indicator color="var(--accent-primary)" />
            <div style={capLabelStyle}>Dimmer</div>
            <div style={{ fontFamily: 'var(--font-heading)', fontSize: 22, fontWeight: 600, color: 'var(--accent-primary)', marginBottom: 2 }}>{Math.round(level)}%</div>
            <div style={{ marginTop: 12 }}>
              <SliderControl value={level} className="slider-dimmer" accentColor="var(--accent-primary)" onCommit={v => cmd('Dimmer', v)} />
            </div>
          </div>
        );
      }
      default: {
        const val = state[cap];
        return (
          <div key={cap} style={capCardStyle}>
            <Indicator color="var(--text-muted)" />
            <div style={capLabelStyle}>{cap}</div>
            <div style={{ fontFamily: 'var(--font-heading)', fontSize: 22, fontWeight: 600, color: 'var(--text-primary)', marginBottom: 2 }}>
              {val != null ? String(val) : '—'}
            </div>
          </div>
        );
      }
    }
  }
  ```

- [ ] **Step 2: Verify frontend build**

  Run: `cd C:\code\vidar\frontend && npm run build`
  Expected: Build completed successfully.

- [ ] **Step 3: Commit**

  ```
  git add frontend/src/pages/GroupDetailPage.tsx
  git commit -m "feat: add GroupDetailPage with edit mode, capability cards, and member list"
  ```

---

## Task 11: App routing + DevicesPage group label

**Files:**
- Modify: `frontend/src/App.tsx`
- Modify: `frontend/src/pages/DevicesPage.tsx`

- [ ] **Step 1: Add /groups/:id route to App.tsx**

  Replace `frontend/src/App.tsx` with:
  ```tsx
  import { BrowserRouter, Routes, Route } from 'react-router-dom';
  import { Layout } from './components/Layout';
  import { RoomsPage } from './pages/RoomsPage';
  import { DevicesPage } from './pages/DevicesPage';
  import { DiscoveredPage } from './pages/DiscoveredPage';
  import { DeviceDetailPage } from './pages/DeviceDetailPage';
  import { GroupDetailPage } from './pages/GroupDetailPage';
  
  export default function App() {
    return (
      <BrowserRouter>
        <Routes>
          <Route path="/" element={<Layout />}>
            <Route index element={<RoomsPage />} />
            <Route path="devices" element={<DevicesPage />} />
            <Route path="devices/:id" element={<DeviceDetailPage />} />
            <Route path="groups/:id" element={<GroupDetailPage />} />
            <Route path="discovered" element={<DiscoveredPage />} />
          </Route>
        </Routes>
      </BrowserRouter>
    );
  }
  ```

- [ ] **Step 2: Update DevicesPage to show group label**

  In `frontend/src/pages/DevicesPage.tsx`, the DeviceRow component already receives `device` which now has `groupId` and `groupName` from the API. We surface this in the DeviceRow's `roomName` area by modifying DevicesPage to pass a custom prop.

  The simplest approach: the DeviceRow already shows `roomName` when `showRoom` is true. We can extend DeviceRow's subtitle logic. But to avoid changing DeviceRow signature drastically, add the group label in DevicesPage by passing it as an overridden rooms list that appends the group label.

  Actually the cleanest approach: use the existing `showRoom` + `rooms` system; no changes needed to DeviceRow. The group label can be added to DevicesPage's template directly as a wrapper, OR we can do a minimal DeviceRow change.

  Best approach with minimal change: in DevicesPage, after the DeviceRow, overlay a small group label. But DeviceRow doesn't expose that hook.

  Instead, add a simple `groupLabel?: string` prop to DeviceRow that renders below the room line. Full DeviceRow replacement is not needed — just add the prop in the props interface and render it in the subtitle area.

  Replace `frontend/src/components/DeviceRow.tsx` with the same file content as before but extend the `Props` interface and render a group label:

  At Props interface, add: `groupLabel?: string;`

  In the function signature: `export function DeviceRow({ device, showRoom = false, rooms, groupLabel, onStateChange }: Props)`

  After the `{roomName && ...}` block, add:
  ```tsx
  {groupLabel && (
    <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 2 }}>
      Group: <span style={{ color: 'var(--accent-primary)' }}>{groupLabel}</span>
    </div>
  )}
  ```

  In `DevicesPage.tsx`, compute the group label from `device.groupName` and pass it:
  ```tsx
  <DeviceRow
    key={d.id}
    device={d}
    showRoom
    rooms={rooms}
    groupLabel={d.groupName ?? undefined}
    onStateChange={loadData}
  />
  ```

  Full updated `frontend/src/components/DeviceRow.tsx`:
  ```tsx
  import React from 'react';
  import { Link } from 'react-router-dom';
  import type { Device } from '../types';
  import { sendCommand } from '../api/client';
  import { ToggleSwitch } from './ToggleSwitch';
  import { ProgressBar } from './ProgressBar';
  import { StatusDot } from './StatusDot';
  import { SliderControl } from './SliderControl';
  import { CapabilityIcon, primaryCapabilityIcon } from './CapabilityIcon';
  
  interface Props {
    device: Device;
    showRoom?: boolean;
    rooms?: { id: string; name: string }[];
    groupLabel?: string;
    onStateChange?: () => void;
  }
  
  export function DeviceRow({ device, showRoom = false, rooms, groupLabel, onStateChange }: Props) {
    const state = device.state ?? {};
  
    async function handleSwitch(value: boolean) {
      await sendCommand(device.id, { capability: 'Switch', value });
      onStateChange?.();
    }
  
    async function handleDimmer(value: number) {
      await sendCommand(device.id, { capability: 'Dimmer', value });
      onStateChange?.();
    }
  
    async function handleCover(value: number) {
      await sendCommand(device.id, { capability: 'Cover', value });
      onStateChange?.();
    }
  
    const roomName = showRoom && rooms
      ? (rooms.find((r) => r.id === device.roomId)?.name ?? 'Unassigned')
      : null;
  
    const controls: React.CSSProperties = {
      display: 'flex',
      alignItems: 'center',
      gap: 12,
      flexShrink: 0,
      flexWrap: 'wrap',
      justifyContent: 'flex-end',
    };
  
    function renderControls() {
      const items: React.ReactNode[] = [];
  
      if (device.capabilities.includes('Light')) {
        const lightState = state['Light'] as Record<string, unknown> | undefined;
        const isOn = lightState?.on === true;
        items.push(
          <ToggleSwitch key="light" checked={isOn} onChange={v => sendCommand(device.id, { capability: 'Light', value: v }).then(() => onStateChange?.())} />
        );
      }
  
      if (device.capabilities.includes('Switch') && !device.capabilities.includes('Light')) {
        const isOn = Boolean(state['Switch']);
        items.push(
          <ToggleSwitch key="switch" checked={isOn} onChange={handleSwitch} />
        );
      }
  
      if (device.capabilities.includes('Dimmer') && !device.capabilities.includes('Light')) {
        const level = typeof state['Dimmer'] === 'number' ? (state['Dimmer'] as number) : 0;
        items.push(
          <div key="dimmer" style={{ width: 110 }}>
            <SliderControl value={level} className="slider-dimmer" accentColor="var(--accent-primary)" onCommit={handleDimmer} />
          </div>
        );
      }
  
      if (device.capabilities.includes('Cover')) {
        const pos = typeof state['Cover'] === 'number' ? (state['Cover'] as number) : 0;
        items.push(
          <div key="cover" style={{ width: 110 }}>
            <SliderControl value={pos} className="slider-cover" accentColor="var(--accent-teal)" onCommit={handleCover} />
          </div>
        );
      }
  
      if (device.capabilities.includes('Motion')) {
        const detected = Boolean(state['Motion']);
        items.push(
          <StatusDot key="motion" active={detected} label={detected ? 'Detected' : 'Clear'} />
        );
      }
  
      if (device.capabilities.includes('Temperature')) {
        const temp = state['Temperature'];
        items.push(
          <span key="temp" style={{ fontSize: 13, color: temp != null ? 'var(--accent-red)' : 'var(--text-muted)' }}>
            {temp != null ? `${Number(temp).toFixed(1)} °C` : '— °C'}
          </span>
        );
      }
  
      if (device.capabilities.includes('Power')) {
        const power = state['Power'];
        items.push(
          <span key="power" style={{ fontSize: 13, color: power != null ? 'var(--accent-blue)' : 'var(--text-muted)' }}>
            {power != null ? `${Number(power).toFixed(1)} W` : '— W'}
          </span>
        );
      }
  
      if (device.capabilities.includes('Energy')) {
        const energy = state['Energy'];
        items.push(
          <span key="energy" style={{ fontSize: 13, color: energy != null ? 'var(--accent-green)' : 'var(--text-muted)' }}>
            {energy != null ? `${Number(energy).toFixed(2)} kWh` : '— kWh'}
          </span>
        );
      }
  
      if (device.capabilities.includes('Humidity')) {
        const hum = typeof state['Humidity'] === 'number' ? (state['Humidity'] as number) : 0;
        items.push(
          <div key="hum" style={{ width: 80 }}>
            <ProgressBar value={hum} color="var(--accent-blue)" label={`${Math.round(hum)}%`} />
          </div>
        );
      }
  
      return items;
    }
  
    const isOffline = device.online === false;
    const primaryCap = primaryCapabilityIcon(device.capabilities);
  
    return (
      <div className="device-row">
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, opacity: isOffline ? 0.6 : 1 }}>
          <CapabilityIcon capability={primaryCap} size={20} color={isOffline ? 'var(--text-muted)' : undefined} />
        </div>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <Link to={`/devices/${device.id}`} className="device-name-link">
              {device.name}
            </Link>
            {isOffline && (
              <span style={{ fontSize: 11, fontWeight: 600, color: 'var(--accent-red)', letterSpacing: '0.04em' }}>
                Offline
              </span>
            )}
          </div>
          {roomName && (
            <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 2 }}>{roomName}</div>
          )}
          {groupLabel && (
            <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 2 }}>
              Group: <span style={{ color: 'var(--accent-primary)' }}>{groupLabel}</span>
            </div>
          )}
          {device.capabilities.length > 0 && (
            <div className="device-caps">
              {device.capabilities.join(' · ')}
            </div>
          )}
        </div>
        <div style={{ ...controls, opacity: isOffline ? 0.5 : 1 }}>{renderControls()}</div>
      </div>
    );
  }
  ```

  Updated `frontend/src/pages/DevicesPage.tsx` (only change is the added `groupLabel` prop):
  ```tsx
  import { useCallback, useEffect, useState } from 'react';
  import type { Device, Room } from '../types';
  import { getDevices, getRooms } from '../api/client';
  import { subscribeDeviceState } from '../api/sse';
  import { DeviceRow } from '../components/DeviceRow';
  
  type Filter = 'All' | string;
  
  export function DevicesPage() {
    const [devices, setDevices] = useState<Device[]>([]);
    const [rooms, setRooms] = useState<Room[]>([]);
    const [filter, setFilter] = useState<Filter>('All');
    const [loading, setLoading] = useState(true);
  
    const loadData = useCallback(async () => {
      const [deviceList, roomList] = await Promise.all([getDevices(), getRooms()]);
      setDevices(deviceList);
      setRooms(roomList);
      setLoading(false);
    }, []);
  
    useEffect(() => {
      loadData();
      const unsub = subscribeDeviceState(() => loadData());
      return unsub;
    }, [loadData]);
  
    const allCapabilities = Array.from(
      new Set(devices.flatMap((d) => d.capabilities))
    ).sort();
  
    const filters: Filter[] = ['All', ...allCapabilities];
  
    const filtered =
      filter === 'All'
        ? devices
        : devices.filter((d) => d.capabilities.includes(filter));
  
    if (loading) {
      return <div style={{ color: 'var(--text-muted)', padding: 24, fontFamily: 'var(--font-body)' }}>Loading devices…</div>;
    }
  
    return (
      <div className="page-content">
        <div className="page-title">All Devices</div>
  
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, marginBottom: 24 }}>
          {filters.map((f) => (
            <button
              key={f}
              className={`filter-pill${filter === f ? ' active' : ''}`}
              onClick={() => setFilter(f)}
            >
              {f}
            </button>
          ))}
        </div>
  
        {filtered.length === 0 ? (
          <div style={{ color: 'var(--text-muted)', fontSize: 14 }}>
            No devices{filter !== 'All' ? ` with capability "${filter}"` : ''}.
          </div>
        ) : (
          <div
            style={{
              background: 'var(--bg-elevated)',
              border: '1px solid var(--border-subtle)',
              borderRadius: 'var(--radius-lg)',
              padding: '4px 20px',
              boxShadow: 'var(--shadow-card)',
            }}
          >
            {filtered.map((d) => (
              <DeviceRow
                key={d.id}
                device={d}
                showRoom
                rooms={rooms}
                groupLabel={d.groupName ?? undefined}
                onStateChange={loadData}
              />
            ))}
          </div>
        )}
      </div>
    );
  }
  ```

- [ ] **Step 3: Verify frontend build**

  Run: `cd C:\code\vidar\frontend && npm run build`
  Expected: Build completed successfully, no errors.

- [ ] **Step 4: Full solution build**

  Run: `dotnet build C:\code\vidar\Vidar.slnx`
  Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

  ```
  git add frontend/src/App.tsx frontend/src/pages/DevicesPage.tsx frontend/src/components/DeviceRow.tsx
  git commit -m "feat: add /groups/:id route, show group label in DevicesPage"
  ```

---

## Self-Review

### Spec Coverage Check

| Spec Requirement | Task |
|---|---|
| GroupConfiguration model | Task 1 |
| IGroupRepository interface | Task 1 |
| MongoGroupRepository | Task 2 |
| BSON class map registration | Task 2 |
| DI registration | Task 2 |
| CreateGroupRequest DTO | Task 3 |
| GroupResponse DTO | Task 3 |
| UpdateGroupRequest DTO | Task 3 |
| GET /api/groups | Task 4 |
| POST /api/groups | Task 4 |
| GET /api/groups/{id} | Task 4 |
| PUT /api/groups/{id} | Task 4 |
| DELETE /api/groups/{id} | Task 4 |
| POST /api/groups/{id}/command fan-out | Task 4 |
| Capabilities = intersection | Task 4 |
| Leader state/online | Task 4 |
| Resolve room name | Task 4 |
| GET /api/rooms/{id}/groups | Task 5 |
| Exclude grouped devices from /rooms/{id}/devices | Task 5 |
| GroupId/GroupName on DeviceResponse | Task 6 |
| Annotate in GetAll | Task 6 |
| DeviceGroup TypeScript type | Task 7 |
| groupId/groupName on Device interface | Task 7 |
| Group API client functions | Task 7 |
| GroupRow component | Task 8 |
| Group icon (stacked layers SVG) | Task 8 |
| Member count badge | Task 8 |
| Inline controls in GroupRow | Task 8 |
| RoomCard accepts groups prop | Task 9 |
| GroupRows rendered above DeviceRows | Task 9 |
| Grouped devices filtered from RoomCard | Task 9 |
| RoomsPage loads groups per room | Task 9 |
| Create Group button on RoomsPage | Task 9 |
| CreateGroupModal | Task 9 |
| GroupDetailPage | Task 10 |
| Back button | Task 10 |
| Edit mode (name, room, member devices) | Task 10 |
| Capability cards | Task 10 |
| Members section | Task 10 |
| /groups/:id route in App.tsx | Task 11 |
| Group label on DevicesPage | Task 11 |

All spec requirements are covered.

### Placeholder Scan

No placeholders found. All code blocks are complete.

### Type Consistency Check

- `GroupResponse` record: `(Guid Id, string Name, Guid RoomId, string? RoomName, List<Guid> DeviceIds, List<string> Capabilities, Dictionary<string, object>? State, bool? Online)` — used consistently in `GroupsController.BuildGroupResponse` and `RoomsController.BuildGroupResponse`.
- `DeviceResponse` record extended with `Guid? GroupId, string? GroupName` — callers in `DevicesController` and `RoomsController` both pass `null, null` for the new params.
- `DeviceGroup` TypeScript interface fields match the C# `GroupResponse` JSON serialization (camelCase).
- `GroupRow` calls `sendGroupCommand` from client.ts — both defined consistently.
- `CreateGroupModal` calls `getDevicesInRoom` (already in client.ts) — correct.
- `GroupDetailPage` calls `getGroup`, `getRooms`, `updateGroup`, `deleteGroup`, `sendGroupCommand`, `getDevices` — all defined in Task 7.
- `DeviceRow` new `groupLabel?: string` prop — used in `DevicesPage` as `groupLabel={d.groupName ?? undefined}` — consistent.
