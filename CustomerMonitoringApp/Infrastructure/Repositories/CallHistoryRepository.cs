using CustomerMonitoringApp.Domain.Entities;
using CustomerMonitoringApp.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
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

        #region Methods for Adding and Retrieving Call History

        /// <summary>
        /// Adds a list of call history records to the database.
        /// </summary>
        public async Task AddCallHistoryAsync(List<CallHistory> records)
        {
            await _context.CallHistories.AddRangeAsync(records);
            await _context.SaveChangesAsync();
        }

        #region New Methods for Searching by Phone Number

        /// <summary>
        /// Retrieves all call histories for a specific phone number.
        /// </summary>
        public async Task<List<CallHistory>> GetCallsByPhoneNumberAsync(string phoneNumber)
        {
            return await _context.CallHistories
                .Where(ch => ch.SourcePhoneNumber == phoneNumber || ch.DestinationPhoneNumber == phoneNumber)
                .ToListAsync();
        }

        /// <summary>
        /// Retrieves recent call histories for a specific phone number based on a start date.
        /// </summary>
        public async Task<List<CallHistory>> GetRecentCallsByPhoneNumberAsync(string phoneNumber, DateTime startDate)
        {
            return await _context.CallHistories
                .Where(ch => ch.SourcePhoneNumber == phoneNumber && ch.CallDateTime >= startDate)
                .ToListAsync();
        }

        /// <summary>
        /// Retrieves long calls for a specific phone number that exceed a specified duration.
        /// </summary>
        public async Task<List<CallHistory>> GetLongCallsByPhoneNumberAsync(string phoneNumber, int minimumDurationInSeconds)
        {
            return await _context.CallHistories
                .Where(ch => ch.SourcePhoneNumber == phoneNumber && ch.Duration > minimumDurationInSeconds)
                .ToListAsync();
        }

        public Task<List<CallHistory>> GetAfterHoursCallsByPhoneNumberAsync(string phoneNumber, TimeSpan startBusinessTime, TimeSpan endBusinessTime)
        {
            throw new NotImplementedException();
        }


        /// <summary>
        /// Retrieves frequent call dates and times for a specific phone number.
        /// </summary>
        public async Task<Dictionary<DateTime, int>> GetFrequentCallDatesByPhoneNumberAsync(string phoneNumber)
        {
            return await _context.CallHistories
                .Where(ch => ch.SourcePhoneNumber == phoneNumber)
                .GroupBy(ch => ch.CallDateTime.Date)
                .ToDictionaryAsync(g => g.Key, g => g.Count());
        }

        /// <summary>
        /// Retrieves the top N most recent calls for a phone number.
        /// </summary>
        public async Task<List<CallHistory>> GetTopNRecentCallsAsync(string phoneNumber, int numberOfCalls)
        {
            return await _context.CallHistories
                .Where(ch => ch.SourcePhoneNumber == phoneNumber)
                .OrderByDescending(ch => ch.CallDateTime)
                .Take(numberOfCalls)
                .ToListAsync();
        }

        /// <summary>
        /// Checks if a phone number has been contacted within a specified time window.
        /// </summary>
        public async Task<bool> HasRecentCallWithinTimeSpanAsync(string phoneNumber, TimeSpan timeSpan)
        {
            var recentCallThreshold = DateTime.Now - timeSpan;
            return await _context.CallHistories
                .AnyAsync(ch => ch.SourcePhoneNumber == phoneNumber && ch.CallDateTime >= recentCallThreshold);
        }

        #endregion

        #endregion
    }
}
