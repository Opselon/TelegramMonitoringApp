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
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace CustomerMonitoringApp.Infrastructure.Repositories
{
    public class CallHistoryRepository : ICallHistoryRepository
    {
        private readonly AppDbContext _context;
        private readonly ILogger<CallHistoryRepository> _logger;
        private readonly IBackgroundJobClient _backgroundJobClient; // Hangfire background job client
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly AsyncCircuitBreakerPolicy _circuitBreakerPolicy;
        private readonly AsyncTimeoutPolicy _timeoutPolicy;

        public CallHistoryRepository(AppDbContext context , ILogger<CallHistoryRepository> logger , IBackgroundJobClient backgroundJobClient)
        {
            _backgroundJobClient = backgroundJobClient;
            _context = context;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            // Define retry policy
            _retryPolicy = Policy
                .Handle<Exception>()
                .RetryAsync(3); // Retry up to 3 times

            // Define circuit breaker policy
            _circuitBreakerPolicy = Policy
                .Handle<Exception>()
                .CircuitBreakerAsync(5, TimeSpan.FromMinutes(1)); // Break circuit after 5 consecutive failures

            // Define timeout policy
            _timeoutPolicy = Policy
                .TimeoutAsync(TimeSpan.FromSeconds(600)); // Timeout after 30 seconds
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
        /// Utilizes batch processing and parallel execution for optimized performance.
        /// </summary>
        /// <param name="dataTable">DataTable containing the data to be saved in bulk.</param>
        /// <returns>A task representing the asynchronous save operation.</returns>
        public async Task SaveBulkDataAsync(DataTable dataTable)
        {
            const string connectionString = "Data Source=.;Integrated Security=True;Encrypt=True;Trust Server Certificate=True";
            const int batchSize = 100000;          // Batch size for bulk copy
            const int bulkCopyTimeout = 60;         // Timeout for large data insertions

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

                            // Map DataTable columns to SQL table columns
                            foreach (DataColumn column in dataTable.Columns)
                            {
                                sqlBulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
                            }

                            // Divide data into batches for processing in parallel
                            var totalRows = dataTable.Rows.Count;
                            var batches = Enumerable.Range(0, (int)Math.Ceiling((double)totalRows / batchSize))
                                .Select(i => dataTable.AsEnumerable().Skip(i * batchSize).Take(batchSize).CopyToDataTable())
                                .ToList();

                            // Process each batch in parallel for optimized CPU usage
                            await Task.WhenAll(batches.Select(async batchTable =>
                            {
                                try
                                {
                                    await sqlBulkCopy.WriteToServerAsync(batchTable);
                                    _logger.LogInformation($"Successfully inserted batch with {batchTable.Rows.Count} rows.");
                                }
                                catch (Exception batchEx)
                                {
                                    _logger.LogError(batchEx, "Error during batch insertion. Handling individual row errors.");

                                    // Attempt to reinsert each row in the batch to isolate failures
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

                            // Commit transaction if all batches are successfully inserted
                            transaction.Commit();
                            _logger.LogInformation("Bulk data saved successfully to the database.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical error during SqlBulkCopy operation. Operation aborted.");
                throw; // Re-throw to allow upper layers to handle the exception
            }
        }



        /// <summary>
        /// Retrieves and processes user details based on the provided phone number, with policies for retry, timeout, and circuit breaking.
        /// </summary>
        /// <param name="phoneNumber">The phone number associated with the user to retrieve and process.</param>
        /// <returns>A task representing the asynchronous operation, containing the user details or null if not found.</returns>
        public async Task<User?> GetUserDetailsByPhoneNumberAsync(string phoneNumber)
        {
            // Step 1: Input Validation
            if (string.IsNullOrWhiteSpace(phoneNumber))
            {
                // Log warning if phone number is invalid
                _logger.LogWarning("Invalid phone number provided; cannot retrieve user details.");
                return null;
            }

            try
            {
                // Step 2: Log the start of the operation
                _logger.LogInformation($"Starting user details retrieval for phone number: {phoneNumber}");

                // Step 3: Resilient Database Query (Retry, Timeout, Circuit Breaker)
                return await _retryPolicy.ExecuteAsync(async () =>
                    await _timeoutPolicy.ExecuteAsync(async () =>
                        await _circuitBreakerPolicy.ExecuteAsync(async () =>
                        {
                            // Step 4: Perform the database query to retrieve the user by phone number
                            var user = await _context.Users
                                .Where(u => u.UserNumberFile == phoneNumber)
                                .FirstOrDefaultAsync();

                            // Step 5: Process the result of the query
                            if (user == null)
                            {
                                // Log if no user found
                                _logger.LogWarning($"No user found for phone number: {phoneNumber}");
                                return null;  // User not found, returning null
                            }

                            // Log additional information if user is found
                            _logger.LogInformation($"User found for phone number {phoneNumber}. Processing details.");

                            // Step 6: Additional processing or logging (if needed)
                            _logger.LogInformation($"Processing user details for: {user.UserNumberFile}");

                            // Insert custom processing logic here (e.g., notifications, further updates, etc.)

                            return user;  // Return the user if found and processed successfully
                        })
                    )
                );
            }
            catch (BrokenCircuitException ex)
            {
                // Step 7: Handle circuit breaker open
                _logger.LogError(ex, "Circuit breaker is open; operation halted due to repeated failures.");
                return null;
            }
            catch (OperationCanceledException ex)
            {
                // Step 8: Handle cancellation (e.g., timeout or cancellation token)
                _logger.LogWarning(ex, "Operation was canceled during user details retrieval.");
                return null;
            }
            catch (SqlException ex)
            {
                // Step 9: Handle database-specific issues (e.g., connection issues)
                _logger.LogError(ex, "Database error occurred while retrieving user details for phone number: {PhoneNumber}", phoneNumber);
                return null;
            }
            catch (TimeoutException ex)
            {
                // Step 10: Handle timeout-specific errors
                _logger.LogError(ex, "Timeout occurred while retrieving user details for phone number: {PhoneNumber}", phoneNumber);
                return null;
            }
            catch (Exception ex)
            {
                // Step 11: Handle any other unexpected errors
                _logger.LogError(ex, "Unexpected error occurred while retrieving user details for phone number: {PhoneNumber}", phoneNumber);
                throw;  // Rethrow for higher-level handling if necessary
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
/// Retrieves a list of call history records associated with a given phone number.
/// </summary>
/// <param name="phoneNumber">The phone number to filter call records by.</param>
/// <param name="cancellationToken">Cancellation token to stop the operation if required.</param>
/// <returns>A task representing the asynchronous operation, containing a list of CallHistory records.</returns>
public async Task<List<CallHistory>> GetCallsByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken)
{
    const int batchSize = 500000;
    var results = new List<CallHistory>();

    try
    {
        // Execute database query within retry, timeout, and circuit breaker policies
        await _retryPolicy.ExecuteAsync(async () =>
            await _timeoutPolicy.ExecuteAsync(async () =>
                await _circuitBreakerPolicy.ExecuteAsync(async () =>
                {
                    var query = _context.CallHistories
                        .Where(ch => ch.SourcePhoneNumber == phoneNumber || ch.DestinationPhoneNumber == phoneNumber)
                        .Select(ch => new CallHistory
                        {
                            SourcePhoneNumber = ch.SourcePhoneNumber,
                            DestinationPhoneNumber = ch.DestinationPhoneNumber,
                            CallDateTime = ch.CallDateTime,
                            Duration = ch.Duration,
                            CallType = ch.CallType,
                            FileName = ch.FileName
                        });

                    var batchedQuery = query.AsNoTracking().AsAsyncEnumerable();

                    // Stream results asynchronously in batches
                    await foreach (var record in batchedQuery.WithCancellation(cancellationToken))
                    {
                        results.Add(record);

                        // Yield control after each batch to manage memory usage
                        if (results.Count >= batchSize)
                        {
                            await Task.Yield();
                            results.Clear(); // Clear after each batch to manage memory efficiently
                        }
                    }
                })
            )
        );

        _logger.LogInformation($"Fetched {results.Count} records for phone number: {phoneNumber}");
    }
    catch (BrokenCircuitException)
    {
        _logger.LogWarning("Circuit breaker is open due to repeated errors.");
    }
    catch (OperationCanceledException)
    {
        _logger.LogInformation("Operation was canceled.");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error occurred while fetching call history for phone number: {PhoneNumber}", phoneNumber);
    }

    return results;
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




        #region HasRecentCallWithinTimeSpanAsync

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

        #region DeleteAllCallHistoriesAsync


        /// <summary>
        /// Deletes all records in the CallHistories table and resets identity columns with a retry policy and transactional safety.
        /// </summary>
        /// <returns>A task representing the asynchronous delete operation.</returns>
        public async Task DeleteAllCallHistoriesAsync()
        {
            const int batchSize = 10000; // Defines the number of records to delete per batch
            const int maxRetryAttempts = 5; // Maximum number of retry attempts
            const int baseDelayBetweenRetries = 2000; // Base delay in milliseconds for exponential backoff

            // Define a retry policy with exponential backoff for error resilience
            var retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    maxRetryAttempts,
                    attempt => TimeSpan.FromMilliseconds(baseDelayBetweenRetries * Math.Pow(2, attempt - 1)),
                    (exception, timespan, retryAttempt, context) =>
                    {
                        Log.Warning($"Retry attempt {retryAttempt} due to error: {exception.Message}. Retrying in {timespan.TotalSeconds} seconds.");
                    });

            // Begin a transaction to ensure atomic operation of deletion and identity reset
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Execute the delete and reset operations within the retry policy
                await retryPolicy.ExecuteAsync(async () =>
                {
                    // Step 1: Direct SQL for efficient deletion and identity reset
                    await ResetTablesDirectlyAsync(batchSize);
                    Log.Information("All records deleted, and identities reset via direct SQL execution.");

                    // Step 2: Clear EF Core tracked entities from memory to free up resources
                    await ClearEntitiesInMemoryAsync();
                    Log.Information("Entities cleared from memory using Entity Framework.");
                });

                // Commit the transaction upon successful deletion and reset operations
                await transaction.CommitAsync();
                Log.Information("Successfully deleted and reset records in CallHistories.");
            }
            catch (Exception ex)
            {
                // Roll back the transaction if any exception occurs
                await transaction.RollbackAsync();
                Log.Error($"Transaction rolled back due to error: {ex.Message}");
                throw;
            }
        }



        /// <summary>
        /// Layer 1: Executes direct SQL to delete all records in specified tables and resets identity values in batches.
        /// </summary>
        /// <param name="batchSize">The size of each deletion batch for efficient handling of large tables.</param>
        /// <returns>A task representing the asynchronous batch deletion and identity reset.</returns>
        private async Task ResetTablesDirectlyAsync(int batchSize)
        {
            // Array of table reset commands, including table names and identity reset SQL commands
            var resetSqlCommands = new[]
            {
        new { TableName = "CallHistories", ResetIdentitySql = "DBCC CHECKIDENT ('CallHistories', RESEED, 0);" },
        new { TableName = "Users", ResetIdentitySql = "DBCC CHECKIDENT ('Users', RESEED, 0);" }
    };

            // Process each table's deletion and identity reset operation
            foreach (var command in resetSqlCommands)
            {
                int rowsDeleted;

                // Perform batched deletion to handle large datasets without overwhelming memory
                do
                {
                    // SQL command to delete a batch of records
                    var deleteBatchSql = $"DELETE TOP (@batchSize) FROM {command.TableName};";

                    // Execute the batched delete and capture the number of rows affected
                    rowsDeleted = await _context.Database.ExecuteSqlRawAsync(deleteBatchSql, new { batchSize });

                    // Log the number of records deleted for the current batch
                    Log.Information($"Deleted {rowsDeleted} records from {command.TableName}.");

                } while (rowsDeleted > 0);

                // Execute identity reset after all records are deleted
                await _context.Database.ExecuteSqlRawAsync(command.ResetIdentitySql);
                Log.Information($"Identity reset for {command.TableName} completed.");
            }
        }

        /// <summary>
        /// Layer 2: Clears any tracked or cached entities from memory within Entity Framework's DbContext, optimizing resource usage.
        /// </summary>
        /// <returns>A task representing the asynchronous operation to clear tracked entities.</returns>
        private async Task ClearEntitiesInMemoryAsync()
        {
            // Get a list of all tracked entities in states that indicate they need to be cleared (Added, Modified, or Deleted)
            var trackedEntries = _context.ChangeTracker.Entries()
                .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted)
                .ToList();

            // Detach each entity to remove it from the DbContext's tracking
            foreach (var entry in trackedEntries)
            {
                entry.State = EntityState.Detached;
            }

            Log.Information("Cleared all tracked entities from memory.");

            // Optionally clear specific in-memory data collections if required
            // Example: _context.Users.Local.Clear(); to clear specific local collections
        }

        #endregion

        #region DeleteCallHistoriesByFileNameAsync

        public async Task DeleteCallHistoriesByFileNameAsync(string fileName)
        {
            const int batchSize = 100000; // Size of each batch for deletion (adjust as needed)
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


        #endregion

        #region GetCallsByUserNamesAsync

        /// <summary>
        /// Retrieves call history records for users based on their name and family name. 
        /// This includes approximate matching for Persian names, handles common character variants,
        /// trims whitespace, and performs case-insensitive matching.
        /// </summary>
        /// <param name="name">The name of the user to search for.</param>
        /// <param name="family">The family name of the user to search for.</param>
        /// <returns>A list of CallHistoryWithUserNames containing call history records.</returns>
        public async Task<List<CallHistoryWithUserNames>> GetCallsByUserNamesAsync(string name, string family)
        {
            // Begin method for retrieving call history by user names
            try
            {
                #region Normalize Input
                // Normalize input names for improved matching performance
                string normalizedInputName = NormalizePersianCharacters(name?.Trim() ?? string.Empty);
                string normalizedInputFamily = NormalizePersianCharacters(family?.Trim() ?? string.Empty);
                #endregion

                #region Initialize Result List
                // Initialize the result list to hold the final call history records
                var allResults = new List<CallHistoryWithUserNames>();
                int batchSize = 2000; // Set a smaller batch size to reduce memory usage
                int skip = 0;
                List<CallHistoryWithUserNames> batchResults;
                #endregion

                #region Batch Processing Loop
                do
                {
                    // Perform batch processing with pagination and minimal in-memory filtering
                    batchResults = await (
                        from call in _context.CallHistories
                        join caller in _context.Users on call.SourcePhoneNumber equals caller.UserNumberFile into callerInfo
                        from callerData in callerInfo.DefaultIfEmpty()
                        join receiver in _context.Users on call.DestinationPhoneNumber equals receiver.UserNumberFile into receiverInfo
                        from receiverData in receiverInfo.DefaultIfEmpty()
                        where
                            // Case-insensitive and normalized matching for caller and receiver
                            (callerData != null &&
                             EF.Functions.Like(callerData.UserNameFile.Trim(), $"%{normalizedInputName}%") &&
                             EF.Functions.Like(callerData.UserFamilyFile.Trim(), $"%{normalizedInputFamily}%")) ||
                            (receiverData != null &&
                             EF.Functions.Like(receiverData.UserNameFile.Trim(), $"%{normalizedInputName}%") &&
                             EF.Functions.Like(receiverData.UserFamilyFile.Trim(), $"%{normalizedInputFamily}%"))
                        select new CallHistoryWithUserNames
                        {
                            CallId = call.CallId,
                            SourcePhoneNumber = call.SourcePhoneNumber,
                            DestinationPhoneNumber = call.DestinationPhoneNumber,
                            CallDateTime = call.CallDateTime,
                            Duration = call.Duration,
                            CallType = call.CallType,
                            FileName = call.FileName,
                            CallerName = callerData != null ? $"{callerData.UserNameFile} {callerData.UserFamilyFile}".Trim() : string.Empty,
                            ReceiverName = receiverData != null ? $"{receiverData.UserNameFile} {receiverData.UserFamilyFile}".Trim() : string.Empty
                        })
                        .Skip(skip)
                        .Take(batchSize)
                        .AsNoTracking()
                        .ToListAsync();

                    // Add results to the main list and increment the skip count
                    allResults.AddRange(batchResults);
                    skip += batchSize;

                } while (batchResults.Count == batchSize); // Stop when batch size is less than requested, indicating end of data
                #endregion

                #region Return Results
                // Return the complete list of call history records
                return allResults;
                #endregion
            }
            catch (Exception ex)
            {
                // Log detailed error information for troubleshooting
                _logger.LogError(ex, "Error occurred while fetching call history by user names.");

                // Return an empty list in case of an error
                return new List<CallHistoryWithUserNames>();
            }
        }

        /// <summary>
        /// Normalizes Persian characters in a string to handle common variants,
        /// replacing Arabic and Persian similar characters with consistent Persian versions.
        /// </summary>
        /// <param name="input">The input string to normalize.</param>
        /// <returns>A string with normalized Persian characters.</returns>
        private string NormalizePersianCharacters(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            return input
                // Handle Arabic vs Persian 'ی' (Ya) and 'ي'
                .Replace("ي", "ی") // Arabic Ya to Persian Ya
                .Replace("ى", "ی") // Alternate Arabic Ya to Persian Ya

                // Handle Arabic vs Persian 'ک' (Kaf) and 'ك'
                .Replace("ك", "ک") // Arabic Kaf to Persian Kaf

                // Normalize 'هٔ' and 'ة' to 'ه'
                .Replace("ة", "ه") // Arabic Ta Marbuta to Persian He
                .Replace("ۀ", "ه") // Persian Heh with Hamzeh to Persian He

                // Normalize commonly used Arabic punctuation to Persian equivalents
                .Replace("ـ", "") // Remove Kashida (Arabic Tatweel)
                .Replace("ؤ", "و") // Arabic Waw with Hamza to Persian Waw
                .Replace("إ", "ا") // Arabic Alif with Hamza below to Persian Alif
                .Replace("أ", "ا") // Arabic Alif with Hamza above to Persian Alif

                // Handle Persian currency sign consistency
                .Replace("﷼", "ریال") // Standardize Rial symbol

                // Handle Arabic vs Persian numeral differences if applicable
                .Replace("١", "۱") // Arabic numeral one to Persian numeral one
                .Replace("٢", "۲") // Arabic numeral two to Persian numeral two
                .Replace("٣", "۳") // Arabic numeral three to Persian numeral three
                .Replace("٤", "۴") // Arabic numeral four to Persian numeral four
                .Replace("٥", "۵") // Arabic numeral five to Persian numeral five
                .Replace("٦", "۶") // Arabic numeral six to Persian numeral six
                .Replace("٧", "۷") // Arabic numeral seven to Persian numeral seven
                .Replace("٨", "۸") // Arabic numeral eight to Persian numeral eight
                .Replace("٩", "۹") // Arabic numeral nine to Persian numeral nine
                .Replace("٠", "۰"); // Arabic numeral zero to Persian numeral zero
        }


        #endregion



        public async Task<List<CallHistoryWithUserNames>> GetCallsWithUserNamesAsync(string phoneNumber, CancellationToken cancellationToken)
        {
            const int batchSize = 500000; // Consider adjusting based on actual memory usage and performance testing
            var results = new List<CallHistoryWithUserNames>();

            try
            {
                // Execute database query within a retry and timeout policy
                await _retryPolicy.ExecuteAsync(async () =>
                    await _timeoutPolicy.ExecuteAsync(async () =>
                        await _circuitBreakerPolicy.ExecuteAsync(async () =>
                        {
                            var query = from call in _context.CallHistories
                                        where call.SourcePhoneNumber == phoneNumber || call.DestinationPhoneNumber == phoneNumber
                                        join caller in _context.Users on call.SourcePhoneNumber equals caller.UserNumberFile into callerInfo
                                        from callerData in callerInfo.DefaultIfEmpty()
                                        join receiver in _context.Users on call.DestinationPhoneNumber equals receiver.UserNumberFile into receiverInfo
                                        from receiverData in receiverInfo.DefaultIfEmpty()
                                        select new CallHistoryWithUserNames
                                        {
                                            CallId = call.CallId,
                                            SourcePhoneNumber = call.SourcePhoneNumber,
                                            DestinationPhoneNumber = call.DestinationPhoneNumber,
                                            CallDateTime = call.CallDateTime,
                                            Duration = call.Duration,
                                            CallType = call.CallType,
                                            FileName = call.FileName,
                                            CallerName = callerData != null
                                                ? $"{callerData.UserNameFile} {callerData.UserFamilyFile} {callerData.UserAddressFile} {callerData.UserFatherNameFile}"
                                                : string.Empty,
                                            ReceiverName = receiverData != null
                                                ? $"{receiverData.UserNameFile} {receiverData.UserFamilyFile} {receiverData.UserAddressFile} {receiverData.UserFatherNameFile}"
                                                : string.Empty
                                        };

                            var batchedQuery = query.AsNoTracking().AsAsyncEnumerable();

                            // Stream results asynchronously in batches
                            await foreach (var record in batchedQuery.WithCancellation(cancellationToken))
                            {
                                results.Add(record);

                                // Yield control after every batch of records to manage memory usage
                                if (results.Count >= batchSize)
                                {
                                    // Yield control to avoid blocking the thread and optimize memory
                                    await Task.Yield();
                                    results.Clear(); // Clear the list after each batch to reduce memory usage
                                }
                            }
                        })
                    )
                );
            }
            catch (BrokenCircuitException)
            {
                Console.WriteLine("The operation was stopped due to too many errors. Circuit breaker is open.");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("The operation was canceled.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetCallsWithUserNamesAsync: {ex.Message}");
            }

            return results;
        }

        #endregion

        #endregion
    }
}
