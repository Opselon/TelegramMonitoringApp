using CustomerMonitoringApp.Domain.Entities;
using CustomerMonitoringApp.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Polly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OfficeOpenXml;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace CustomerMonitoringApp.Application.Services
{
    /// <summary>
    /// Service responsible for processing and importing CallHistory data from an Excel file.
    /// </summary>
    public class CallHistoryImportService
    {
        private readonly ICallHistoryRepository _callHistoryRepository;
        private readonly ILogger<CallHistoryImportService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="CallHistoryImportService"/> class.
        /// </summary>
        /// <param name="callHistoryRepository">The repository to interact with the CallHistory data.</param>
        /// <param name="logger">The logger to log the process.</param>
        public CallHistoryImportService(ICallHistoryRepository callHistoryRepository, ILogger<CallHistoryImportService> logger)
        {
            _callHistoryRepository = callHistoryRepository ?? throw new ArgumentNullException(nameof(callHistoryRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Processes the Excel file and imports the CallHistory records into the database with retry and transaction support.
        /// </summary>
        /// <param name="filePath">The file path of the Excel document.</param>
        /// <returns>Task representing the asynchronous operation.</returns>
        public async Task ProcessExcelFileAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                _logger.LogError("File path cannot be null or empty.");
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
            }

            try
            {
                // Read the Excel file into a list of CallHistory records
                var records = new List<CallHistory>();

                // Open the Excel package
                using (var package = new ExcelPackage(new FileInfo(filePath)))
                {
                    // Ensure there are worksheets in the file
                    if (package.Workbook.Worksheets.Count == 0)
                    {
                        throw new InvalidOperationException("No worksheets found in the Excel file.");
                    }

                    var worksheet = package.Workbook.Worksheets[0]; // Assuming the first sheet is the data
                    var rowCount = worksheet.Dimension.Rows;

                    // Validate if data starts from row 2 (skipping headers)
                    if (rowCount < 2)
                    {
                        throw new InvalidOperationException("File contains too few rows.");
                    }

                    // Start from row 2 to skip headers (row 1)
                    for (int row = 2; row <= rowCount; row++)
                    {
                        // Validate row data
                        var sourcePhone = worksheet.Cells[row, 1].Text.Trim();
                        var destinationPhone = worksheet.Cells[row, 2].Text.Trim();
                        var callDate = worksheet.Cells[row, 3].Text.Trim();
                        var callTime = worksheet.Cells[row, 4].Text.Trim();
                        var durationText = worksheet.Cells[row, 5].Text.Trim();
                        var callType = worksheet.Cells[row, 6].Text.Trim();

                        if (string.IsNullOrWhiteSpace(sourcePhone) || string.IsNullOrWhiteSpace(destinationPhone) ||
                            string.IsNullOrWhiteSpace(callDate) || string.IsNullOrWhiteSpace(callTime) ||
                            string.IsNullOrWhiteSpace(durationText) || string.IsNullOrWhiteSpace(callType))
                        {
                            _logger.LogWarning($"Skipping row {row} due to missing data.");
                            continue; // Skip this row and move to the next one
                        }

                        // Parse and validate each field
                        if (!DateTime.TryParseExact(callDate + " " + callTime, "yyyy/MM/dd HH:mm", null, System.Globalization.DateTimeStyles.None, out DateTime callDateTime))
                        {
                            _logger.LogWarning($"Skipping row {row} due to invalid date/time format.");
                            continue; // Skip row with invalid date/time
                        }

                        if (!int.TryParse(durationText, out int duration))
                        {
                            _logger.LogWarning($"Skipping row {row} due to invalid duration.");
                            continue; // Skip row with invalid duration
                        }

                        var record = new CallHistory
                        {
                            SourcePhoneNumber = sourcePhone,
                            DestinationPhoneNumber = destinationPhone,
                            CallDateTime = callDateTime,
                            Duration = duration,
                            CallType = callType
                        };

                        records.Add(record);
                    }
                }

                if (records.Any())
                {
                    // Retry logic using Polly
                    var retryPolicy = Policy
                        .Handle<Exception>()
                        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (exception, timeSpan, context) =>
                        {
                            _logger.LogError($"Error during file processing: {exception.Message}. Retrying in {timeSpan.TotalSeconds} seconds.");
                        });

                    // Wrap the database operation in a transaction and ensure atomicity
                    await retryPolicy.ExecuteAsync(async () =>
                    {
                        using (var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
                        {
                            try
                            {
                                // Save the extracted records to the database
                                await _callHistoryRepository.AddCallHistoryAsync(records);

                                // Commit transaction
                                transaction.Complete();
                                _logger.LogInformation("File processed and data saved to database.");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError($"Error saving data to database: {ex.Message}. Rolling back transaction.");
                                // Ensure the transaction is rolled back automatically when an error occurs
                                throw; // Re-throw the exception to trigger the retry policy
                            }
                        }
                    });
                }
                else
                {
                    _logger.LogWarning("No valid records found in the file.");
                }
            }
            catch (FileNotFoundException fnfEx)
            {
                _logger.LogError($"File not found: {fnfEx.Message}");
                throw; // Rethrow for external handling if needed
            }
            catch (InvalidOperationException ioEx)
            {
                _logger.LogError($"Invalid operation: {ioEx.Message}");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing file: {ex.Message}");
                throw; // Rethrow for external handling if needed
            }
        }
    }
}
