using Vidar.Core.Model;

namespace Vidar.Host.Persistence;

public interface IHistoryRepository
{
    Task AddStateEntryAsync(StateHistoryEntry entry);
    Task AddCommandEntryAsync(CommandHistoryEntry entry);
    Task<List<StateHistoryEntry>> GetStateHistoryAsync(Guid deviceId, int skip = 0, int limit = 20, DateTime? from = null, DateTime? to = null);
    Task<List<CommandHistoryEntry>> GetCommandHistoryAsync(Guid deviceId, int skip = 0, int limit = 20, DateTime? from = null, DateTime? to = null);
}
