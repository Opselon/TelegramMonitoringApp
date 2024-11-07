using CustomerMonitoringApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CustomerMonitoringApp.Domain.Interfaces
{
    public interface ICallHistoryRepository
    {
        Task AddCallHistoryAsync(List<CallHistory> records);

        // Retrieves all call histories for a specific phone number
        Task<List<CallHistory>> GetCallsByPhoneNumberAsync(string phoneNumber);

        // Retrieves recent call histories for a specific phone number based on a start date
        Task<List<CallHistory>> GetRecentCallsByPhoneNumberAsync(string phoneNumber, DateTime startDate);

        // Retrieves long calls for a specific phone number that exceed a specified duration
        Task<List<CallHistory>> GetLongCallsByPhoneNumberAsync(string phoneNumber, int minimumDurationInSeconds);

        // Retrieves after-hours calls for a specific phone number
        Task<List<CallHistory>> GetAfterHoursCallsByPhoneNumberAsync(string phoneNumber, TimeSpan startBusinessTime, TimeSpan endBusinessTime);

        // Retrieves frequent call dates and times for a specific phone number
        Task<Dictionary<DateTime, int>> GetFrequentCallDatesByPhoneNumberAsync(string phoneNumber);

        // Retrieves the top N most recent calls for a specific phone number
        Task<List<CallHistory>> GetTopNRecentCallsAsync(string phoneNumber, int numberOfCalls);

        // Checks if a phone number has been contacted within a specified time window
        Task<bool> HasRecentCallWithinTimeSpanAsync(string phoneNumber, TimeSpan timeSpan);
    }
}