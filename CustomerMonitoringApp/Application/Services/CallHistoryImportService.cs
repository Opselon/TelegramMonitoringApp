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
            dataTable.Columns.Add(new DataColumn("FileName", typeof(string)) { MaxLength = 255 }); // Add FileName column

            // Add records to DataTable
            foreach (var record in records)
            {
                var row = dataTable.NewRow();

                // Ensure phone numbers do not exceed MaxLength
                row["SourcePhoneNumber"] = record.SourcePhoneNumber?.Length > 15 ? record.SourcePhoneNumber.Substring(0, 15) : record.SourcePhoneNumber;
                row["DestinationPhoneNumber"] = record.DestinationPhoneNumber?.Length > 15 ? record.DestinationPhoneNumber.Substring(0, 15) : record.DestinationPhoneNumber;

                row["CallDateTime"] = record.CallDateTime ?? "dd/MM/yyyy";
                row["Duration"] = record.Duration;
                row["CallType"] = record.CallType ?? "Unknown";  // Default value if CallType is null
                row["FileName"] = fileName; // Add FileName to DataTable

                dataTable.Rows.Add(row);
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

                    if (worksheet.Dimension == null || worksheet.Dimension.Rows <= 1 ||
                        worksheet.Dimension.Columns == 0)
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





        public string ConvertToPersianDate(DateTime dateTime)
        {
            try
            {
                if (dateTime < new DateTime(622, 3, 21) || dateTime > new DateTime(9999, 12, 31)) return "Invalid Date Range";

                var persianCalendar = new PersianCalendar();
                int year = persianCalendar.GetYear(dateTime);
                int month = persianCalendar.GetMonth(dateTime);
                int day = persianCalendar.GetDayOfMonth(dateTime);

                return $"{year}/{month:D2}/{day:D2}";
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error converting DateTime to Persian date: {ex.Message}");
                return "Conversion Error";
            }
        }

        // Method to convert Persian date (e.g., 1403/04/31) to Gregorian DateTime
        private bool TryParsePersianDate(string persianDate, out DateTime result)
        {
            result = DateTime.MinValue; // Default value for invalid dates
            try
            {
                // Assume the date is in yyyy/MM/dd format
                string[] dateParts = persianDate.Split('/');
                if (dateParts.Length == 3)
                {
                    int year = int.Parse(dateParts[0]);
                    int month = int.Parse(dateParts[1]);
                    int day = int.Parse(dateParts[2]);

                    PersianCalendar persianCalendar = new PersianCalendar();

                    // Check if the month is valid
                    if (month < 1 || month > 12)
                    {
                        _logger.LogWarning($"Invalid month {month} in Persian date '{persianDate}', using default date.");
                        return false; // Invalid month
                    }

                    // Validate the number of days in the month
                    int maxDaysInMonth = persianCalendar.GetDaysInMonth(year, month);
                    if (day < 1 || day > maxDaysInMonth)
                    {
                        _logger.LogWarning($"Invalid day {day} for month {month} in Persian date '{persianDate}', using default date.");
                        return false; // Invalid day
                    }

                    // Convert Persian date to Gregorian date
                    result = persianCalendar.ToDateTime(year, month, day, 0, 0, 0, 0); // Time is set to 00:00:00
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
                // Begin a transaction and log it
                transaction = await _callHistoryRepository.BeginTransactionAsync();
                _logger.LogInformation($"Transaction started for saving {dataTable.Rows.Count} records.");

                // Perform the bulk insert and confirm row count in the log
                await _callHistoryRepository.SaveBulkDataAsync(dataTable);
                _logger.LogInformation($"Attempted to save {dataTable.Rows.Count} records to the database.");

                // Commit the transaction
                await _callHistoryRepository.CommitTransactionAsync(transaction);
                _logger.LogInformation("Data successfully saved and transaction committed.");
            }
            catch (Exception ex)
            {
                // Rollback the transaction if an error occurs
                if (transaction != null)
                {
                    await _callHistoryRepository.RollbackTransactionAsync(transaction);
                }

                _logger.LogError($"Error during data saving: {ex.Message}");
                throw;
            }
            finally
            {
                // Dispose of the transaction
                transaction?.Dispose();
            }
        }


    }
}
