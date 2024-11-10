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
using EFCore.BulkExtensions;
using System.Threading.Tasks.Dataflow;
using Microsoft.EntityFrameworkCore.Storage;
using FastMember;
using Polly;
using MediatR; // For handling as a MediatR command
using Serilog;

namespace CustomerMonitoringApp.Infrastructure.Repositories
{
    public class CallHistoryRepository : ICallHistoryRepository
    {
        private readonly AppDbContext _context;
        private readonly ILogger<CallHistoryRepository> _logger;

        public CallHistoryRepository(AppDbContext context , ILogger<CallHistoryRepository> logger)
        {
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

        public async Task SaveBulkDataAsync(DataTable dataTable)
        {
            string connectionString = "Data Source=.;Integrated Security=True;Encrypt=True;Trust Server Certificate=True";
            const int bulkCopyTimeout = 120; // Increased timeout for large data
            const int maxRetryAttempts = 3; // Max retry attempts for Polly
            const int baseDelayBetweenRetries = 2000; // Base delay between retries in ms
            const int optimalBatchSize = 9000; // Optimal batch size for bulk copy
            const int maxConcurrentThreads = 3; // Max concurrent threads for parallel processing

            // Retry policy with exponential backoff using Polly
            var retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    maxRetryAttempts,
                    attempt => TimeSpan.FromMilliseconds(baseDelayBetweenRetries * Math.Pow(2, attempt - 1)),
                    (exception, timespan, retryAttempt, context) =>
                    {
                        _logger.LogWarning($"Retry {retryAttempt} encountered an error: {exception.Message}. Waiting {timespan.TotalSeconds} seconds before retry.");
                    });

            try
            {
                // Retry logic to handle transient failures
                await retryPolicy.ExecuteAsync(async () =>
                {
                    using (var connection = new SqlConnection(connectionString))
                    {
                        await connection.OpenAsync();

                        // Start a transaction to ensure atomicity
                        using (var transaction = await connection.BeginTransactionAsync())
                        {
                            try
                            {
                                // Split data into chunks based on optimal batch size
                                var totalRows = dataTable.Rows.Count;
                                var numberOfBatches = (int)Math.Ceiling((double)totalRows / optimalBatchSize);

                                var tasks = new List<Task>();

                                for (int batchIndex = 0; batchIndex < numberOfBatches; batchIndex++)
                                {
                                    // Split the DataTable into smaller chunks
                                    var batchStartIndex = batchIndex * optimalBatchSize;
                                    var batchEndIndex = Math.Min(batchStartIndex + optimalBatchSize, totalRows);

                                    var batch = dataTable.Clone();
                                    for (int rowIndex = batchStartIndex; rowIndex < batchEndIndex; rowIndex++)
                                    {
                                        batch.ImportRow(dataTable.Rows[rowIndex]);
                                    }

                                    // Process each batch in parallel (up to `maxConcurrentThreads`)
                                    if (tasks.Count >= maxConcurrentThreads)
                                    {
                                        await Task.WhenAny(tasks); // Wait for at least one task to finish before continuing
                                        tasks.RemoveAll(t => t.IsCompleted);
                                    }

                                    tasks.Add(Task.Run(async () =>
                                    {
                                        using (var reader = ObjectReader.Create(batch.AsEnumerable(), batch.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToArray()))
                                        using (var sqlBulkCopy = new SqlBulkCopy(connection, Microsoft.Data.SqlClient.SqlBulkCopyOptions.TableLock, (SqlTransaction)transaction))
                                        {
                                            sqlBulkCopy.DestinationTableName = "CallHistories"; // Target table name
                                            sqlBulkCopy.EnableStreaming = true; // Enable streaming for large data
                                            sqlBulkCopy.BulkCopyTimeout = bulkCopyTimeout; // Timeout for the bulk copy operation
                                            sqlBulkCopy.BatchSize = optimalBatchSize; // Set optimal batch size for each batch
                   

                                            // Map columns between DataTable and SQL table
                                            foreach (DataColumn column in batch.Columns)
                                            {
                                                sqlBulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
                                            }

                                            try
                                            {
                                                // Perform the bulk insert for this batch
                                                await sqlBulkCopy.WriteToServerAsync(reader);
                                                _logger.LogInformation($"Successfully inserted {batch.Rows.Count} rows.");
                                            }
                                            catch (Exception batchEx)
                                            {
                                                _logger.LogError(batchEx, "Error during batch insertion. Collecting failed rows.");
                                                HandleFailedRows(batch, sqlBulkCopy); // Implement custom logic for failed rows
                                            }
                                        }
                                    }));
                                }

                                // Ensure all parallel tasks are completed
                                await Task.WhenAll(tasks);

                                // Commit transaction after successful insertion of all batches
                                await transaction.CommitAsync();
                                _logger.LogInformation("Bulk data saved successfully to the database.");
                            }
                            catch (Exception ex)
                            {
                                // Rollback the transaction if any error occurs
                                await transaction.RollbackAsync();
                                _logger.LogError($"Error during bulk operation: {ex.Message}. Transaction rolled back.");
                                throw;
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                // Log and throw critical errors that occur during the bulk operation
                _logger.LogError($"Critical error during bulk data save: {ex.Message}. Operation aborted.");
                throw;
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
                        CallType = ch.CallType,
                        FileName = ch.FileName
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

        public async Task DeleteAllCallHistoriesAsync()
        {
            const int batchSize = 80000;
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

            var totalRecordsToDelete = await _context.CallHistories.CountAsync();
            if (totalRecordsToDelete == 0)
            {
                Log.Information("No records to delete.");
                return;
            }

            int deletedRecords = 0;

            using (var transaction = await _context.Database.BeginTransactionAsync())
            {
                try
                {
                    while (deletedRecords < totalRecordsToDelete)
                    {
                        await retryPolicy.ExecuteAsync(async () =>
                        {
                            var batch = await _context.CallHistories
                                .OrderBy(ch => ch.CallId)
                                .Take(batchSize)
                                .ToListAsync();

                            if (!batch.Any())
                            {
                                Log.Information("No more records found for deletion.");
                                return;
                            }

                            // Delete the batch using EFCore.BulkExtensions
                            await _context.BulkDeleteAsync(batch);
                            deletedRecords += batch.Count;

                            Log.Information($"Deleted {deletedRecords} records so far.");
                        });
                    }

                    await transaction.CommitAsync();
                    Log.Information($"Successfully deleted {deletedRecords} call history records.");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    Log.Error($"Failed to delete records. Transaction rolled back due to error: {ex.Message}");
                    throw;
                }
            }
        }

        public async Task DeleteCallHistoriesByFileNameAsync(string fileName)
        {
            const int batchSize = 80000; // Size of each batch for deletion (adjust as needed)
            const int maxRetryAttempts = 5; // Max number of retries before giving up
            const int delayBetweenRetries = 2000; // Delay in milliseconds between retries (e.g., 2 seconds)

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
                    var batch = await _context.CallHistories
                        .Where(ch => ch.FileName == fileName) // Filter based on file name
                        .OrderBy(ch => ch.CallId) // Assuming there's a primary key column "CallId"
                        .Take(batchSize)
                        .ToListAsync();

                    if (batch.Any())
                    {
                        _context.CallHistories.RemoveRange(batch);
                        await _context.SaveChangesAsync();

                        deletedRecords += batch.Count;

                        // Log the progress of deletion (e.g., every 80k rows)
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

        #endregion

        #endregion
    }
}
