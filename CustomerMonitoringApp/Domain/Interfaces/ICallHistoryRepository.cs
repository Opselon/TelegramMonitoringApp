using CustomerMonitoringApp.Application.DTOs;
using CustomerMonitoringApp.Domain.Entities;
using CustomerMonitoringApp.Domain.Views;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace CustomerMonitoringApp.Domain.Interfaces
{
    /// <summary>
    /// Interface for the Call History repository, defining methods for managing call history records.
    /// </summary>
    public interface ICallHistoryRepository
    {
        /// <summary>
        /// Adds a list of call history records to the database asynchronously.
        /// </summary>
        /// <param name="records">List of call history records to add.</param>
        Task AddCallHistoryAsync(List<CallHistory> records);

        /// <summary>
        /// Saves a bulk dataset to the database using a DataTable.
        /// </summary>
        /// <param name="dataTable">DataTable containing bulk data for saving.</param>
        Task SaveBulkDataAsync(DataTable dataTable);

        /// <summary>
        /// Retrieves all call histories associated with a specific phone number.
        /// </summary>
        /// <param name="phoneNumber">The phone number to search for in call histories.</param>
        /// <returns>A list of call history records associated with the specified phone number.</returns>
        Task<List<CallHistory>> GetCallsByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken);

        /// <summary>
        /// Retrieves call histories for a specific phone number within a date range.
        /// </summary>
        /// <param name="phoneNumber">The phone number to search for in call histories.</param>
        /// <param name="startDate">The start date of the range.</param>
        /// <param name="endDateTime">The end date and time of the range.</param>
        /// <returns>A list of recent call histories within the specified date range.</returns>
        Task<List<CallHistory>> GetRecentCallsByPhoneNumberAsync(string phoneNumber, string startDate, string endDateTime);

        /// <summary>
        /// Retrieves long calls for a specific phone number that exceed a specified duration.
        /// </summary>
        /// <param name="phoneNumber">The phone number to search for in call histories.</param>
        /// <param name="minimumDurationInSeconds">The minimum call duration in seconds.</param>
        /// <returns>A list of call histories with durations exceeding the specified time.</returns>
        Task<List<CallHistory>> GetLongCallsByPhoneNumberAsync(string phoneNumber, int minimumDurationInSeconds);

        /// <summary>
        /// Retrieves calls outside specified business hours for a specific phone number.
        /// </summary>
        /// <param name="phoneNumber">The phone number to search for in call histories.</param>
        /// <param name="startBusinessTime">Start time of business hours.</param>
        /// <param name="endBusinessTime">End time of business hours.</param>
        /// <returns>A list of call histories outside of the specified business hours.</returns>
        Task<List<CallHistory>> GetAfterHoursCallsByPhoneNumberAsync(string phoneNumber, TimeSpan startBusinessTime, TimeSpan endBusinessTime);

        /// <summary>
        /// Retrieves the most frequent call dates and times for a specific phone number.
        /// </summary>
        /// <param name="phoneNumber">The phone number to search for in call histories.</param>
        /// <returns>A dictionary with dates and corresponding call frequency counts.</returns>
        Task<Dictionary<DateTime, int>> GetFrequentCallDatesByPhoneNumberAsync(string phoneNumber);
        Task<List<CallHistory>> GetCallHistoryByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken);
        Task<List<CallHistory>> GetAllCallHistoryAsync(CancellationToken cancellationToken);
        /// <summary>
        /// Retrieves the top N most recent call histories for a specific phone number.
        /// </summary>
        /// <param name="phoneNumber">The phone number to search for in call histories.</param>
        /// <param name="numberOfCalls">The number of recent calls to retrieve.</param>
        /// <returns>A list of the top N recent call histories.</returns>
        Task<List<CallHistory>> GetTopNRecentCallsAsync(string phoneNumber, int numberOfCalls);

        Task<int> GetCallHistoryCountAsync(CancellationToken cancellationToken);
        /// <summary>
        /// Checks if a phone number has been contacted within a specified time span.
        /// </summary>
        /// <param name="phoneNumber">The phone number to check.</param>
        /// <param name="timeSpan">The time span to search within.</param>
        /// <returns>True if the phone number was contacted within the time span; otherwise, false.</returns>
        Task<bool> HasRecentCallWithinTimeSpanAsync(string phoneNumber, TimeSpan timeSpan);

        /// <summary>
        /// Deletes all call histories from the database.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task DeleteAllCallHistoriesAsync();

        /// <summary>
        /// Deletes call histories from the database where the file name matches the specified name.
        /// </summary>
        /// <param name="fileName">The file name to match.</param>
        Task DeleteCallHistoriesByFileNameAsync(string fileName);

        /// <summary>
        /// Begins a database transaction asynchronously.
        /// </summary>
        /// <returns>An IDbContextTransaction representing the database transaction.</returns>
        Task<IDbContextTransaction> BeginTransactionAsync();

        /// <summary>
        /// Commits a specified database transaction asynchronously.
        /// </summary>
        /// <param name="transaction">The transaction to commit.</param>
        Task CommitTransactionAsync(IDbContextTransaction transaction);

        /// <summary>
        /// Rolls back a specified database transaction asynchronously.
        /// </summary>
        /// <param name="transaction">The transaction to roll back.</param>
        Task RollbackTransactionAsync(IDbContextTransaction transaction);

        /// <summary>
        /// Retrieves all call histories for a specific phone number along with user names.
        /// </summary>
        /// <param name="phoneNumber">The phone number to search for in call histories.</param>
        /// <returns>A list of CallHistoryWithUserNames containing call history records and user names.</returns>
        IAsyncEnumerable<CallHistoryWithUserNames> GetCallsWithUserNamesStreamAsync(
            string phoneNumber,
            [EnumeratorCancellation] CancellationToken cancellationToken);

        /// <summary>
        /// Retrieves user details based on their phone number.
        /// </summary>
        /// <param name="phoneNumber">The phone number to search for.</param>
        /// <returns>A User entity containing the details of the user.</returns>
        Task<User> GetUserDetailsByPhoneNumberAsync(string phoneNumber);

        /// <summary>
        /// Retrieves call histories for users based on their name and family name.
        /// Includes information on who called whom along with other call details.
        /// </summary>
        /// <param name="name">The first name of the user to search for.</param>
        /// <param name="family">The family name of the user to search for.</param>
        /// <returns>A list of CallHistoryWithUserNames containing call history records and user names.</returns>
        Task<List<CallHistoryWithUserNames>> GetCallsByUserNamesAsync(string name, string family);
        /// <summary>
        /// Fetch user details by phone number.
        /// </summary>
        /// <param name="phoneNumber">The phone number to search for.</param>
        /// <returns>User details or null if not found.</returns>
        ///
        /// 
        Task<IEnumerable<UserCallSmsStatistics>> GetEnhancedUserStatisticsWithPartnersAsync(string phoneNumber ,int topCount = 1);

        /// <summary>
        /// Get total call count for a specific phone number.
        /// </summary>
        /// <param name="phoneNumber">The phone number to search for.</param>
        /// <returns>Total number of calls made or received.</returns>
        Task<int> GetCallCountByPhoneNumberAsync(string phoneNumber);
        void GetEnhancedUserStatisticsWithPartnersInBackground(string phoneNumber, int topCount = 1);
        /// <summary>
        /// Get total message count for a specific phone number.
        /// </summary>
        /// <param name="phoneNumber">The phone number to search for.</param>
        /// <returns>Total number of messages sent or received.</returns>
        Task<int> GetMessageCountByPhoneNumberAsync(string phoneNumber);

    }
}
