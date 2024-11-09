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

namespace CustomerMonitoringApp.Application.Services
{
    // Ensure that the class is public so it can be used by other parts of the application.
    public class CallHistoryImportService : ICallHistoryImportService
    {
        private readonly ILogger<CallHistoryImportService> _logger;
        private readonly ICallHistoryRepository _callHistoryRepository;

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
        public async Task ProcessExcelFileAsync(string filePath, CancellationToken cancellationToken = default)
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
                    // Step 3: Convert to DataTable asynchronously
                    var dataTable = await ConvertToDataTable(records, cancellationToken); // Await the async method

                    // Step 4: Save the records with retry logic
                    await SaveRecordsWithRetryAsync(dataTable);
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

        // متد کمکی برای تبدیل List<CallHistory> به DataTable
        public async Task<DataTable> ConvertToDataTable(List<CallHistory> records, CancellationToken cancellationToken = default)
        {
            var dataTable = new DataTable("CallHistory");

            // Define columns with constraints
            dataTable.Columns.Add(new DataColumn("SourcePhoneNumber", typeof(string)) { MaxLength = 15 });
            dataTable.Columns.Add(new DataColumn("DestinationPhoneNumber", typeof(string)) { MaxLength = 15 });
            dataTable.Columns.Add(new DataColumn("CallDateTime", typeof(DateTime)));
            dataTable.Columns.Add(new DataColumn("Duration", typeof(int)) { DefaultValue = 0 });
            dataTable.Columns.Add(new DataColumn("CallType", typeof(string)) { MaxLength = 50 });





            // Set Primary Key if needed for uniqueness
            dataTable.PrimaryKey = new DataColumn[] { dataTable.Columns["SourcePhoneNumber"], dataTable.Columns["DestinationPhoneNumber"], dataTable.Columns["CallDateTime"] };

            // Block for processing rows in parallel with Dataflow
            var addRowBlock = new ActionBlock<CallHistory>(async record =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger?.LogInformation("Cancellation requested. Skipping further processing.");
                    return; // Early exit if cancellation is requested
                }

                try
                {
                    if (IsValidRecord(record))
                    {
                        var row = dataTable.NewRow();
                        row["SourcePhoneNumber"] = record.SourcePhoneNumber;
                        row["DestinationPhoneNumber"] = record.DestinationPhoneNumber;
                        row["CallDateTime"] = record.CallDateTime;
                        row["Duration"] = Math.Max(0, record.Duration);
                        row["CallType"] = record.CallType ?? "Unknown";

                        // Asynchronously add row to DataTable using a semaphore for thread safety
                        await Task.Run(() =>
                        {
                            lock (dataTable) // Ensure thread-safety when modifying DataTable
                            {
                                dataTable.Rows.Add(row);
                            }
                        }, cancellationToken);
                    }
                    else
                    {
                        _logger?.LogWarning($"Skipping invalid record: {record?.SourcePhoneNumber} -> {record?.DestinationPhoneNumber}.");
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"Error processing record ({record?.SourcePhoneNumber} -> {record?.DestinationPhoneNumber}): {ex.Message}");
                }
            },
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount, // Optimal parallelism
                CancellationToken = cancellationToken
            });

            try
            {
                // Add records asynchronously to the ActionBlock
                foreach (var record in records)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    await addRowBlock.SendAsync(record, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error converting records to DataTable asynchronously: {ex.Message}");
                throw;
            }
            finally
            {
                try
                {
                    // Mark the block as complete and wait for all tasks to finish
                    addRowBlock.Complete();
                    await addRowBlock.Completion;
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"Error completing ActionBlock processing: {ex.Message}");
                    throw;
                }
            }

            return dataTable;
        }

        // Helper method to validate each CallHistory record
        private bool IsValidRecord(CallHistory record)
        {
            // Check if the record is null
            if (record == null)
            {
                _logger?.LogWarning("Null CallHistory record found.");
                return false;
            }

            // Validate SourcePhoneNumber (non-empty, correct length, and correct format)
            if (string.IsNullOrWhiteSpace(record.SourcePhoneNumber))
            {
                _logger?.LogWarning($"SourcePhoneNumber is missing or empty for record {record?.CallDateTime}");
                return false;
            }

            if (record.SourcePhoneNumber.Length < 10 || record.SourcePhoneNumber.Length > 15) // Example length validation
            {
                _logger?.LogWarning($"SourcePhoneNumber '{record.SourcePhoneNumber}' is invalid length for record {record?.CallDateTime}");
                return false;
            }

            // Validate DestinationPhoneNumber (non-empty, correct length, and correct format)
            if (string.IsNullOrWhiteSpace(record.DestinationPhoneNumber))
            {
                _logger?.LogWarning($"DestinationPhoneNumber is missing or empty for record {record?.CallDateTime}");
                return false;
            }

            if (record.DestinationPhoneNumber.Length < 10 || record.DestinationPhoneNumber.Length > 15) // Example length validation
            {
                _logger?.LogWarning($"DestinationPhoneNumber '{record.DestinationPhoneNumber}' is invalid length for record {record?.CallDateTime}");
                return false;
            }

            // Additional checks can be added as needed (e.g., CallType validation, Duration validation)
            if (string.IsNullOrWhiteSpace(record.CallType))
            {
                _logger?.LogWarning($"CallType is missing for record {record?.SourcePhoneNumber} -> {record?.DestinationPhoneNumber}");
                record.CallType = "Unknown"; // Default value to prevent null
            }

            // If all checks passed, return true indicating the record is valid
            return true;
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
                var sourcePhone = worksheet.Cells[row, 1].Text.Trim();
                var destinationPhone = worksheet.Cells[row, 2].Text.Trim();
                var persianDate = worksheet.Cells[row, 3].Text.Trim();
                var callTime = worksheet.Cells[row, 4].Text.Trim();
                var durationText = worksheet.Cells[row, 5].Text.Trim();
                var callType = worksheet.Cells[row, 6].Text.Trim();

                _logger.LogInformation(
                    $"Row {row} - SourcePhone: {sourcePhone}, DestinationPhone: {destinationPhone}, CallDate: {persianDate}, CallTime: {callTime}, Duration: {durationText}, CallType: {callType}");

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
                if (!TryParsePersianDate(persianDate, out callDateTime))  // Removed callTime from the method call
                {
                    _logger.LogWarning($"Skipping row {row} due to invalid date/time format.");
                    return null;
                }

                // Combine callTime with callDateTime if needed, e.g., setting the time part of callDateTime
                // You may need additional logic here to handle time merging if `callTime` is in a specific format

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
                    CallDateTime = ConvertToPersianDate(callDateTime),
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


        public string ConvertToPersianDate(DateTime dateTime)
        {
            try
            {
                // Check if the date is valid for Persian conversion (based on typical Persian calendar ranges)
                if (dateTime < new DateTime(622, 3, 21) || dateTime > new DateTime(9999, 12, 31))
                {
                    return "Invalid Date Range"; // Handle out-of-range dates
                }

                // Attempt to convert to Persian date
                var persianDate = new PersianDateTime(dateTime);
                return persianDate.ToString("yyyy/MM/dd"); // Persian format
            }
            catch (Exception ex)
            {
                // Log the error if required
                _logger?.LogError($"Error converting to Persian date: {ex.Message}");

                // Return default value to avoid exceptions
                return "Conversion Error";
            }
        }

        // متد برای تبدیل تاریخ فارسی به میلادی
        // متد برای تبدیل تاریخ فارسی به میلادی با استفاده از مقدار پیش‌فرض در صورت نامعتبر بودن تاریخ
        private bool TryParsePersianDate(string persianDate, out DateTime result)
        {
            result = DateTime.Today; // مقدار پیش‌فرض برای تاریخ‌های نامعتبر
            try
            {
                // فرض بر این است که persianDate به فرمت yyyy/MM/dd است.
                string[] dateParts = persianDate.Split('/');
                if (dateParts.Length == 3)
                {
                    int year = int.Parse(dateParts[0]);
                    int month = int.Parse(dateParts[1]);
                    int day = int.Parse(dateParts[2]);

                    PersianCalendar persianCalendar = new PersianCalendar();

                    // کنترل برای روزهای نامعتبر (به‌طور مثال اگر روز 31 در ماه‌های خاصی وجود نداشته باشد)
                    if (month > 12 || day > 31 || (month > 6 && day > 30))
                    {
                        _logger.LogWarning($"Invalid day or month in Persian date '{persianDate}', using default date.");
                        return false; // نشان می‌دهد که تاریخ نامعتبر بوده است
                    }

                    result = persianCalendar.ToDateTime(year, month, day, 0, 0, 0, 0); // بدون ساعت
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error parsing Persian date: {ex.Message}");
            }

            _logger.LogWarning($"Using default date for Persian date '{persianDate}' due to parsing failure.");
            return false; // نشان می‌دهد که تاریخ نامعتبر بوده است
        }



        // Method to normalize the call type string to English or a preferred format
        private string NormalizeCallType(string callType)
        {
            if (string.IsNullOrWhiteSpace(callType))
            {
                return "Unknown"; // در صورت خالی یا نامعتبر بودن ورودی
            }

            // نرمال‌سازی برای حالت‌های مختلف
            callType = callType.ToLowerInvariant();

            // بررسی انواع مختلف پیامک
            if (callType.Contains("پیام") || callType.Contains("sms") || callType.Contains("message"))
            {
                return "SMS";
            }
            // بررسی انواع تماس‌های صوتی
            else if (callType.Contains("تماس") || callType.Contains("voice") || callType.Contains("call") || callType.Contains("صدا"))
            {
                return "Voice Call";
            }
            // بررسی نوع ارتباطات داده‌ای و اینترنتی
            else if (callType.Contains("اینترنت") || callType.Contains("data") || callType.Contains("gprs") ||
                     callType.Contains("internet") || callType.Contains("irancell") || callType.Contains("online"))
            {
                return "Data/Internet";
            }
            // دسته‌بندی انواع دیگر تماس‌ها (برای اپراتور همراه اول)
            else if (callType.Contains("mci") || callType.Contains("همراه اول") || callType.Contains("mtn"))
            {
                return "MCI";
            }
            // دسته‌بندی انواع دیگر تماس‌ها (برای اپراتور ایرانسل)
            else if (callType.Contains("ایرانسل") || callType.Contains("irancell") || callType.Contains("irc") ||
                     callType.Contains("mtc"))
            {
                return "Irancell";
            }
            // دسته‌بندی برای موارد تبلیغاتی و پیامک‌های تبلیغاتی
            else if (callType.Contains("تبلیغات") || callType.Contains("ad") || callType.Contains("promotion") ||
                     callType.Contains("advertisement"))
            {
                return "Advertisement";
            }
            // بررسی برای نوع تماس بین‌المللی یا رومینگ
            else if (callType.Contains("رومینگ") || callType.Contains("بین المللی") || callType.Contains("international") ||
                     callType.Contains("roaming"))
            {
                return "International/Roaming";
            }
            // اگر نوع تماس مشخص نباشد و در هیچ دسته دیگری قرار نگیرد
            else
            {
                return "Other";
            }
        }


        // Method to save records to the database with retry policy
        private async Task SaveRecordsWithRetryAsync(DataTable dataTable)
        {
            var retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    (exception, timeSpan, context) =>
                    {
                        _logger.LogError($"Error during data saving: {exception.Message}. Retrying in {timeSpan.TotalSeconds} seconds.");
                    });

            await retryPolicy.ExecuteAsync(async () =>
            {
                using (var connection = new SqlConnection("Data Source=.;Integrated Security=True;Encrypt=True;Trust Server Certificate=True"))
                {
                    await connection.OpenAsync();

                    // استفاده از تراکنش SQL Server
                    using (var transaction = connection.BeginTransaction())
                    using (var sqlBulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.TableLock, transaction))
                    {
                        sqlBulkCopy.DestinationTableName = "CallHistories";
                        sqlBulkCopy.BatchSize = 100000;
                        sqlBulkCopy.EnableStreaming = true;

                        foreach (DataColumn column in dataTable.Columns)
                        {
                            sqlBulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
                        }

                        try
                        {
                            await sqlBulkCopy.WriteToServerAsync(dataTable);
                            transaction.Commit(); // در صورت موفقیت آمیز بودن، تراکنش تایید می‌شود
                            _logger.LogInformation("Data successfully saved to the database.");
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback(); // در صورت بروز خطا، تراکنش بازگشت داده می‌شود
                            _logger.LogError($"Error during SqlBulkCopy operation: {ex.Message}. Rolling back transaction.");
                            throw;
                        }
                    }
                }
            });
        
    }
    }
}
