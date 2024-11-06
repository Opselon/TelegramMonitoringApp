using CustomerMonitoringApp.Domain.Entities;
using CustomerMonitoringApp.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using CustomerMonitoringApp.Infrastructure.Data;

namespace CustomerMonitoringApp.Infrastructure.Repositories
{
    public class CallHistoryRepository : ICallHistoryRepository
    {
        private readonly AppDbContext _context;

        public CallHistoryRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task AddCallHistoryAsync(List<CallHistory> records)
        {
            await _context.CallHistories.AddRangeAsync(records);
            await _context.SaveChangesAsync();
        }
    }
}