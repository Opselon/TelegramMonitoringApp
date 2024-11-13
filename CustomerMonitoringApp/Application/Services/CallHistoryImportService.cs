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
            dataTable.Columns.Add(new DataColumn("CallDateTime", typeof(string)) { MaxLength = 80 });  // Fix the syntax error here
            dataTable.Columns.Add(new DataColumn("Duration", typeof(int)) { DefaultValue = 0 });
            dataTable.Columns.Add(new DataColumn("CallType", typeof(string)) { MaxLength = 50 });
            dataTable.Columns.Add(new DataColumn("FileName", typeof(string)) { MaxLength = 255 });

            // Add records to DataTable
            foreach (var record in records)
            {
                var row = dataTable.NewRow();

                // Truncate phone numbers if they exceed MaxLength
                row["SourcePhoneNumber"] = record.SourcePhoneNumber?.Length > 15
                    ? record.SourcePhoneNumber.Substring(0, 15)
                    : record.SourcePhoneNumber;
                row["DestinationPhoneNumber"] = record.DestinationPhoneNumber?.Length > 15
                    ? record.DestinationPhoneNumber.Substring(0, 15)
                    : record.DestinationPhoneNumber;

                // Directly use the CallDateTime from the record as it is in Persian format
                row["CallDateTime"] = !string.IsNullOrEmpty(record.CallDateTime)
                    ? record.CallDateTime // Directly assign the date if it is in Persian format
                    : "01/01/1970"; // Default value if CallDateTime is null or empty

                row["Duration"] = record.Duration;
                row["CallType"] = record.CallType ?? "Unknown";  // Default value if CallType is null
                row["FileName"] = fileName; // Add FileName to DataTable

                dataTable.Rows.Add(row);
            }

            return dataTable;
        }

        // Method to parse the Excel file
        /// <summary>
        /// Parses the given Excel file and extracts call history records from the first worksheet.
        /// </summary>
        /// <param name="filePath">The path of the Excel file to parse.</param>
        /// <returns>A list of CallHistory records parsed from the Excel file.</returns>
        private async Task<List<CallHistory>> ParseExcelFileAsync(string filePath)
        {
            #region Initialize Local Variables
            var records = new List<CallHistory>(); // List to hold parsed records
            #endregion

            #region Try-Catch for Error Handling
            try
            {
                #region Check File Existence
                // Check if the provided file exists
                if (!File.Exists(filePath))
                {
                    _logger.LogError($"The file '{filePath}' does not exist.");
                    return records;
                }
                #endregion




                #region Open and Process Excel File



                // Open the Excel file using EPPlus
                using (var package = new ExcelPackage(new FileInfo(filePath)))
                {


                    var worksheets = package.Workbook.Worksheets;

                    #region Check for Worksheets
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
                    #endregion

                    #region Check for Valid Worksheet Dimension
                    // Validate the worksheet dimensions (rows and columns)
                    if (worksheet.Dimension == null || worksheet.Dimension.Rows <= 1 ||
                        worksheet.Dimension.Columns == 0)
                    {
                        _logger.LogError("The worksheet is empty, has no rows, or invalid columns.");
                        return records;
                    }





                    var rowCount = worksheet.Dimension.Rows;
                    var colCount = worksheet.Dimension.Columns;
                    _logger.LogInformation($"The worksheet has {rowCount} rows and {colCount} columns.");
                    #endregion

                    #region Loop Through Rows and Parse Data
                    // Iterate through rows in the worksheet starting from row 2 (skipping header)
                    for (int row = 2; row <= rowCount; row++)
                    {
                        _logger.LogInformation($"Processing row {row} of {rowCount}.");

                        // Extract row values and trim any whitespace
                        var rowValues = worksheet.Cells[row, 1, row, colCount].Select(c => c.Text.Trim()).ToList();

                        // Skip empty rows
                        if (rowValues.All(string.IsNullOrEmpty))
                        {
                            _logger.LogInformation($"Skipping empty row {row}.");
                            continue;
                        }

                        // Parse individual row into a CallHistory record
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
                    #endregion
                }
                #endregion
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error parsing Excel file: {ex.Message}");
                throw; // Re-throw the exception after logging it
            }
            #endregion

            #region Return Parsed Records
            // Log and return the total number of records parsed
            _logger.LogInformation($"Total records parsed: {records.Count}");
            return records;
            #endregion
        }


        // Method to parse a single row from Excel
        private async Task<CallHistory> ParseRowAsync(ExcelWorksheet worksheet, int row)
        {
            try
            {
                // Retrieve and trim values from the Excel row
                var sourcePhone = worksheet.Cells[row, 1]?.Text?.Trim();
                var destinationPhone = worksheet.Cells[row, 2]?.Text?.Trim();
                var persianDate = worksheet.Cells[row, 3]?.Text?.Trim();
                var callTime = worksheet.Cells[row, 4]?.Text?.Trim();
                var durationText = worksheet.Cells[row, 5]?.Text?.Trim();
                var callType = worksheet.Cells[row, 6]?.Text?.Trim();

                // Skip the row if any mandatory field is missing or invalid
                if (string.IsNullOrEmpty(sourcePhone) || string.IsNullOrEmpty(destinationPhone) ||
                    string.IsNullOrEmpty(persianDate) || string.IsNullOrEmpty(callTime) ||
                    string.IsNullOrEmpty(durationText) || string.IsNullOrEmpty(callType))
                {
                    _logger.LogWarning($"Row {row}: Missing required fields.");
                    return null; // Skip row if any mandatory field is missing
                }


                // Validate and parse duration
                if (!int.TryParse(durationText, out int duration) || duration < 0)
                {
                    _logger.LogWarning($"Row {row}: Invalid call duration '{durationText}'.");
                    return null; // Skip row if duration is invalid
                }

                // Return the CallHistory record with the parsed data
                return new CallHistory
                {
                    SourcePhoneNumber = sourcePhone,
                    DestinationPhoneNumber = destinationPhone,
                    CallDateTime = persianDate + " | " + callTime, // Format for Persian date
                    Duration = duration,
                    CallType = callType ?? "Unknown"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error parsing row {row}: {ex.Message}");
                return null; // Return null if any error occurs during parsing
            }
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
