using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using Polly;
using System.Transactions;
using CustomerMonitoringApp.Domain.Entities;
using CustomerMonitoringApp.Domain.Interfaces;
using System.Globalization;

namespace CustomerMonitoringApp.Application.Services
{
    // Ensure that the class is public so it can be used by other parts of the application.
    public class CallHistoryImportService : ICallHistoryImportService
    {
        private readonly ILogger<CallHistoryImportService> _logger;
        private readonly ICallHistoryRepository _callHistoryRepository;

        // Injecting the logger and repository through the constructor.
        public CallHistoryImportService(ILogger<CallHistoryImportService> logger, ICallHistoryRepository callHistoryRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _callHistoryRepository = callHistoryRepository ?? throw new ArgumentNullException(nameof(callHistoryRepository));
            _logger.LogInformation("CallHistoryImportService initialized.");
        }

        // Public method to process the Excel file.
        public async Task ProcessExcelFileAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                _logger.LogError("File path cannot be null or empty.");
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
            }

            try
            {
                var records = await ParseExcelFileAsync(filePath);

                if (records.Any())
                {
                    await SaveRecordsWithRetryAsync(records);
                }
                else
                {
                    _logger.LogWarning("No valid records found in the file.");
                }
            }
            catch (FileNotFoundException fnfEx)
            {
                _logger.LogError($"File not found: {fnfEx.Message}");
                throw;
            }
            catch (InvalidOperationException ioEx)
            {
                _logger.LogError($"Invalid operation: {ioEx.Message}");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing file: {ex.Message}");
                throw;
            }
        }

        // Method to parse the Excel file
        private async Task<List<CallHistory>> ParseExcelFileAsync(string filePath)
        {
            var records = new List<CallHistory>();

            try
            {
                if (!File.Exists(filePath))
                {
                    _logger.LogError($"The file '{filePath}' does not exist.");
                    return records;
                }

                using (var package = new ExcelPackage(new FileInfo(filePath)))
                {
                    var worksheets = package.Workbook.Worksheets;
                    if (worksheets.Count == 0)
                    {
                        _logger.LogError("The Excel file contains no worksheets.");
                        return records;
                    }

                    _logger.LogInformation($"The workbook contains {worksheets.Count} worksheet(s).");

                    var worksheet = worksheets.FirstOrDefault();
                    if (worksheet == null)
                    {
                        _logger.LogError("No valid worksheet found in the workbook.");
                        return records;
                    }

                    if (worksheet.Dimension == null || worksheet.Dimension.Rows <= 1 || worksheet.Dimension.Columns == 0)
                    {
                        _logger.LogError("The worksheet is empty, has no rows, or invalid columns.");
                        return records;
                    }

                    var rowCount = worksheet.Dimension.Rows;
                    var colCount = worksheet.Dimension.Columns;
                    _logger.LogInformation($"The worksheet has {rowCount} rows and {colCount} columns.");

                    // Add logging for rows processed
                    for (int row = 2; row <= rowCount; row++)
                    {
                        _logger.LogInformation($"Processing row {row} of {rowCount}.");
                        var rowValues = worksheet.Cells[row, 1, row, colCount].Select(c => c.Text.Trim()).ToList();
                        if (rowValues.All(string.IsNullOrEmpty))
                        {
                            _logger.LogInformation($"Skipping empty row {row}.");
                            continue;
                        }

                        var record = await ParseRowAsync(worksheet, row);
                        if (record != null)
                        {
                            records.Add(record);
                        }
                        else
                        {
                            _logger.LogWarning($"Row {row} could not be parsed.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error parsing Excel file: {ex.Message}");
                throw;
            }

            _logger.LogInformation($"Total records parsed: {records.Count}");
            return records;
        }

        // Method to parse a single row from Excel
        private async Task<CallHistory> ParseRowAsync(ExcelWorksheet worksheet, int row)
        {
            try
            {
                var sourcePhone = worksheet.Cells[row, 1].Text.Trim();
                var destinationPhone = worksheet.Cells[row, 2].Text.Trim();
                var persianDate = worksheet.Cells[row, 3].Text.Trim();
                var callTime = worksheet.Cells[row, 4].Text.Trim();
                var durationText = worksheet.Cells[row, 5].Text.Trim();
                var callType = worksheet.Cells[row, 6].Text.Trim();

                _logger.LogInformation($"Row {row} - SourcePhone: {sourcePhone}, DestinationPhone: {destinationPhone}, CallDate: {persianDate}, CallTime: {callTime}, Duration: {durationText}, CallType: {callType}");

                // Handle missing or invalid fields
                if (string.IsNullOrWhiteSpace(sourcePhone) || string.IsNullOrWhiteSpace(destinationPhone) ||
                    string.IsNullOrWhiteSpace(persianDate) || string.IsNullOrWhiteSpace(callTime) ||
                    string.IsNullOrWhiteSpace(durationText) || string.IsNullOrWhiteSpace(callType))
                {
                    _logger.LogWarning($"Skipping row {row} due to missing data.");
                    return null;
                }

                // Convert Persian date to Gregorian DateTime
                DateTime callDateTime;
                if (!TryParsePersianDate(persianDate, callTime, out callDateTime))
                {
                    _logger.LogWarning($"Skipping row {row} due to invalid date/time format.");
                    return null;
                }

                // Parse duration (allowing zero duration for SMS)
                int duration = 0;
                if (!int.TryParse(durationText, out duration) || duration < 0)
                {
                    _logger.LogWarning($"Skipping row {row} due to invalid duration.");
                    return null;
                }

                // Normalize call type to English or preferred format
                string normalizedCallType = NormalizeCallType(callType);

                var record = new CallHistory
                {
                    SourcePhoneNumber = sourcePhone,
                    DestinationPhoneNumber = destinationPhone,
                    CallDateTime = callDateTime,
                    Duration = duration,
                    CallType = normalizedCallType
                };

                return record;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error parsing row {row}: {ex.Message}");
                return null;
            }
        }
        // Method to try parsing Persian date format (convert to Gregorian)
        private bool TryParsePersianDate(string persianDate, string time, out DateTime result)
        {
            result = DateTime.MinValue;

            try
            {
                // Persian to Gregorian conversion (simplified)
                var persianDateParts = persianDate.Split('/');
                if (persianDateParts.Length == 3)
                {
                    int year = int.Parse(persianDateParts[0]);
                    int month = int.Parse(persianDateParts[1]);
                    int day = int.Parse(persianDateParts[2]);

                    // Create a Persian calendar and convert to Gregorian
                    PersianCalendar pc = new PersianCalendar();
                    result = pc.ToDateTime(year, month, day, 0, 0, 0, 0);

                    // Adjust the time part (HH:mm) to the date
                    var timeParts = time.Split(':');
                    if (timeParts.Length == 2)
                    {
                        result = new DateTime(result.Year, result.Month, result.Day, int.Parse(timeParts[0]), int.Parse(timeParts[1]), 0);
                        return true;
                    }
                }
            }
            catch
            {
                _logger.LogWarning($"Failed to parse Persian date {persianDate}.");
            }

            return false;
        }

        // Method to normalize the call type string to English or a preferred format
        private string NormalizeCallType(string callType)
        {
            switch (callType)
            {
                case "پیام کوتاه": return "SMS";
                case "تماس صوتی": return "Voice Call";
                default: return callType;
            }
        }


        // Method to save records to the database with retry policy
        private async Task SaveRecordsWithRetryAsync(List<CallHistory> records)
        {
            var retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    (exception, timeSpan, context) =>
                    {
                        _logger.LogError($"Error during file processing: {exception.Message}. Retrying in {timeSpan.TotalSeconds} seconds.");
                    });

            await retryPolicy.ExecuteAsync(async () =>
            {
                using (var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
                {
                    try
                    {
                        await _callHistoryRepository.AddCallHistoryAsync(records);
                        transaction.Complete();
                        _logger.LogInformation("File processed and data saved to database.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error saving data to database: {ex.Message}. Rolling back transaction.");
                        throw;
                    }
                }
            });
        }
    }
}
