using CustomerMonitoringApp.Domain.Entities;
using CustomerMonitoringApp.Domain.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using OfficeOpenXml;

namespace CustomerMonitoringApp.Application.Services
{
    /// <summary>
    /// Service responsible for processing and importing CallHistory data from an Excel file.
    /// </summary>
    public class CallHistoryImportService
    {
        private readonly ICallHistoryRepository _callHistoryRepository;

        /// <summary>
        /// Initializes a new instance of the <see cref="CallHistoryImportService"/> class.
        /// </summary>
        /// <param name="callHistoryRepository">The repository to interact with the CallHistory data.</param>
        public CallHistoryImportService(ICallHistoryRepository callHistoryRepository)
        {
            _callHistoryRepository = callHistoryRepository ?? throw new ArgumentNullException(nameof(callHistoryRepository));
        }

        /// <summary>
        /// Processes the Excel file and imports the CallHistory records into the database.
        /// </summary>
        /// <param name="filePath">The file path of the Excel document.</param>
        /// <returns>Task representing the asynchronous operation.</returns>
        public async Task ProcessExcelFileAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
            }

            try
            {
                // Read the Excel file into a list of CallHistory records
                var records = new List<CallHistory>();

                // Open the Excel package
                using (var package = new ExcelPackage(new FileInfo(filePath)))
                {
                    var worksheet = package.Workbook.Worksheets[0]; // Assuming the first sheet is the data
                    var rowCount = worksheet.Dimension.Rows;

                    // Start from row 2 to skip headers (row 1)
                    for (int row = 2; row <= rowCount; row++)
                    {
                        // Extract the data from each row
                        var record = new CallHistory
                        {
                            SourcePhoneNumber = worksheet.Cells[row, 1].Text.Trim(), // Column A: شماره مبدا
                            DestinationPhoneNumber = worksheet.Cells[row, 2].Text.Trim(), // Column B: شماره مقصد
                            // Combine Date (Column C) and Time (Column D) into a single DateTime property
                            CallDateTime = DateTime.ParseExact(
                                worksheet.Cells[row, 3].Text.Trim() + " " + worksheet.Cells[row, 4].Text.Trim(), // Column C and D combined
                                "yyyy/MM/dd HH:mm", // Format: adjust this format if needed
                                null
                            ),
                            Duration = int.TryParse(worksheet.Cells[row, 5].Text.Trim(), out int duration) ? duration : 0, // Column E: مدت
                            CallType = worksheet.Cells[row, 6].Text.Trim() // Column F: نوع تماس
                        };
                        records.Add(record);
                    }
                }

                // Save the extracted records to the database
                if (records.Any())
                {
                    await _callHistoryRepository.AddCallHistoryAsync(records);
                    Console.WriteLine("File processed and data saved to database.");
                }
                else
                {
                    Console.WriteLine("No valid records found in the file.");
                }
            }
            catch (FileNotFoundException fnfEx)
            {
                Console.WriteLine($"File not found: {fnfEx.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing file: {ex.Message}");
            }
        }
    }
}
