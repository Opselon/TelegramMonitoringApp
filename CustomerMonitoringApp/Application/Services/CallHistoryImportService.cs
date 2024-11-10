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
using Microsoft.Data.SqlClient;
using System.Data;
using System.Threading.Tasks.Dataflow;
using Microsoft.EntityFrameworkCore.Storage;
using FastMember;

namespace CustomerMonitoringApp.Application.Services
{
    // Ensure that the class is public so it can be used by other parts of the application.
    public class CallHistoryImportService : ICallHistoryImportService
    {
        private readonly ILogger<CallHistoryImportService> _logger;
        private readonly ICallHistoryRepository _callHistoryRepository;
        private static readonly PersianCalendar PersianCalendar = new PersianCalendar(); // Static instance to reuse across calls

        // Injecting the logger and repository through the constructor.
        public CallHistoryImportService(ILogger<CallHistoryImportService> logger,
            ICallHistoryRepository callHistoryRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _callHistoryRepository =
                callHistoryRepository ?? throw new ArgumentNullException(nameof(callHistoryRepository));
            _logger.LogInformation("CallHistoryImportService initialized.");
        }


        // Public method to process the Excel file.
        public async Task ProcessExcelFileAsync(string filePath, string fileName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                _logger.LogError("File path cannot be null or empty.");
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
            }

            try
            {
                // Step 1: Parse the Excel file asynchronously
                var records = await ParseExcelFileAsync(filePath);

                // Step 2: Check if we have any valid records
                if (records.Any())
                {
                    // Step 3: Convert to DataTable asynchronously with the file name
                    var dataTable = ConvertToDataTable(records, fileName);

                    // Step 4: Save the records with retry logic
                    await SaveRecordsAsync(dataTable);
                }
                else
                {
                    _logger.LogWarning("No valid records found in the file.");
                }
            }
            catch (FileNotFoundException fnfEx)
            {
                _logger.LogError($"File not found: {fnfEx.Message}");
                throw; // Rethrow the exception to allow further handling or logging
            }
            catch (InvalidOperationException ioEx)
            {
                _logger.LogError($"Invalid operation: {ioEx.Message}");
                throw; // Rethrow the exception to allow further handling or logging
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing file: {ex.Message}");
                throw; // Rethrow the exception to allow further handling or logging
            }
        }



        private DataTable ConvertToDataTable(List<CallHistory> records, string fileName)
        {
            var dataTable = new DataTable("CallHistory");

            // Define columns with constraints
            dataTable.Columns.Add(new DataColumn("SourcePhoneNumber", typeof(string)) { MaxLength = 15 });
            dataTable.Columns.Add(new DataColumn("DestinationPhoneNumber", typeof(string)) { MaxLength = 15 });
            dataTable.Columns.Add("CallDateTime", typeof(string));
            dataTable.Columns.Add(new DataColumn("Duration", typeof(int)) { DefaultValue = 0 });
            dataTable.Columns.Add(new DataColumn("CallType", typeof(string)) { MaxLength = 50 });
            dataTable.Columns.Add(new DataColumn("FileName", typeof(string)) { MaxLength = 255 });

            // Use FastMember ObjectReader for bulk conversion
            using (var reader = ObjectReader.Create(records, "SourcePhoneNumber", "DestinationPhoneNumber", "CallDateTime", "Duration", "CallType"))
            {
                // Bulk add the records in a single operation
                foreach (var record in reader)
                {
                    // Cast the 'record' object to the CallHistory type
                    var callHistory = record as CallHistory;

                    if (callHistory != null)
                    {
                        var row = dataTable.NewRow();
                        row["SourcePhoneNumber"] = callHistory.SourcePhoneNumber?.Length > 15 ? callHistory.SourcePhoneNumber.Substring(0, 15) : callHistory.SourcePhoneNumber;
                        row["DestinationPhoneNumber"] = callHistory.DestinationPhoneNumber?.Length > 15 ? callHistory.DestinationPhoneNumber.Substring(0, 15) : callHistory.DestinationPhoneNumber;
                        row["CallDateTime"] = callHistory.CallDateTime ?? "dd/MM/yyyy";
                        row["Duration"] = callHistory.Duration;
                        row["CallType"] = callHistory.CallType ?? "Unknown";
                        row["FileName"] = fileName;

                        // Add row to DataTable
                        dataTable.Rows.Add(row);
                    }
                }
            }

            return dataTable;
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
                    var worksheet = package.Workbook.Worksheets.FirstOrDefault();
                    if (worksheet == null || worksheet.Dimension == null || worksheet.Dimension.Rows <= 1)
                    {
                        _logger.LogError("Invalid or empty worksheet.");
                        return records;
                    }

                    var rowCount = worksheet.Dimension.Rows;
                    var colCount = worksheet.Dimension.Columns;
                    _logger.LogInformation($"Processing {rowCount} rows and {colCount} columns.");

                    // Process rows with parallelization or simple loop depending on the environment
                    for (int row = 2; row <= rowCount; row++)
                    {
                        var rowValues = worksheet.Cells[row, 1, row, colCount].Select(c => c.Text.Trim()).ToList();
                        if (rowValues.All(string.IsNullOrEmpty))
                        {
                            continue; // Skip empty rows
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
                var sourcePhone = worksheet.Cells[row, 1]?.Text;
                var destinationPhone = worksheet.Cells[row, 2]?.Text;
                var persianDate = worksheet.Cells[row, 3]?.Text;
                var callTime = worksheet.Cells[row, 4]?.Text;
                var durationText = worksheet.Cells[row, 5]?.Text;
                var callType = worksheet.Cells[row, 6]?.Text;

                if (string.IsNullOrWhiteSpace(sourcePhone) || string.IsNullOrWhiteSpace(destinationPhone) ||
                    string.IsNullOrWhiteSpace(persianDate) || string.IsNullOrWhiteSpace(callTime) ||
                    string.IsNullOrWhiteSpace(durationText) || string.IsNullOrWhiteSpace(callType))
                {
                    return null; // Skip row if any mandatory field is missing
                }

                if (!TryParsePersianDate(persianDate, out DateTime callDateTime) ||
                    !TryParseCallTime(callTime, out TimeSpan parsedTime))
                {
                    return null; // Skip row if date or time parsing fails
                }

                callDateTime = callDateTime.Add(parsedTime);

                if (!int.TryParse(durationText, out int duration) || duration < 0)
                {
                    return null; // Skip row if duration is invalid
                }

                return new CallHistory
                {
                    SourcePhoneNumber = sourcePhone.Trim(),
                    DestinationPhoneNumber = destinationPhone.Trim(),
                    CallDateTime = callDateTime.ToString() ?? "بدون تاریخ",
                    Duration = duration,
                    CallType = callType?.Trim() ?? "Unknown"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error parsing row {row}: {ex.Message}");
                return null;
            }
        }


        private bool TryParseCallTime(string callTime, out TimeSpan result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(callTime)) return false;

            return TimeSpan.TryParseExact(callTime.Trim(), new[] { "hh\\:mm\\:ss", "hh\\:mm" }, CultureInfo.InvariantCulture, out result);
        }



        // Method to convert Persian date (e.g., 1403/04/31) to Gregorian DateTime
        private bool TryParsePersianDate(string persianDate, out DateTime result)
        {
            result = DateTime.MinValue; // Default value for invalid dates
            try
            {
                // Assume the date is in yyyy/MM/dd format
                // Parse directly without allocating extra array
                if (persianDate.Length == 10 && persianDate[4] == '/' && persianDate[7] == '/')
                {
                    // Parse parts directly from the string
                    int year = int.Parse(persianDate.Substring(0, 4));
                    int month = int.Parse(persianDate.Substring(5, 2));
                    int day = int.Parse(persianDate.Substring(8, 2));

                    // Check if the month is valid
                    if (month < 1 || month > 12)
                    {
                        _logger.LogWarning($"Invalid month {month} in Persian date '{persianDate}', using default date.");
                        return false; // Invalid month
                    }

                    // Validate the number of days in the month
                    int maxDaysInMonth = PersianCalendar.GetDaysInMonth(year, month);
                    if (day < 1 || day > maxDaysInMonth)
                    {
                        _logger.LogWarning($"Invalid day {day} for month {month} in Persian date '{persianDate}', using default date.");
                        return false; // Invalid day
                    }

                    // Convert Persian date to Gregorian date
                    result = PersianCalendar.ToDateTime(year, month, day, 0, 0, 0, 0); // Time is set to 00:00:00
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error parsing Persian date '{persianDate}': {ex.Message}");
            }

            _logger.LogWarning($"Using default date for Persian date '{persianDate}' due to parsing failure.");
            return false; // Invalid date
        }



        public async Task SaveRecordsAsync(DataTable dataTable)
        {
            IDbContextTransaction transaction = null;

            try
            {
                // Begin a transaction
                transaction = await _callHistoryRepository.BeginTransactionAsync();

                // Perform the bulk insert or other database operations
                await _callHistoryRepository.SaveBulkDataAsync(dataTable);

                // Commit the transaction if everything is successful
                await _callHistoryRepository.CommitTransactionAsync(transaction);

                _logger.LogInformation("Data successfully saved to the database.");
            }
            catch (Exception ex)
            {
                // Rollback the transaction if an error occurs
                if (transaction != null)
                {
                    await _callHistoryRepository.RollbackTransactionAsync(transaction);
                }

                _logger.LogError($"Error during data saving: {ex.Message}");
                throw; // Rethrow the exception to let the caller handle it
            }
            finally
            {
                // Dispose of the transaction to release resources
                transaction?.Dispose();
            }
        }


    }
}
