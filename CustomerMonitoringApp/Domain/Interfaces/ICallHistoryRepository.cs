using CustomerMonitoringApp.Domain.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CustomerMonitoringApp.Domain.Interfaces
{
    public interface ICallHistoryRepository
    {
        Task AddCallHistoryAsync(List<CallHistory> records);
    }
}