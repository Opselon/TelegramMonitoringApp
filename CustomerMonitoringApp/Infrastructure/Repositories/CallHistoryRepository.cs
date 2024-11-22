using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Runtime.CompilerServices;
using CustomerMonitoringApp.Domain.Entities;
using CustomerMonitoringApp.Domain.Interfaces;
using CustomerMonitoringApp.Domain.Views;
using CustomerMonitoringApp.Infrastructure.Data;
using Hangfire;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using Serilog;
using Dapper;
using System.Data.SqlClient;
using CustomerMonitoringApp.Application.DTOs;


namespace CustomerMonitoringApp.Infrastructure.Repositories;

public class CallHistoryRepository : ICallHistoryRepository
{
    private readonly IBackgroundJobClient _backgroundJobClient; // Hangfire background job client
    private readonly AsyncCircuitBreakerPolicy _circuitBreakerPolicy;
    private readonly AppDbContext _context;
    private readonly ILogger<CallHistoryRepository> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;

    private readonly string _connectionString;
    private readonly AsyncTimeoutPolicy _timeoutPolicy;

    public CallHistoryRepository(AppDbContext context, ILogger<CallHistoryRepository> logger,
        IBackgroundJobClient backgroundJobClient)
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
        _connectionString = "Data Source=.;Integrated Security=True;Encrypt=True;Trust Server Certificate=True";
        // Define timeout policy
        _timeoutPolicy = Policy
            .TimeoutAsync(TimeSpan.FromSeconds(6000)); // Timeout after 30 seconds
    }

    public async Task<int> GetCallHistoryCountByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        return await _context.CallHistories
            .Where(c => c.SourcePhoneNumber == phoneNumber || c.DestinationPhoneNumber == phoneNumber)
            .CountAsync(cancellationToken);
    }

    public async Task<int> GetTotalCallHistoryCountAsync(CancellationToken cancellationToken)
    {
        return await _context.CallHistories
            .CountAsync(cancellationToken);
    }


    #region Methods for Adding and Retrieving Call History

    /// <summary>
    ///     Adds a list of call history records to the database.
    /// </summary>
    public async Task AddCallHistoryAsync(List<CallHistory> records)
    {
        await _context.CallHistories.AddRangeAsync(records);
        await _context.SaveChangesAsync();
    }

    #endregion

    #region New Methods for Searching by Phone Number

    // Begin a transaction
    /// <summary>
    ///     Begins a database transaction and logs relevant transaction details.
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

    public async Task<List<CallHistory>> GetCallHistoryByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        return await _context.CallHistories
            .Where(c => c.SourcePhoneNumber == phoneNumber || c.DestinationPhoneNumber == phoneNumber)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<CallHistory>> GetAllCallHistoryAsync(CancellationToken cancellationToken)
    {
        return await _context.CallHistories.ToListAsync(cancellationToken);
    }

    

    /// <summary>
    ///     Commits the transaction and logs relevant details.
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
    ///     Rolls back the transaction and logs relevant details.
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


    public async Task<List<CallHistory>> GetRecentCallsByPhoneNumberAsync(string phoneNumber, string startDate,
        string endDateTime)
    {
        try
        {
            // Parse Persian dates to Gregorian DateTime
            var parsedStartDate = ParsePersianDate(startDate);
            var parsedEndDateTime = ParsePersianDate(endDateTime);

            var callHistories = await _context.CallHistories
                .Where(ch => ch.SourcePhoneNumber == phoneNumber || ch.DestinationPhoneNumber == phoneNumber)
                .ToListAsync();

            // فیلتر کردن تاریخ‌ها در کد
            var filteredCallHistories = callHistories
                .Where(ch =>
                    DateTime.Parse(ch.CallDateTime) >= parsedStartDate &&
                    DateTime.Parse(ch.CallDateTime) <= parsedEndDateTime)
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
    ///     Saves bulk data to the database using SqlBulkCopy, executed as a Hangfire background job.
    ///     Utilizes batch processing and parallel execution for optimized performance.
    /// </summary>
    /// <param name="dataTable">DataTable containing the data to be saved in bulk.</param>
    /// <returns>A task representing the asynchronous save operation.</returns>
    public async Task SaveBulkDataAsync(DataTable dataTable)
    {
        const string connectionString =
            "Data Source=.;Integrated Security=True;Encrypt=True;Trust Server Certificate=True";
        const int batchSize = 100000; // Batch size for bulk copy
        const int bulkCopyTimeout = 60; // Timeout for large data insertions

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
                            sqlBulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);

                        // Ensure FileName field fits within the database column size
                        foreach (DataRow row in dataTable.Rows)
                        {
                            row["FileName"] = TrimToMaxLength(row["FileName"].ToString(), 20); // Adjust the length
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
                                _logger.LogInformation(
                                    $"Successfully inserted batch with {batchTable.Rows.Count} rows.");
                            }
                            catch (Exception batchEx)
                            {
                                _logger.LogError(batchEx,
                                    "Error during batch insertion. Handling individual row errors.");

                                // Attempt to reinsert each row in the batch to isolate failures
                                foreach (DataRow row in batchTable.Rows)
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
    private string TrimToMaxLength(string input, int maxLength)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // Trim or return the string within the maxLength limit
        return input.Length > maxLength ? input.Substring(0, maxLength) : input;
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
            _logger.LogWarning("Invalid phone number provided; cannot retrieve user details.");
            return null;
        }

        try
        {
            // Normalize phone number before querying
            phoneNumber = NormalizePhoneNumber(phoneNumber);

            _logger.LogInformation("Starting user details retrieval for phone number: {PhoneNumber}", phoneNumber);

            // Step 2: Resilient Database Query (Retry, Timeout, Circuit Breaker)
            return await _retryPolicy.ExecuteAsync(async () =>
                await _timeoutPolicy.ExecuteAsync(async () =>
                    await _circuitBreakerPolicy.ExecuteAsync(async () =>
                    {
                        // Use Dapper to query the database
                        using (var connection = new SqlConnection("Data Source=.;Integrated Security=True;Encrypt=True;Trust Server Certificate=True"))
                        {
                            // Open connection
                            await connection.OpenAsync();

                            // Define the query
                            string query = @"
                            SELECT * 
                            FROM Users 
                            WHERE UserNumberFile = @PhoneNumber";

                            // Execute the query
                            var user = await connection.QueryFirstOrDefaultAsync<User>(query, new { PhoneNumber = phoneNumber });

                            // Check if user was found
                            if (user == null)
                            {
                                _logger.LogWarning("No user found for phone number: {PhoneNumber}", phoneNumber);
                                return null;
                            }

                            _logger.LogInformation("User found for phone number: {PhoneNumber}. Processing details.", phoneNumber);

                            // Additional processing (if needed)
                            ProcessUserDetails(user);

                            return user;
                        }
                    })
                )
            );
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogError(ex, "Circuit breaker is open; operation halted for phone number: {PhoneNumber}", phoneNumber);
            return null;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Operation was canceled while retrieving user details for phone number: {PhoneNumber}", phoneNumber);
            return null;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error while retrieving user details for phone number: {PhoneNumber}", phoneNumber);
            return null;
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "Timeout occurred while retrieving user details for phone number: {PhoneNumber}", phoneNumber);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while retrieving user details for phone number: {PhoneNumber}", phoneNumber);
            throw; // Re-throw for higher-level handling
        }
    }



    /// <summary>
    /// Retrieves the total call count for a given phone number.
    /// </summary>
    /// <param name="phoneNumber">The phone number to search for.</param>
    /// <returns>The total count of calls for the phone number.</returns>
    public async Task<int> GetCallCountByPhoneNumberAsync(string phoneNumber)
    {
        using var connection = new SqlConnection(_connectionString);

        const string query = @"
            SELECT COUNT(*) 
            FROM CallHistory
            WHERE CallerNumber = @PhoneNumber OR ReceiverNumber = @PhoneNumber";

        return await connection.ExecuteScalarAsync<int>(query, new { PhoneNumber = phoneNumber });
    }

    /// <summary>
    /// Retrieves the total message count for a given phone number.
    /// </summary>
    /// <param name="phoneNumber">The phone number to search for.</param>
    /// <returns>The total count of messages for the phone number.</returns>
    public async Task<int> GetMessageCountByPhoneNumberAsync(string phoneNumber)
    {
        using var connection = new SqlConnection(_connectionString);

        const string query = @"
            SELECT COUNT(*) 
            FROM MessageHistory
            WHERE SenderNumber = @PhoneNumber OR ReceiverNumber = @PhoneNumber";

        return await connection.ExecuteScalarAsync<int>(query, new { PhoneNumber = phoneNumber });
    }



private async Task<User?> QueryUserAsync(string phoneNumber)
    {
        _logger.LogInformation("Executing primary query for phone number: {PhoneNumber}", phoneNumber);
        var user = await _context.Users
            .Where(u => u.UserNumberFile == phoneNumber)
            .FirstOrDefaultAsync();

        if (user != null)
        {
            _logger.LogInformation("Primary query succeeded. UserId: {UserId}, PhoneNumber: {UserNumberFile}",
                user.UserId, user.UserNumberFile);
        }
        else
        {
            _logger.LogWarning("Primary query returned no results for phone number: {PhoneNumber}", phoneNumber);
        }

        return user;
    }
    private async Task<User?> FallbackQueryUserAsync(string phoneNumber)
    {
        return await _context.Users
            .Where(u => EF.Functions.Like(u.UserNumberFile, $"%{phoneNumber}%"))
            .FirstOrDefaultAsync();
    }



    /// Diagnoses query problems by attempting to retrieve a single user with a closely matching phone number.
    /// Logs details about the diagnosis.
    /// </summary>
    /// <param name="phoneNumber">The phone number to diagnose against.</param>
    /// <returns>A task representing the asynchronous operation, containing the closest matching user or null if none found.</returns>
    private async Task<User?> DiagnoseDatabaseIssuesAsync(string phoneNumber)
    {
        try
        {
            _logger.LogInformation("Starting diagnostic check for phone number: {PhoneNumber}", phoneNumber);

            // Example: Attempt to find a user with a loosely matching phone number
            var similarUser = await _context.Users
                .Where(u => EF.Functions.Like(u.UserNumberFile, $"%{phoneNumber}%"))
                .FirstOrDefaultAsync();

            if (similarUser != null)
            {
                _logger.LogInformation(
                    "Diagnostic result: Found a similar user. Id: {UserId}, Number: {UserNumberFile}",
                    similarUser.UserId,
                    similarUser.UserNumberFile);
            }
            else
            {
                _logger.LogWarning("Diagnostic result: No similar user found for phone number: {PhoneNumber}", phoneNumber);
            }

            return similarUser;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during database diagnostic for phone number: {PhoneNumber}", phoneNumber);
            return null;
        }
    }


    /// <summary>
    /// Processes additional logic for the retrieved user details.
    /// </summary>
    /// <param name="user">The user to process.</param>
    private void ProcessUserDetails(User user)
    {
        _logger.LogInformation("Processing user details for: {UserId}", user.UserId);

        // Example: Trigger notifications, update last access, etc.
        // Custom logic goes here
    }

    /// <summary>
    /// Normalizes the phone number to a consistent format for database queries.
    /// </summary>
    private string NormalizePhoneNumber(string phoneNumber)
    {
        // Example: Convert '98XXXXXXXXXX' to '09XXXXXXXXX'
        if (phoneNumber.StartsWith("98"))
        {
            phoneNumber = "0" + phoneNumber.Substring(2);
        }

        return phoneNumber;
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
                _logger.LogWarning(
                    $"Invalid Persian date format '{persianDate}'. Expected format: yyyy/MM/dd. Defaulting to {defaultDate}.");
                return defaultDate; // If format is wrong, return default date
            }

            // Attempt to parse year, month, and day
            var year = int.Parse(dateParts[0]);
            var month = int.Parse(dateParts[1]);
            var day = int.Parse(dateParts[2]);

            // Validate the month and day ranges based on Persian calendar
            if (month < 1 || month > 12 || day < 1 || day > 31 || (month > 6 && day > 30))
            {
                _logger.LogWarning(
                    $"Invalid Persian date '{persianDate}' (day or month out of range). Storing incorrect date but logging for review. Defaulting to {defaultDate}.");
                // Log the wrong date without discarding it, still return a fallback value
                // You could also return the original value here if you want to store the invalid input
                return defaultDate;
            }

            // Convert to DateTime using the Persian calendar
            var resultDate = persianCalendar.ToDateTime(year, month, day, 0, 0, 0, 0); // No time component
            return resultDate;
        }
        catch (Exception ex)
        {
            // Log the error and return default date
            _logger.LogError(
                $"Error parsing Persian date '{persianDate}': {ex.Message}. Returning default date {defaultDate}.");
            return defaultDate; // Default value when error occurs
        }
    }


    /// <summary>
    ///     Retrieves a list of call history records associated with a given phone number.
    /// </summary>
    /// <param name="phoneNumber">The phone number to filter call records by.</param>
    /// <param name="cancellationToken">Cancellation token to stop the operation if required.</param>
    /// <returns>A task representing the asynchronous operation, containing a list of CallHistory records.</returns>
    public async Task<List<CallHistory>> GetCallsByPhoneNumberAsync(string phoneNumber,
        CancellationToken cancellationToken)
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
                            .Where(ch =>
                                ch.SourcePhoneNumber == phoneNumber || ch.DestinationPhoneNumber == phoneNumber)
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
            _logger.LogError(ex, "Error occurred while fetching call history for phone number: {PhoneNumber}",
                phoneNumber);
        }

        return results;
    }

    // This method enqueues the job for background execution
    public void GetEnhancedUserStatisticsWithPartnersInBackground(string phoneNumber, int topCount = 1)
    {
        // Enqueue the job to execute GetEnhancedUserStatisticsWithPartnersAsync in the background
        _backgroundJobClient.Enqueue(() => GetEnhancedUserStatisticsWithPartnersAsync(phoneNumber, topCount));
    }

    public async Task<IEnumerable<UserCallSmsStatistics>> GetEnhancedUserStatisticsWithPartnersAsync(string phoneNumber ,int topCount = 1)
{
    try
    {

        string query = @"
WITH CallStatistics AS (
    SELECT
        u.UserNumberFile AS PhoneNumber,
        COALESCE(u.UserNameFile, 'Not Available') AS FirstName,
        COALESCE(u.UserFamilyFile, 'Not Available') AS LastName,
        COALESCE(u.UserFatherNameFile, 'Not Available') AS FatherName,
        COALESCE(u.UserBirthDayFile, 'Not Available') AS BirthDate,
        COALESCE(u.UserAddressFile, 'Not Available') AS Address,
        COALESCE(u.UserSourceFile, 'Not Available') AS UserSourceFile,
        COUNT(CASE WHEN c.CallType = N'تماس صوتی' THEN 1 END) AS TotalCalls,
        COUNT(CASE WHEN c.CallType = N'پیام کوتاه' THEN 1 END) AS TotalSMS,
        SUM(CASE WHEN c.CallType = N'تماس صوتی' THEN c.Duration ELSE 0 END) AS TotalCallDuration
    FROM dbo.Users u
    LEFT JOIN dbo.CallHistories c
        ON u.UserNumberFile IN (c.SourcePhoneNumber, c.DestinationPhoneNumber)
    GROUP BY
        u.UserNumberFile,
        u.UserNameFile,
        u.UserFamilyFile,
        u.UserFatherNameFile,
        u.UserBirthDayFile,
        u.UserAddressFile,
        u.UserSourceFile
),
FileAggregates AS (
    SELECT 
        c.FileName,
        u.UserNumberFile AS PhoneNumber,
        COUNT(DISTINCT c.CallID) AS TotalOccurrences
    FROM dbo.CallHistories c
    JOIN dbo.Users u
        ON u.UserNumberFile IN (c.SourcePhoneNumber, c.DestinationPhoneNumber)
    WHERE u.UserNumberFile = @PhoneNumber
    GROUP BY c.FileName, u.UserNumberFile
),
PartnerStatistics AS (
    SELECT
        CASE 
            WHEN c.SourcePhoneNumber = @PhoneNumber THEN c.DestinationPhoneNumber
            ELSE c.SourcePhoneNumber
        END AS PartnerPhoneNumber,
        SUM(CASE WHEN c.CallType = N'تماس صوتی' THEN c.Duration ELSE 0 END) AS TotalDuration,
        COUNT(*) AS TotalInteractions
    FROM dbo.CallHistories c
    WHERE c.SourcePhoneNumber = @PhoneNumber OR c.DestinationPhoneNumber = @PhoneNumber
    GROUP BY CASE 
                 WHEN c.SourcePhoneNumber = @PhoneNumber THEN c.DestinationPhoneNumber
                 ELSE c.SourcePhoneNumber
             END
),
RankedPartners AS (
    SELECT
        ps.PartnerPhoneNumber,
        ps.TotalDuration,
        ps.TotalInteractions,
        RANK() OVER (ORDER BY ps.TotalDuration DESC) AS PartnerRank
    FROM PartnerStatistics ps
),
PartnerRanges AS (
    SELECT 
        rp.PartnerRank, 
        rp.PartnerPhoneNumber, 
        rp.TotalDuration
    FROM RankedPartners rp
)
SELECT TOP (@TopCount)
    cs.PhoneNumber,
    cs.FirstName,
    cs.LastName,
    cs.FatherName,
    cs.BirthDate,
    cs.Address,
    cs.UserSourceFile,
    STRING_AGG(df.FileName + ' (' + CAST(df.TotalOccurrences AS VARCHAR) + ' times)', ', ') AS FileNames,
    cs.TotalCalls,
    cs.TotalSMS,
    cs.TotalCallDuration,
    (SELECT STRING_AGG(rp.PartnerPhoneNumber + ' (' + CAST(rp.TotalDuration AS VARCHAR) + 's)', ', ') 
     FROM PartnerRanges rp WHERE rp.PartnerRank BETWEEN 1 AND 5) AS FrequentPartners1,
    (SELECT STRING_AGG(rp.PartnerPhoneNumber + ' (' + CAST(rp.TotalDuration AS VARCHAR) + 's)', ', ') 
     FROM PartnerRanges rp WHERE rp.PartnerRank BETWEEN 6 AND 10) AS FrequentPartners2,
    (SELECT STRING_AGG(rp.PartnerPhoneNumber + ' (' + CAST(rp.TotalDuration AS VARCHAR) + 's)', ', ') 
     FROM PartnerRanges rp WHERE rp.PartnerRank BETWEEN 11 AND 15) AS FrequentPartners3,
    (SELECT STRING_AGG(rp.PartnerPhoneNumber + ' (' + CAST(rp.TotalDuration AS VARCHAR) + 's)', ', ') 
     FROM PartnerRanges rp WHERE rp.PartnerRank BETWEEN 16 AND 20) AS FrequentPartners4
FROM CallStatistics cs
LEFT JOIN FileAggregates df 
    ON df.PhoneNumber = cs.PhoneNumber
WHERE cs.PhoneNumber = @PhoneNumber
GROUP BY 
    cs.PhoneNumber, 
    cs.FirstName, 
    cs.LastName, 
    cs.FatherName, 
    cs.BirthDate, 
    cs.Address, 
    cs.UserSourceFile,
    cs.TotalCalls, 
    cs.TotalSMS, 
    cs.TotalCallDuration
ORDER BY cs.TotalCalls + cs.TotalSMS DESC;
";

        using (var connection =
               new SqlConnection("Data Source=.;Integrated Security=True;Encrypt=True;Trust Server Certificate=True"))
        {
            await connection.OpenAsync();

            var parameters = new { PhoneNumber = phoneNumber, TopCount = topCount };

            // Execute the query and get results using Dapper
            var result = await connection.QueryAsync(query, parameters);

            // Map the results to the UserCallSmsStatistics class
            var userStatistics = result.Select(row => new UserCallSmsStatistics
            {
                PhoneNumber = row.PhoneNumber ?? "Not Available", // Set default if null
                FirstName = string.IsNullOrWhiteSpace(row.FirstName) ? "Not Available" : row.FirstName,
                LastName = string.IsNullOrWhiteSpace(row.LastName) ? "Not Available" : row.LastName,
                FatherName = string.IsNullOrWhiteSpace(row.FatherName) ? "Not Available" : row.FatherName,
                BirthDate = string.IsNullOrWhiteSpace(row.BirthDate) ? "Not Available" : row.BirthDate,
                Address = string.IsNullOrWhiteSpace(row.Address) ? "Not Available" : row.Address,
                UserSourceFiles = string.IsNullOrWhiteSpace(row.UserSourceFile) ? "Not Available" : row.UserSourceFile,
                FileNames = string.IsNullOrWhiteSpace(row.FileNames) ? "Not Available" : row.FileNames,
                TotalCalls = row.TotalCalls ?? 0, // Set to 0 if null
                TotalSMS = row.TotalSMS ?? 0, // Set to 0 if null
                TotalCallDuration = row.TotalCallDuration ?? 0, // Set to 0 if null
                FrequentPartners1 = string.IsNullOrWhiteSpace(row.FrequentPartners1)
                    ? "Not Available"
                    : row.FrequentPartners1,
                FrequentPartners2 = string.IsNullOrWhiteSpace(row.FrequentPartners2)
                    ? "Not Available"
                    : row.FrequentPartners2,
                FrequentPartners3 = string.IsNullOrWhiteSpace(row.FrequentPartners3)
                    ? "Not Available"
                    : row.FrequentPartners3,
                FrequentPartners4 = string.IsNullOrWhiteSpace(row.FrequentPartners4)
                    ? "Not Available"
                    : row.FrequentPartners4
            }).ToList();
            // You can now use the userStatistics list, where null or empty values have been replaced with default


            // Apply default values to properties if they are null
            userStatistics.ForEach(u => u.SetDefaultsIfNeeded());

            return userStatistics;
        }
    }


    catch (SqlException ex)
    {
        _logger.LogError(ex, "SQL exception occurred while fetching user statistics.");
        throw new InvalidOperationException("A database error occurred while fetching user statistics.", ex);
    }
    catch (TimeoutException ex)
    {
        _logger.LogError(ex, "Query timed out while fetching user statistics.");
        throw new InvalidOperationException("The database query timed out. Please try again later.", ex);
    }
    catch (UnauthorizedAccessException ex)
    {
        _logger.LogError(ex, "Unauthorized access while fetching user statistics.");
        throw new InvalidOperationException("You do not have permission to access the database.", ex);
    }
    catch (InvalidOperationException ex)
    {
        _logger.LogError(ex, "Invalid operation occurred while fetching user statistics.");
        throw new InvalidOperationException("An invalid operation occurred while processing your request.", ex);
    }
    catch (ArgumentNullException ex)
    {
        _logger.LogError(ex, "A required argument was null while fetching user statistics.");
        throw new InvalidOperationException("A required parameter was missing or invalid.", ex);
    }
    catch (ArgumentException ex)
    {
        _logger.LogError(ex, "An invalid argument was provided while fetching user statistics.");
        throw new InvalidOperationException("One of the provided arguments was invalid.", ex);
    }
    catch (FormatException ex)
    {
        _logger.LogError(ex, "A format exception occurred while parsing user statistics.");
        throw new InvalidOperationException("There was an error in data formatting while fetching user statistics.",
            ex);
    }

    catch (InvalidCastException ex)
    {
        _logger.LogError(ex, "A casting error occurred while processing user statistics.");
        throw new InvalidOperationException("An error occurred while processing the data retrieved from the database.",
            ex);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "An unexpected error occurred while fetching user statistics.");
        throw new InvalidOperationException("An unexpected error occurred. Please try again later.", ex);
    }
}


    /// <summary>
    /// Retrieves the user details for the user with the most calls and their SMS count.
    /// </summary>
    /// <returns>A tuple containing user details, total call count, and total SMS count.</returns>
    public async Task<(User? User, int TotalCalls, int TotalSMS)> GetUserWithMostCallsAsync()
    {
        using var connection = new SqlConnection(_connectionString);

        // SQL to identify the user with the most calls
        const string mostCallsQuery = @"
            SELECT TOP 1 
                UserDetails.UserNumberFile, 
                UserDetails.UserNameFile, 
                UserDetails.UserFamilyFile, 
                UserDetails.UserFatherNameFile,
                UserDetails.UserBirthDayFile, 
                UserDetails.UserAddressFile, 
                UserDetails.UserTelegramID, 
                UserDetails.UserDescriptionFile,
                UserDetails.UserSourceFile, 
                UserDetails.UserId,
                COUNT(*) AS TotalCalls
            FROM CallHistory
            JOIN UserDetails ON 
                CallHistory.CallerNumber = UserDetails.UserNumberFile 
                OR CallHistory.ReceiverNumber = UserDetails.UserNumberFile
            GROUP BY 
                UserDetails.UserNumberFile, 
                UserDetails.UserNameFile, 
                UserDetails.UserFamilyFile, 
                UserDetails.UserFatherNameFile,
                UserDetails.UserBirthDayFile, 
                UserDetails.UserAddressFile, 
                UserDetails.UserTelegramID, 
                UserDetails.UserDescriptionFile,
                UserDetails.UserSourceFile, 
                UserDetails.UserId
            ORDER BY COUNT(*) DESC";

        // Fetch the user with the most calls
        var userWithMostCalls = await connection.QueryFirstOrDefaultAsync<(User User, int TotalCalls)>(
            mostCallsQuery);

        if (userWithMostCalls.User == null)
        {
            // No user found
            return (null, 0, 0);
        }

        // Fetch the SMS count for the identified user
        const string smsCountQuery = @"
            SELECT COUNT(*)
            FROM MessageHistory
            WHERE SenderNumber = @PhoneNumber OR ReceiverNumber = @PhoneNumber";

        var totalSMS = await connection.ExecuteScalarAsync<int>(
            smsCountQuery,
            new { PhoneNumber = userWithMostCalls.User.UserNumberFile });

        return (userWithMostCalls.User, userWithMostCalls.TotalCalls, totalSMS);
    }


/// <summary>
///     Retrieves long calls for a specific phone number that exceed a specified duration.
/// </summary>
public async Task<List<CallHistory>> GetLongCallsByPhoneNumberAsync(string phoneNumber,
        int minimumDurationInSeconds)
    {
        return await _context.CallHistories
            .Where(ch => ch.SourcePhoneNumber == phoneNumber && ch.Duration > minimumDurationInSeconds)
            .ToListAsync();
    }


    public Task<List<CallHistory>> GetAfterHoursCallsByPhoneNumberAsync(string phoneNumber, TimeSpan startBusinessTime,
        TimeSpan endBusinessTime)
    {
        throw new NotImplementedException();
    }


    /// <summary>
    ///     Retrieves frequent call dates and times for a specific phone number.
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
    ///     Retrieves the top N most recent calls for a phone number.
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
    ///     Checks if a phone number has been contacted within a specified time window.
    /// </summary>
    public async Task<bool> HasRecentCallWithinTimeSpanAsync(string phoneNumber, TimeSpan timeSpan)
    {
        var recentCallThreshold = DateTime.Now - timeSpan;
        return await _context.CallHistories
            .AnyAsync(ch => ch.SourcePhoneNumber == phoneNumber);
    }

    #endregion

    #region DeleteAllCallHistoriesAsync

    public async Task DeleteAllCallHistoriesAsync()
    {
        const int maxRetryAttempts = 5; // Maximum retry attempts.
        const int baseDelayBetweenRetries = 2000; // Base delay between retries in milliseconds.

        var retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                maxRetryAttempts,
                attempt => TimeSpan.FromMilliseconds(baseDelayBetweenRetries *
                                                     Math.Pow(2, attempt - 1)), // Exponential backoff.
                (exception, timespan, retryAttempt, context) =>
                {
                    Log.Warning(
                        $"Retry attempt {retryAttempt} due to error: {exception.Message}. Retrying in {timespan.TotalSeconds} seconds.");
                });

        // SQL commands for deleting data, checking if tables exist before deleting
        var deleteCallHistoriesSql = "IF OBJECT_ID('dbo.CallHistories', 'U') IS NOT NULL DELETE FROM CallHistories;";
        var deleteUserPermissionSql = "IF OBJECT_ID('dbo.UserPermission', 'U') IS NOT NULL DELETE FROM UserPermission;";
        var deleteUsersSql = "IF OBJECT_ID('dbo.Users', 'U') IS NOT NULL DELETE FROM Users;";

        // Begin transaction for deletion
        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            // Delete records from CallHistories table
            await retryPolicy.ExecuteAsync(async () =>
            {
                try
                {
                    await _context.Database.ExecuteSqlRawAsync(deleteCallHistoriesSql);
                    Log.Information("CallHistories table records deleted successfully.");
                }
                catch (Exception ex)
                {
                    Log.Error($"Error deleting records from CallHistories table: {ex.Message}");
                    throw;
                }
            });

            // Delete records from UserPermission table if it exists
            await retryPolicy.ExecuteAsync(async () =>
            {
                try
                {
                    await _context.Database.ExecuteSqlRawAsync(deleteUserPermissionSql);
                    Log.Information("UserPermission table records deleted successfully.");
                }
                catch (Exception ex)
                {
                    Log.Error($"Error deleting records from UserPermission table: {ex.Message}");
                    throw;
                }
            });

            // Delete records from Users table if it exists
            await retryPolicy.ExecuteAsync(async () =>
            {
                try
                {
                    await _context.Database.ExecuteSqlRawAsync(deleteUsersSql);
                    Log.Information("Users table records deleted successfully.");
                }
                catch (Exception ex)
                {
                    Log.Error($"Error deleting records from Users table: {ex.Message}");
                    throw;
                }
            });

            // Commit the transaction after successful deletion
            await transaction.CommitAsync();
            Log.Information("All records deleted and transaction committed.");
        }
        catch (Exception ex)
        {
            // Rollback the transaction if any exception occurs
            await transaction.RollbackAsync();
            Log.Error($"Transaction rolled back due to error: {ex.Message}");
            throw;
        }
    }


    #region DeleteCallHistoriesByFileNameAsync

    public async Task DeleteCallHistoriesByFileNameAsync(string fileName)
    {
        const int batchSize = 100000; // Size of each batch for deletion (adjust as needed)
        const int maxRetryAttempts = 5; // Max number of retries before giving up
        const int delayBetweenRetries = 1000; // Delay in milliseconds between retries (e.g., 2 seconds)

        // Validate input fileName
        if (string.IsNullOrEmpty(fileName))
            throw new ArgumentException("File name cannot be null or empty.", nameof(fileName));

        var totalRecordsToDelete = await _context.CallHistories
            .Where(ch => ch.FileName == fileName) // Filter based on file name
            .CountAsync();

        if (totalRecordsToDelete == 0)
            // No records to delete
            return;

        var deletedRecords = 0;
        var retryCount = 0;

        // Loop through the table and delete in batches
        while (deletedRecords < totalRecordsToDelete)
            try
            {
                // SQL command to delete records in batches
                var sqlCommand = @"
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
                    _logger.LogError(
                        $"Failed to delete records for file name '{fileName}' after {maxRetryAttempts} attempts: {ex.Message}");
                    throw; // Rethrow exception after maximum retries
                }

                // Log the error and retry
                _logger.LogWarning(
                    $"Error during batch deletion attempt {retryCount} for file name '{fileName}': {ex.Message}. Retrying in {delayBetweenRetries / 1000} seconds...");

                // Wait before retrying
                await Task.Delay(delayBetweenRetries);
            }

        _logger.LogInformation(
            $"Successfully deleted {deletedRecords} call history records for file name '{fileName}'.");
    }

    #endregion

    #region GetCallsByUserNamesAsync

    /// <summary>
    ///     Retrieves call history records for users based on their name and family name.
    ///     This includes approximate matching for Persian names, handles common character variants,
    ///     trims whitespace, and performs case-insensitive matching.
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
            var normalizedInputName = NormalizePersianCharacters(name?.Trim() ?? string.Empty);
            var normalizedInputFamily = NormalizePersianCharacters(family?.Trim() ?? string.Empty);

            #endregion

            #region Initialize Result List

            // Initialize the result list to hold the final call history records
            var allResults = new List<CallHistoryWithUserNames>();
            var batchSize = 2000; // Set a smaller batch size to reduce memory usage
            var skip = 0;
            List<CallHistoryWithUserNames> batchResults;

            #endregion

            #region Batch Processing Loop

            do
            {
                // Perform batch processing with pagination and minimal in-memory filtering
                batchResults = await (
                        from call in _context.CallHistories
                        join caller in _context.Users on call.SourcePhoneNumber equals caller.UserNumberFile into
                            callerInfo
                        from callerData in callerInfo.DefaultIfEmpty()
                        join receiver in _context.Users on call.DestinationPhoneNumber equals receiver.UserNumberFile
                            into receiverInfo
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
                            CallerName = callerData != null
                                ? $"{callerData.UserNameFile} {callerData.UserFamilyFile}".Trim()
                                : string.Empty,
                            ReceiverName = receiverData != null
                                ? $"{receiverData.UserNameFile} {receiverData.UserFamilyFile}".Trim()
                                : string.Empty
                        })
                    .Skip(skip)
                    .Take(batchSize)
                    .AsNoTracking()
                    .ToListAsync();

                // Add results to the main list and increment the skip count
                allResults.AddRange(batchResults);
                skip += batchSize;
            } while
                (batchResults.Count ==
                 batchSize); // Stop when batch size is less than requested, indicating end of data

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
    ///     Normalizes Persian characters in a string to handle common variants,
    ///     replacing Arabic and Persian similar characters with consistent Persian versions.
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

    private int CalculateBatchSize(int totalRows, int availableMemoryMB, int processorCount)
    {
        // Define system-specific constants
        const int ramThresholdMB = 8192; // 8GB in MB
        const int cpuThreshold = 4;      // 4 cores
        const int minBatchSize = 1000;   // Minimum fallback batch size
        const int maxBatchSize = 50000;  // Maximum allowed batch size

        // Ensure memory and CPU values are within expected bounds
        availableMemoryMB = Math.Min(availableMemoryMB, ramThresholdMB);
        processorCount = Math.Min(processorCount, cpuThreshold);

        // Calculate a baseline batch size based on total rows and resources
        int memoryBasedBatch = (availableMemoryMB / processorCount) * 500; // Memory contribution
        int cpuBasedBatch = (processorCount * 2000);                      // CPU contribution
        int rowBasedBatch = totalRows / 20;                               // Scale with dataset size

        // Determine the optimal batch size
        int optimalBatchSize = Math.Min(
            maxBatchSize, // Do not exceed the maximum limit
            Math.Max(minBatchSize, Math.Min(memoryBasedBatch, cpuBasedBatch)) // Choose between memory and CPU-based values
        );

        // Adjust batch size dynamically based on the total number of rows
        if (totalRows < optimalBatchSize)
        {
            optimalBatchSize = Math.Max(minBatchSize, totalRows / processorCount);
        }

        // Ensure the batch size is reasonable
        return optimalBatchSize;
    }

    public async IAsyncEnumerable<CallHistoryWithUserNames> GetCallsWithUserNamesStreamAsync(
      string phoneNumber,
      [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Constants for batch control
        const int initialBatchSize = 5000;
        const int maxBatchSize = 2000;
        const int minBatchSize = 1000;
        const long maxPartSizeInBytes = 50 * 1024 * 1024; // 50MB per batch

        int currentBatchSize = initialBatchSize;
        long currentBatchSizeInBytes = 0;

        var results = new List<CallHistoryWithUserNames>(); // Collect results here

        try
        {
            // Execute within retry and timeout policies
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
                                            : "Unknown",
                                        ReceiverName = receiverData != null
                                            ? $"{receiverData.UserNameFile} {receiverData.UserFamilyFile} {receiverData.UserAddressFile} {receiverData.UserFatherNameFile}"
                                            : "Unknown"
                                    };

                        var distinctQuery = query
                            .AsNoTrackingWithIdentityResolution() // Optimizes tracking for queries with joins
                            .Distinct()
                            .AsSplitQuery() // Executes each join as a separate query to reduce database complexity for large datasets
                            .AsAsyncEnumerable();

                        var currentBatch = new List<CallHistoryWithUserNames>();

                        // Process query results
                        await foreach (var record in distinctQuery.WithCancellation(cancellationToken))
                        {
                            currentBatch.Add(record);
                            currentBatchSizeInBytes += EstimateBatchSize(new List<CallHistoryWithUserNames> { record });

                            if (currentBatchSizeInBytes >= maxPartSizeInBytes || currentBatch.Count >= currentBatchSize)
                            {
                                results.AddRange(currentBatch);
                                currentBatch.Clear();
                                currentBatchSizeInBytes = 0;

                                currentBatchSize = Math.Min(Math.Max(minBatchSize, currentBatchSize * 2), maxBatchSize);
                                await Task.Yield();
                            }
                        }

                        if (currentBatch.Any())
                        {
                            results.AddRange(currentBatch);
                        }
                    })
                )
            );
        }
        catch (BrokenCircuitException)
        {
            Console.WriteLine("Operation stopped due to too many errors. Circuit breaker is open.");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Operation was canceled.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GetCallsWithUserNamesStreamAsync: {ex.Message}");
            throw; // Rethrow for higher-level handling
        }

        // Use a separate loop to yield results
        foreach (var result in results)
        {
            yield return result;
        }
    }



    private async Task WriteBatchToDatabaseAsync(List<CallHistoryWithUserNames> batch)
    {
        if (batch == null || !batch.Any()) return;

    }


    // This is an estimate of the data size (in bytes) for each batch
    private long EstimateBatchSize(List<CallHistoryWithUserNames> batch)
    {
        long totalSize = 0;

        foreach (var record in batch)
        {
            // Approximate the size of a record based on typical string lengths
            totalSize += record.CallId.ToString().Length; // CallId as string
            totalSize += record.SourcePhoneNumber.Length; // SourcePhoneNumber
            totalSize += record.DestinationPhoneNumber.Length; // DestinationPhoneNumber
            totalSize += record.CallDateTime.ToString().Length; // CallDateTime
            totalSize += record.Duration.ToString().Length; // Duration
            totalSize += record.CallType.Length; // CallType
            totalSize += record.FileName.Length; // FileName
            totalSize += record.CallerName.Length; // CallerNameCallHistoryWithUserNamesCallHistoryWithUserNames
            totalSize += record.ReceiverName.Length; // ReceiverName
        }

        return totalSize;
    }


    public async Task<int> GetCallHistoryCountAsync(CancellationToken cancellationToken)
    {
        try
        {
            // استفاده از ترکیب سیاست‌های Polly (Retry, Timeout, Circuit Breaker)
            return await _retryPolicy.ExecuteAsync(async () =>
                await _timeoutPolicy.ExecuteAsync(async () =>
                    await _circuitBreakerPolicy.ExecuteAsync(async () =>
                    {
                        // دریافت تعداد رکوردها از دیتابیس به صورت غیرهمزمان
                        return await _context.CallHistories.CountAsync(cancellationToken);
                    })
                )
            );
        }
        catch (TimeoutRejectedException ex)
        {
            // مدیریت خطا در صورتی که عملیات timeout شود
            throw new ApplicationException("The operation timed out while retrieving call history count.", ex);
        }
        catch (BrokenCircuitException ex)
        {
            // مدیریت خطا در صورتی که circuit breaker فعال شود
            throw new ApplicationException("Circuit breaker is open. The operation is temporarily unavailable.", ex);
        }
        catch (Exception ex)
        {
            // مدیریت سایر خطاها
            throw new ApplicationException("Unexpected error occurred while retrieving call history count.", ex);
        }
    }
    #endregion

    #endregion

}

public static class BatchSizeConfig
{
    // Total available RAM in MB (for example, 6GB RAM = 6144MB)
    public const int TotalRamInMB = 6144;  // This should be adjusted based on your system's RAM

    // Available CPU cores (in this case, 4 cores)
    public const int CpuCores = 4; // Number of CPU cores available for processing

    // Calculate the initial batch size based on available RAM, CPU cores, and row count
    public static int InitialBatchSize
    {
        get
        {
            // Estimate how many records fit in memory based on available RAM
            // For simplicity, let's assume each record takes about 1KB (adjust as needed based on your record structure)
            int recordSizeInKB = 1;  // Adjust based on actual record size (in KB)
            int recordsPerMB = 1024 / recordSizeInKB;  // How many records fit in 1MB

            // Maximum number of records that can be processed based on available RAM
            int maxRecordsByRam = (TotalRamInMB / 2) * recordsPerMB; // Using half the RAM for the batch

            // Adjust the batch size based on available CPU cores (in this case, splitting the task evenly across cores)
            int maxRecordsByCpu = 100_000 * CpuCores; // Max batch size in terms of cores

            // Choose the smaller of the two to optimize for both memory and CPU
            return Math.Min(maxRecordsByRam, maxRecordsByCpu);
        }
    }

    // The maximum batch size (in records) that should be processed for optimization
    public const int MaxBatchSize = 500_000;  // This is the upper limit for batch size

    // The minimum batch size (in records) to avoid processing too many small batches
    public const int MinBatchSize = 10_000;  // This is the lower limit for batch size

    // The maximum part size in bytes (50MB) to avoid large data transfers in a single batch
    public const int MaxPartSizeInBytes = 50 * 1024 * 1024;  // 50MB
}
