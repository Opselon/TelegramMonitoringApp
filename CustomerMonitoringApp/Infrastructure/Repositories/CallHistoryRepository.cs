using CustomerMonitoringApp.Domain.Entities;
using CustomerMonitoringApp.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CustomerMonitoringApp.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace CustomerMonitoringApp.Infrastructure.Repositories
{
    public class CallHistoryRepository : ICallHistoryRepository
    {
        private readonly AppDbContext _context;
        private readonly ILogger<CallHistoryRepository> _logger;

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

        public async Task<List<CallHistory>> GetRecentCallsByPhoneNumberAsync(string phoneNumber, string startDate, string endDateTime)
        {
            try
            {
                // Parse Persian dates to Gregorian DateTime
                DateTime parsedStartDate = ParsePersianDate(startDate);
                DateTime parsedEndDateTime = ParsePersianDate(endDateTime);

                var callHistories = await _context.CallHistories
                    .Where(ch => ch.SourcePhoneNumber == phoneNumber || ch.DestinationPhoneNumber == phoneNumber)
                    .ToListAsync();

                // فیلتر کردن تاریخ‌ها در کد
                var filteredCallHistories = callHistories
                    .Where(ch => DateTime.Parse(ch.CallDateTime) >= parsedStartDate && DateTime.Parse(ch.CallDateTime) <= parsedEndDateTime)
                    .ToList();

                return filteredCallHistories;
            }
            catch (FormatException ex)
            {
                // Log or handle the exception if date parsing fails
                throw new InvalidOperationException("Invalid date format.", ex);
            }
        }

        // Helper method to parse Persian date strings to DateTime
        private DateTime ParsePersianDate(string persianDate)
        {
            var persianCalendar = new PersianCalendar();
            var dateParts = persianDate.Split('/');
            int year = int.Parse(dateParts[0]);
            int month = int.Parse(dateParts[1]);
            int day = int.Parse(dateParts[2]);
            return persianCalendar.ToDateTime(year, month, day, 0, 0, 0, 0);
        }

        // Helper method to convert DateTime to Persian date string
        private string ConvertToPersianDate(DateTime date)
        {
            var persianCalendar = new PersianCalendar();
            int year = persianCalendar.GetYear(date);
            int month = persianCalendar.GetMonth(date);
            int day = persianCalendar.GetDayOfMonth(date);
            return $"{year}/{month:D2}/{day:D2}";
        }

        /// <summary>
        /// Retrieves selected call history fields for a specific phone number, excluding sensitive data by returning a DTO.
        /// </summary>
        public async Task<List<CallHistory>> GetCallsByPhoneNumberAsync(string phoneNumber)
        {
            var result = new List<CallHistory>();

            try
            {
                result = await _context.CallHistories
                    .Where(ch => ch.SourcePhoneNumber == phoneNumber || ch.DestinationPhoneNumber == phoneNumber)
                    .Select(ch => new CallHistory // Map only specific properties
                    {
                        SourcePhoneNumber = ch.SourcePhoneNumber,
                        DestinationPhoneNumber = ch.DestinationPhoneNumber,
                        CallDateTime = ch.CallDateTime,
                        Duration = ch.Duration,
                        CallType = ch.CallType
                    })
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while fetching call history for phone number: {PhoneNumber}", phoneNumber);
            }

            return result;
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
                .Select(ch => new
                {
                    CallDate = DateTime.Parse(ch.CallDateTime), // Convert string to DateTime
                    ch
                })
                .GroupBy(ch => ch.CallDate.Date) // Now you can use .Date on DateTime
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
                .AnyAsync(ch => ch.SourcePhoneNumber == phoneNumber);
        }

        #endregion

        #endregion
    }
}
