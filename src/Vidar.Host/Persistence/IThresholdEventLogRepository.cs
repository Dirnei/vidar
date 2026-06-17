using Vidar.Core.Model;

namespace Vidar.Host.Persistence;

public interface IThresholdEventLogRepository
{
    Task InsertAsync(ThresholdEventLog entry);
    Task<List<ThresholdEventLog>> GetRecentAsync(int skip = 0, int limit = 50);
    Task<long> CountAsync();
}
