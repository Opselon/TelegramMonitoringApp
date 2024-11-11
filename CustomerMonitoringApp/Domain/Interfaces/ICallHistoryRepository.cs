using CustomerMonitoringApp.Domain.Entities;
using CustomerMonitoringApp.Domain.Views;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace CustomerMonitoringApp.Domain.Interfaces
{
    public interface ICallHistoryRepository
    {
        Task AddCallHistoryAsync(List<CallHistory> records);

           Task SaveBulkDataAsync(DataTable dataTable);
        // Retrieves all call histories for a specific phone number
        Task<List<CallHistory>> GetCallsByPhoneNumberAsync(string phoneNumber);

        // Retrieves recent call histories for a specific phone number based on a start date
        Task<List<CallHistory>> GetRecentCallsByPhoneNumberAsync(string phoneNumber, string startDate, string endDateTime);

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
        // New method to delete all call histories from the database
        Task DeleteAllCallHistoriesAsync();
        // New method to delete all call histories where the file name is "X"
        Task DeleteCallHistoriesByFileNameAsync(string fileName);
        Task<IDbContextTransaction> BeginTransactionAsync(); // Start a transaction
        Task CommitTransactionAsync(IDbContextTransaction transaction); // Commit the transaction
        Task RollbackTransactionAsync(IDbContextTransaction transaction); // Rollback if needed
        Task<List<CallHistoryWithUserNames>> GetCallsWithUserNamesAsync(string phoneNumber);

        Task<User> GetUserDetailsByPhoneNumberAsync(string phoneNumber);
    }
}