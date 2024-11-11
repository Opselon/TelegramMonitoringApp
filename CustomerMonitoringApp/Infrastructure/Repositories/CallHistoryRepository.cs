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
using Microsoft.Data.SqlClient;
using System.Data;
using System.Threading.Tasks.Dataflow;
using CustomerMonitoringApp.Domain.Views;
using Microsoft.EntityFrameworkCore.Storage;
using Polly;
using Serilog;
using Hangfire;

namespace CustomerMonitoringApp.Infrastructure.Repositories
{
    public class CallHistoryRepository : ICallHistoryRepository
    {
        private readonly AppDbContext _context;
        private readonly ILogger<CallHistoryRepository> _logger;
        private readonly IBackgroundJobClient _backgroundJobClient; // Hangfire background job client

        public CallHistoryRepository(AppDbContext context , ILogger<CallHistoryRepository> logger , IBackgroundJobClient backgroundJobClient)
        {
            _backgroundJobClient = backgroundJobClient;
            _context = context;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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


        // Begin a transaction
        /// <summary>
        /// Begins a database transaction and logs relevant transaction details.
        /// </summary>
        public async Task<IDbContextTransaction> BeginTransactionAsync()
        {
            var transactionId = Guid.NewGuid(); // Unique identifier for the transaction
            _logger.LogInformation($"Starting transaction with ID {transactionId}.");

            try
            {
                var transaction = await _context.Database.BeginTransactionAsync();
                _logger.LogInformation($"Transaction {transactionId} started successfully.");
                return transaction;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error starting transaction {transactionId}: {ex.Message}");
                throw new ApplicationException($"Transaction {transactionId} could not be started.", ex);
            }
        }

        /// <summary>
        /// Commits the transaction and logs relevant details.
        /// </summary>
        public async Task CommitTransactionAsync(IDbContextTransaction transaction)
        {
            var transactionId = Guid.NewGuid(); // Unique identifier for the transaction
            try
            {
                _logger.LogInformation($"Committing transaction {transactionId}.");

                // Commit the transaction
                await transaction.CommitAsync();
                _logger.LogInformation($"Transaction {transactionId} committed successfully.");
            }
            catch (Exception ex)
            {
                // Log the error before throwing it
                _logger.LogError($"Error committing transaction {transactionId}: {ex.Message}");
                throw new ApplicationException($"Transaction {transactionId} could not be committed.", ex);
            }
            finally
            {
                // Dispose of the transaction object if necessary, although EF Core handles this
                transaction?.Dispose();
            }
        }

        /// <summary>
        /// Rolls back the transaction and logs relevant details.
        /// </summary>
        public async Task RollbackTransactionAsync(IDbContextTransaction transaction)
        {
            var transactionId = Guid.NewGuid(); // Unique identifier for the transaction
            try
            {
                _logger.LogInformation($"Rolling back transaction {transactionId}.");

                // Rollback the transaction
                await transaction.RollbackAsync();
                _logger.LogInformation($"Transaction {transactionId} rolled back successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error rolling back transaction {transactionId}: {ex.Message}");
                throw new ApplicationException($"Transaction {transactionId} could not be rolled back.", ex);
            }
            finally
            {
                // Ensure that the transaction object is disposed of if necessary
                transaction?.Dispose();
            }
        }
      
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


        /// <summary>
        /// Saves bulk data to the database using SqlBulkCopy, executed as a Hangfire background job.
        /// </summary>
        public async Task SaveBulkDataAsync(DataTable dataTable)
        {
            string connectionString = "Data Source=.;Integrated Security=True;Encrypt=True;Trust Server Certificate=True";
            const int batchSize = 100000; // تنظیم اندازه دسته
            const int bulkCopyTimeout = 60; // تایم‌اوت بیشتر برای درج داده‌های حجیم

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    using (var transaction = connection.BeginTransaction())
                    {
                        using (var sqlBulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.TableLock, transaction))
                        {
                            sqlBulkCopy.DestinationTableName = "CallHistories";
                            sqlBulkCopy.BatchSize = batchSize;
                            sqlBulkCopy.EnableStreaming = true;
                            sqlBulkCopy.BulkCopyTimeout = bulkCopyTimeout;

                            // نقشه‌برداری ستون‌های DataTable به ستون‌های SQL
                            foreach (DataColumn column in dataTable.Columns)
                            {
                                sqlBulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
                            }

                            // برای بهینه‌سازی استفاده از CPU، از Parallel استفاده می‌کنیم
                            var totalRows = dataTable.Rows.Count;
                            var batches = Enumerable.Range(0, (int)Math.Ceiling((double)totalRows / batchSize))
                                .Select(i => dataTable.AsEnumerable().Skip(i * batchSize).Take(batchSize).CopyToDataTable())
                                .ToList();

                            // پردازش موازی دسته‌ها
                            await Task.WhenAll(batches.Select(async batchTable =>
                            {
                                try
                                {
                                    await sqlBulkCopy.WriteToServerAsync(batchTable);
                                    _logger.LogInformation($"Successfully inserted batch with {batchTable.Rows.Count} rows.");
                                }
                                catch (Exception batchEx)
                                {
                                    _logger.LogError(batchEx, "Error during batch insertion. Collecting failed rows.");

                                    // ثبت خطا برای ردیف‌های ناموفق
                                    foreach (DataRow row in batchTable.Rows)
                                    {
                                        try
                                        {
                                            var singleRowTable = batchTable.Clone();
                                            singleRowTable.ImportRow(row);
                                            await sqlBulkCopy.WriteToServerAsync(singleRowTable);
                                        }
                                        catch (Exception rowEx)
                                        {
                                            _logger.LogError(rowEx, "Failed to insert row: {RowData}", row.ItemArray);
                                        }
                                    }
                                }
                            }));

                            // اگر همه دسته‌ها با موفقیت درج شد، commit کنیم
                            transaction.Commit();
                            _logger.LogInformation("Bulk data saved successfully to the database.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Critical error during SqlBulkCopy operation: {ex.Message}. Operation aborted.");
                throw; // برای مدیریت خطا توسط لایه سرویس مجدد پرتاب شود
            }
        }
        // This method returns the user from the background job
        public async Task<User> GetUserDetailsByPhoneNumberAsync(string phoneNumber)
        {
            // First, try to fetch the user directly from the database
            var user = await _context.Users
                .Where(u => u.UserNumberFile == phoneNumber)
                .FirstOrDefaultAsync();

            if (user != null)
            {
                // If user is found, log or process further in the background if needed
                _backgroundJobClient.Enqueue(() => ProcessUserDetailsAsync(phoneNumber));

                // Return the user immediately
                return user;
            }
            else
            {
                // If user is not found, handle the case accordingly
                _logger.LogError("User not found.");
                return null;
            }
        }

        // This method is still executed as a background job for additional processing
        public async Task ProcessUserDetailsAsync(string phoneNumber)
        {
            var user = await _context.Users
                .Where(u => u.UserNumberFile == phoneNumber)
                .FirstOrDefaultAsync();

            if (user != null)
            {
                _logger.LogError($"User Found: {user.UserNumberFile}");
                // Additional logic like sending a notification or logging can go here
            }
            else
            {
                _logger.LogError("User not found.");
                // Handle the case when no user is found
            }
        }

        /// <summary>
        /// Handles rows that failed during bulk insert, attempting to insert them individually.
        /// </summary>
        private async Task HandleFailedRows(DataTable batchTable, SqlBulkCopy sqlBulkCopy)
        {
            foreach (DataRow row in batchTable.Rows)
            {
                try
                {
                    var singleRowTable = batchTable.Clone();
                    singleRowTable.ImportRow(row);

                    // Attempt to insert each individual row
                    await sqlBulkCopy.WriteToServerAsync(singleRowTable);
                }
                catch (Exception rowEx)
                {
                    _logger.LogError(rowEx, "Failed to insert row: {RowData}", row.ItemArray);
                }
            }
        }


        // Helper method to parse Persian date strings to DateTime
        private DateTime ParsePersianDate(string persianDate)
        {
            // Default result if the date is invalid
            var defaultDate = DateTime.MinValue; // Or you could use DateTime.Today, based on your preference

            // Check for null or empty input
            if (string.IsNullOrEmpty(persianDate))
            {
                _logger.LogWarning("Received null or empty Persian date. Defaulting to {defaultDate}.", defaultDate);
                return defaultDate; // Defaulting to a known fallback value
            }

            try
            {
                var persianCalendar = new PersianCalendar();
                var dateParts = persianDate.Split('/');

                // Check for invalid date format (should have exactly 3 parts)
                if (dateParts.Length != 3)
                {
                    _logger.LogWarning($"Invalid Persian date format '{persianDate}'. Expected format: yyyy/MM/dd. Defaulting to {defaultDate}.");
                    return defaultDate; // If format is wrong, return default date
                }

                // Attempt to parse year, month, and day
                int year = int.Parse(dateParts[0]);
                int month = int.Parse(dateParts[1]);
                int day = int.Parse(dateParts[2]);

                // Validate the month and day ranges based on Persian calendar
                if (month < 1 || month > 12 || day < 1 || day > 31 || (month > 6 && day > 30))
                {
                    _logger.LogWarning($"Invalid Persian date '{persianDate}' (day or month out of range). Storing incorrect date but logging for review. Defaulting to {defaultDate}.");
                    // Log the wrong date without discarding it, still return a fallback value
                    // You could also return the original value here if you want to store the invalid input
                    return defaultDate;
                }

                // Convert to DateTime using the Persian calendar
                DateTime resultDate = persianCalendar.ToDateTime(year, month, day, 0, 0, 0, 0); // No time component
                return resultDate;
            }
            catch (Exception ex)
            {
                // Log the error and return default date
                _logger.LogError($"Error parsing Persian date '{persianDate}': {ex.Message}. Returning default date {defaultDate}.");
                return defaultDate; // Default value when error occurs
            }
        }

        public async Task<List<CallHistory>> GetCallsByPhoneNumberAsync(string phoneNumber)
        {
            try
            {
                var callHistories = await _context.CallHistories
                    .Where(ch => ch.SourcePhoneNumber == phoneNumber || ch.DestinationPhoneNumber == phoneNumber)
                    .Select(ch => new CallHistory
                    {
                        SourcePhoneNumber = ch.SourcePhoneNumber,
                        DestinationPhoneNumber = ch.DestinationPhoneNumber,
                        CallDateTime = ch.CallDateTime,
                        Duration = ch.Duration,
                        CallType = ch.CallType,
                        FileName = ch.FileName
                    })
                    .ToListAsync();

                _logger.LogInformation($"Fetched {callHistories.Count} records for phone number: {phoneNumber}");
                return callHistories;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while fetching call history for phone number: {PhoneNumber}",
                    phoneNumber);
                return new List<CallHistory>(); // Return an empty list on failure
            }

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

        public async Task DeleteAllCallHistoriesAsync()
        {
            const int maxRetryAttempts = 5;
            const int baseDelayBetweenRetries = 2000;

            var retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    maxRetryAttempts,
                    attempt => TimeSpan.FromMilliseconds(baseDelayBetweenRetries * Math.Pow(2, attempt - 1)),
                    (exception, timespan, retryAttempt, context) =>
                    {
                        Log.Warning($"Retry {retryAttempt} after error: {exception.Message}. Waiting {timespan.TotalSeconds} seconds before retry.");
                    });

            // Execute the SQL commands within a transaction
            using (var transaction = await _context.Database.BeginTransactionAsync())
            {
                try
                {
                    await retryPolicy.ExecuteAsync(async () =>
                    {
                        // SQL command to truncate all records from the CallHistories table
                        string truncateSql = "TRUNCATE TABLE CallHistories;";
                        await _context.Database.ExecuteSqlRawAsync(truncateSql);

                        Log.Information("All records truncated and table reset successfully.");
                    });

                    await transaction.CommitAsync();
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    Log.Error($"Failed to truncate records. Transaction rolled back due to error: {ex.Message}");
                    throw;
                }
            }
        }


        public async Task DeleteCallHistoriesByFileNameAsync(string fileName)
        {
            const int batchSize = 500; // Size of each batch for deletion (adjust as needed)
            const int maxRetryAttempts = 5; // Max number of retries before giving up
            const int delayBetweenRetries = 1000; // Delay in milliseconds between retries (e.g., 2 seconds)

            // Validate input fileName
            if (string.IsNullOrEmpty(fileName))
            {
                throw new ArgumentException("File name cannot be null or empty.", nameof(fileName));
            }

            var totalRecordsToDelete = await _context.CallHistories
                .Where(ch => ch.FileName == fileName) // Filter based on file name
                .CountAsync();

            if (totalRecordsToDelete == 0)
            {
                // No records to delete
                return;
            }

            int deletedRecords = 0;
            int retryCount = 0;

            // Loop through the table and delete in batches
            while (deletedRecords < totalRecordsToDelete)
            {
                try
                {
                    // SQL command to delete records in batches
                    string sqlCommand = @"
                DELETE FROM CallHistories
                WHERE FileName = {0}
                AND CallId IN (
                    SELECT TOP(@batchSize) CallId
                    FROM CallHistories
                    WHERE FileName = {0}
                    ORDER BY CallId
                )";

                    // Execute SQL to delete the batch of records
                    var batchDeleted = await _context.Database.ExecuteSqlRawAsync(sqlCommand, fileName, batchSize);

                    if (batchDeleted > 0)
                    {
                        deletedRecords += batchDeleted;

                        // Log the progress of deletion
                        _logger.LogInformation($"Deleted {deletedRecords} records for file name '{fileName}' so far.");
                    }

                    // Reset retry count if successful
                    retryCount = 0;
                }
                catch (Exception ex)
                {
                    retryCount++;

                    if (retryCount > maxRetryAttempts)
                    {
                        _logger.LogError($"Failed to delete records for file name '{fileName}' after {maxRetryAttempts} attempts: {ex.Message}");
                        throw; // Rethrow exception after maximum retries
                    }

                    // Log the error and retry
                    _logger.LogWarning($"Error during batch deletion attempt {retryCount} for file name '{fileName}': {ex.Message}. Retrying in {delayBetweenRetries / 1000} seconds...");

                    // Wait before retrying
                    await Task.Delay(delayBetweenRetries);
                }
            }

            _logger.LogInformation($"Successfully deleted {deletedRecords} call history records for file name '{fileName}'.");
        }

        public async Task<List<CallHistoryWithUserNames>> GetCallsWithUserNamesAsync(string phoneNumber)
        {
            try
            {
                // Execute the query to retrieve calls and users
                var queryResults = await (
                    from call in _context.CallHistories
                    join caller in _context.Users on call.SourcePhoneNumber equals caller.UserNumberFile into callerInfo
                    from callerData in callerInfo.DefaultIfEmpty()
                    join receiver in _context.Users on call.DestinationPhoneNumber equals receiver.UserNumberFile into receiverInfo
                    from receiverData in receiverInfo.DefaultIfEmpty()
                    where call.SourcePhoneNumber == phoneNumber || call.DestinationPhoneNumber == phoneNumber
                    select new CallHistoryWithUserNames
                    {
                        CallId = call.CallId,
                        SourcePhoneNumber = call.SourcePhoneNumber,
                        DestinationPhoneNumber = call.DestinationPhoneNumber,
                        CallDateTime = call.CallDateTime,
                        Duration = call.Duration,
                        CallType = call.CallType,
                        FileName = call.FileName,
                        CallerName = callerData != null ? callerData.UserNameFile : string.Empty,
                        ReceiverName = receiverData != null ? receiverData.UserFamilyFile : string.Empty
                    }).ToListAsync();

                return queryResults;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetCallsWithUserNamesAsync: {ex.Message}");
                return new List<CallHistoryWithUserNames>(); // Return an empty list if there is an error
            }
        }

        #endregion

        #endregion
    }
}
