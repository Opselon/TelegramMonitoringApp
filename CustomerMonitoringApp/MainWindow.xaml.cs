﻿using System.Collections.Concurrent;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;
using CustomerMonitoringApp.Application.Services;
using CustomerMonitoringApp.Domain.Entities;
using CustomerMonitoringApp.Domain.Interfaces;
using CustomerMonitoringApp.Domain.Views;
using CustomerMonitoringApp.Infrastructure.Data;
using CustomerMonitoringApp.Infrastructure.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using Polly;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Color = System.Windows.Media.Color;
using File = System.IO.File;
using Run = System.Windows.Documents.Run;
using User = CustomerMonitoringApp.Domain.Entities.User;
using OfficeOpenXml;
using System.ComponentModel.DataAnnotations;
namespace CustomerMonitoringApp.WPFApp;

public partial class MainWindow : Window
{
    public MainWindow() : this(
        App.Services.GetRequiredService<ILogger<MainWindow>>(),
        App.Services.GetRequiredService<ICallHistoryRepository>(),
        App.Services.GetRequiredService<IServiceProvider>(),
        App.Services.GetRequiredService<NotificationService>(),
        App.Services.GetRequiredService<ICallHistoryImportService>())
    {
        GetCommandInlineKeyboard();
        timestamp = GetPersianDate() + ".csv";
        LoadUsersFromDatabaseAsync();
        InitializeBotClient();
        InitializeComponent();
        InitializeButton();
    }


    private InlineKeyboardMarkup GetCommandInlineKeyboard()
    {
        return new InlineKeyboardMarkup(new[]
        {
            new InlineKeyboardButton[]
            {
                new()
                {
                    Text = "📞 /getcalls - Retrieve Complete Call History",
                    CallbackData = "/getcalls"
                }
            },
            new InlineKeyboardButton[]
            {
                new()
                {
                    Text = "📅 /getrecentcalls - Get a Quick Overview of Recent Calls",
                    CallbackData = "/getrecentcalls"
                }
            },
            new InlineKeyboardButton[]
            {
                new()
                {
                    Text = "⏳ /getlongcalls - Find Calls with Duration Exceeding a Set Time",
                    CallbackData = "/getlongcalls"
                }
            },
            new InlineKeyboardButton[]
            {
                new()
                {
                    Text = "🔝 /gettoprecentcalls - View Top Recent Calls",
                    CallbackData = "/gettoprecentcalls"
                }
            },
            new InlineKeyboardButton[]
            {
                new()
                {
                    Text = "🕒 /hasrecentcall - Check if a Number Has Recently Called",
                    CallbackData = "/hasrecentcall"
                }
            },
            new InlineKeyboardButton[]
            {
                new()
                {
                    Text = "🔄 /reset - Reset the Calls Database (Delete All Records)",
                    CallbackData = "/reset"
                }
            }
        });
    }

    private async Task HandleGetTopRecentCallsCommand(
        string[] commandParts,
        long chatId,
        ITelegramBotClient botClient,
        CancellationToken cancellationToken,
        UserState userState)
    {
        // Check if the user is already processing another request
        if (userState.IsBusy)
        {
            await botClient.SendMessage(chatId,
                "⚠️ *You are currently processing another request.*\nPlease wait until it is completed before trying again.",
                cancellationToken: cancellationToken);
            return;
        }

        // Mark user as busy while processing this request
        userState.IsBusy = true;

        // Ensure the user provided a phone number and the number of rows (calls)
        if (commandParts.Length < 3 || !int.TryParse(commandParts[2], out var topN))
        {
            await botClient.SendMessage(chatId,
                "❌ *Invalid input.*\nUsage: /gettoprecentcalls [phoneNumber] [numberOfCalls]",
                cancellationToken: cancellationToken);
            userState.IsBusy = false; // Reset user state if input is invalid
            return;
        }

        var phoneNumber = commandParts[1];

        try
        {
            // Send the initial progress message
            var progressMessage = await botClient.SendMessage(chatId,
                "⏳ *Processing your request...*\nPlease wait while we fetch your top recent calls.",
                cancellationToken: cancellationToken);

            // Variables to control message updates
            var progress = 0; // Start progress at 0%
            var totalProgressSteps = 10; // Number of steps to simulate progress
            var progressUpdateInterval = 3000; // 3 seconds interval
            var progressIncrement = 100 / totalProgressSteps; // Increment per step to reach 100%

            // Simulate progress updates every 3 seconds until 100% is reached
            for (var step = 0; step < totalProgressSteps; step++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                progress += progressIncrement;

                var message =
                    $"🔄 *Processing request...*\nProgress: {progress}% ⏳\nPlease hold on, we're fetching the data...";
                await botClient.EditMessageText(chatId, progressMessage.MessageId, message,
                    cancellationToken: cancellationToken);

                // Simulate some work being done
                await Task.Delay(progressUpdateInterval); // Delay 3 seconds for each progress update
            }

            // Now that we've simulated progress, start processing the actual data
            var topRecentCalls = await _callHistoryRepository.GetTopNRecentCallsAsync(phoneNumber, topN);

            // If no calls are found, send a message to the user
            if (!topRecentCalls.Any())
            {
                await botClient.SendMessage(chatId, $"📞 No recent calls found for *{phoneNumber}*.",
                    cancellationToken: cancellationToken);
                userState.IsBusy = false; // Reset user state after processing
                return;
            }

            // Generate a CSV file for the top recent calls
            var csvFilePath = GenerateCsv(topRecentCalls);

            // Send the CSV file to the user
            using var stream = new FileStream(csvFilePath, FileMode.Open, FileAccess.Read);
            await botClient.SendDocument(chatId,
                new InputFileStream(stream, $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_TopRecentCalls.csv"),
                cancellationToken: cancellationToken);

            // Clean up by deleting the file after sending
            File.Delete(csvFilePath);

            // Final message after completion (Progress reaches 100%)
            await botClient.EditMessageText(chatId, progressMessage.MessageId,
                "✅ *Your request has been successfully processed!* The file with your top recent calls has been sent. 📥",
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            // Log and notify the user if something goes wrong
            Log($"🚨 Error processing /gettoprecentcalls command: {ex.Message}");
            await botClient.SendMessage(chatId, "❌ *Error processing command* \n\n" +
                                                "Oops, something went wrong while trying to process your command. Please try again later. If the issue persists, contact support. 🛠️",
                cancellationToken: cancellationToken);
        }
        finally
        {
            // Ensure user is marked as not busy once the task completes
            userState.IsBusy = false;
        }
    }


    private async Task HandleDeleteAllCallsCommand(long chatId, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        try
        {
            // Delete all call history records
            await _callHistoryRepository.DeleteAllCallHistoriesAsync();

            // Confirm deletion
            await botClient.SendMessage(
                chatId,
                "🗑️ All call history records have been successfully deleted.",
                cancellationToken: cancellationToken
            );
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error deleting all call history records: {ex.Message}");
            await botClient.SendMessage(
                chatId,
                $"⚠️ Failed to delete call history records. Please try again later. {ex.Message}",
                cancellationToken: cancellationToken
            );
        }
    }

    private async Task HandleGetAfterHoursCallsCommand(
        string[] commandParts,
        long chatId,
        ITelegramBotClient botClient,
        CancellationToken cancellationToken,
        UserState userState)
    {
        // Check if the user is currently processing another request
        if (userState.IsBusy)
        {
            await botClient.SendMessage(chatId,
                "⚠️ You are currently processing a request. Please wait until it is completed.",
                cancellationToken: cancellationToken);
            return;
        }

        // Mark user as busy while processing this request
        userState.IsBusy = true;

        // Validate input: Ensure the user has provided a phone number, start time, and end time
        if (commandParts.Length < 4 ||
            !TimeSpan.TryParse(commandParts[2], out var startTime) ||
            !TimeSpan.TryParse(commandParts[3], out var endTime))
        {
            await botClient.SendMessage(chatId,
                "Usage: /getafterhourscalls [phoneNumber] [startTime] [endTime]. Example: /getafterhourscalls +1234567890 18:00 06:00",
                cancellationToken: cancellationToken);
            userState.IsBusy = false; // Reset user state in case of invalid input
            return;
        }

        var phoneNumber = commandParts[1];

        try
        {
            // Retrieve after-hours calls based on the phone number and the provided time window
            var afterHoursCalls =
                await _callHistoryRepository.GetAfterHoursCallsByPhoneNumberAsync(phoneNumber, startTime, endTime);

            // If no calls are found, notify the user
            if (!afterHoursCalls.Any())
            {
                await botClient.SendMessage(chatId,
                    $"❌ No after-hours calls found for {phoneNumber} between {startTime} and {endTime}.",
                    cancellationToken: cancellationToken);
                userState.IsBusy = false; // Reset user state after processing
                return;
            }

            // Convert the after-hours calls to CSV format
            var csvContent = ConvertToCsv(afterHoursCalls);

            // Send the CSV content as a file
            await SendCsvAsync(csvContent, chatId, botClient, cancellationToken);
        }
        catch (Exception ex)
        {
            // Log and notify the user if something goes wrong
            Log($"Error processing /getafterhourscalls command: {ex.Message}");
            await botClient.SendMessage(chatId, "❌ *Error processing command*\n\n" +
                                                "Oops, something went wrong while trying to process your command. Please try again later. If the issue persists, contact support.",
                cancellationToken: cancellationToken);
        }
        finally
        {
            // Ensure the user is marked as not busy once the task completes
            userState.IsBusy = false;
        }
    }


    private string BuildCallSummaryMessage(IEnumerable<CallHistoryWithUserNames> calls)
    {
        var summaryBuilder = new StringBuilder();
        summaryBuilder.AppendLine("📞 *Call History Summary*");

        foreach (var call in calls)
        {
            summaryBuilder.AppendLine("──────────────────────");
            summaryBuilder.AppendLine($"📅 *Date:* {call.CallDateTime}");
            summaryBuilder.AppendLine($"📲 *From:* {call.SourcePhoneNumber} ({call.CallerName ?? "Unknown"})");
            summaryBuilder.AppendLine($"📞 *To:* {call.DestinationPhoneNumber} ({call.ReceiverName ?? "Unknown"})");
            summaryBuilder.AppendLine($"⏳ *Duration:* {call.Duration} seconds");
            summaryBuilder.AppendLine($"📁 *File Name:* {call.FileName}");
            summaryBuilder.AppendLine($"📈 *Call Type:* {call.CallType}");
        }

        summaryBuilder.AppendLine("──────────────────────");
        summaryBuilder.AppendLine("_Summary end_");

        return summaryBuilder.ToString();
    }


    private async Task SendCsvAsync(string csvContent, long chatId, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        var csvBytes = Encoding.UTF8.GetBytes(csvContent);
        using (var stream = new MemoryStream(csvBytes))
        {
            var inputFile = new InputFileStream(stream, FileNameRegex.ToString());
            await botClient.SendDocument(
                chatId,
                inputFile,
                "Here is your requested data.",
                cancellationToken: cancellationToken
            );
        }
    }


    private async Task HandleGetFrequentCallDatesCommand(
        string[] commandParts,
        long chatId,
        ITelegramBotClient botClient,
        CancellationToken cancellationToken,
        UserState userState)
    {
        // Check if the user is currently processing another request
        if (userState.IsBusy)
        {
            await botClient.SendMessage(chatId,
                "⚠️ You are currently processing a request. Please wait until it is completed.",
                cancellationToken: cancellationToken);
            return;
        }

        // Mark user as busy while processing this request
        userState.IsBusy = true;

        // Ensure the user has provided a valid phone number
        if (commandParts.Length < 2)
        {
            await botClient.SendMessage(chatId,
                "Please provide a phone number. Example: /getfrequentcalldates +1234567890",
                cancellationToken: cancellationToken);
            userState.IsBusy = false; // Reset user state in case of invalid input
            return;
        }

        var phoneNumber = commandParts[1];

        try
        {
            // Retrieve frequent call dates for the phone number
            var frequentCallDates = await _callHistoryRepository.GetFrequentCallDatesByPhoneNumberAsync(phoneNumber);

            // If no data is found, notify the user
            if (!frequentCallDates.Any())
            {
                await botClient.SendMessage(chatId, $"❌ No frequent call dates found for {phoneNumber}.",
                    cancellationToken: cancellationToken);
                userState.IsBusy = false; // Reset user state after processing
                return;
            }

            // Convert the frequent call dates to CSV format
            var csvContent = ConvertToCsv(frequentCallDates);

            // Send the CSV content as a file
            await SendCsvAsync(csvContent, chatId, botClient, cancellationToken);
        }
        catch (Exception ex)
        {
            // Log and notify the user if something goes wrong
            Log($"Error processing /getfrequentcalldates command: {ex.Message}");
            await botClient.SendMessage(chatId, "❌ *Error processing command*\n\n" +
                                                "Oops, something went wrong while trying to process your command. Please try again later. If the issue persists, contact support.",
                cancellationToken: cancellationToken);
        }
        finally
        {
            // Ensure the user is marked as not busy once the task completes
            userState.IsBusy = false;
        }
    }

    private async Task HandleGetCallsCommand(
        string[] commandParts,
        long chatId,
        ITelegramBotClient botClient,
        CancellationToken cancellationToken,
        UserState userState)
    {
        // Check if the user is currently processing another request
        if (userState.IsBusy)
        {
            await botClient.SendMessage(chatId,
                "⚠️ You are currently processing a request. Please wait until it is completed.",
                cancellationToken: cancellationToken);
            return;
        }

        // Mark user as busy while processing this request
        userState.IsBusy = true;

        // Ensure the user provided a phone number
        if (commandParts.Length < 2)
        {
            await botClient.SendMessage(chatId, "Usage: /getcalls [phoneNumber]", cancellationToken: cancellationToken);
            userState.IsBusy = false; // Reset user state after invalid input
            return;
        }

        var phoneNumber = commandParts[1];

        try
        {
            // Retrieve call history for the provided phone number
            var callHistories = await _callHistoryRepository.GetCallsByPhoneNumberAsync(phoneNumber, cancellationToken);

            // If no call history found, notify the user
            if (!callHistories.Any())
            {
                await botClient.SendMessage(chatId, "No call history found for this phone number.",
                    cancellationToken: cancellationToken);
                userState.IsBusy = false; // Reset user state after processing
                return;
            }

            // Generate a CSV file for the call history
            var csvFilePath = GenerateCsv(callHistories);

            // Send the CSV file to the user
            using var stream = new FileStream(csvFilePath, FileMode.Open, FileAccess.Read);
            await botClient.SendDocument(chatId,
                new InputFileStream(stream, $"getcalls_{phoneNumber}_{DateTime.UtcNow:yyyyMMddHHmmss}.csv"),
                cancellationToken: cancellationToken);

            // Clean up by deleting the temporary file after sending
            File.Delete(csvFilePath);
        }
        catch (Exception ex)
        {
            // Log and notify the user if something goes wrong
            Log($"Error processing /getcalls command: {ex.Message}");
            await botClient.SendMessage(chatId, "❌ *Error processing command*\n\n" +
                                                "Oops, something went wrong while trying to process your command. Please try again later. If the issue persists, contact support.",
                cancellationToken: cancellationToken);
        }
        finally
        {
            // Ensure the user is marked as not busy once the task completes
            userState.IsBusy = false;
        }
    }


    private async Task HandleGetRecentCallsCommand(
        string[] commandParts,
        long chatId,
        ITelegramBotClient botClient,
        CancellationToken cancellationToken,
        UserState userState)
    {
        // Check if the user is currently processing another request
        if (userState.IsBusy)
        {
            await botClient.SendMessage(chatId,
                "⚠️ You are currently processing a request. Please wait until it is completed.",
                cancellationToken: cancellationToken);
            return;
        }

        // Mark the user as busy while processing this request
        userState.IsBusy = true;

        // Ensure the user provided both phone number and the days parameter
        if (commandParts.Length < 3 || !int.TryParse(commandParts[2], out var days))
        {
            await botClient.SendMessage(chatId, "Usage: /getrecentcalls [phoneNumber] [days]",
                cancellationToken: cancellationToken);
            userState.IsBusy = false; // Reset user state after invalid input
            return;
        }

        var phoneNumber = commandParts[1];

        // Ensure the 'days' value is positive
        if (days <= 0)
        {
            await botClient.SendMessage(chatId, "Please provide a positive number for days.",
                cancellationToken: cancellationToken);
            userState.IsBusy = false; // Reset user state
            return;
        }

        try
        {
            // Calculate the start date based on the given days and get the end date as today
            var startDate = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-dd");
            var endDate = DateTime.UtcNow.ToString("yyyy-MM-dd"); // Use current date as the end date

            // Retrieve recent calls for the provided phone number
            var recentCalls =
                await _callHistoryRepository.GetRecentCallsByPhoneNumberAsync(phoneNumber, startDate, endDate);

            // If no recent calls found, notify the user
            if (!recentCalls.Any())
            {
                await botClient.SendMessage(chatId,
                    "No recent calls found for this phone number within the specified time frame.",
                    cancellationToken: cancellationToken);
                userState.IsBusy = false; // Reset user state
                return;
            }

            // Generate CSV file with recent calls data
            var csvFilePath = GenerateCsv(recentCalls);

            // Send the CSV file to the user
            using var stream = new FileStream(csvFilePath, FileMode.Open, FileAccess.Read);
            await botClient.SendDocument(chatId,
                new InputFileStream(stream, $"getrecentcalls_{phoneNumber}_{DateTime.UtcNow:yyyyMMddHHmmss}.csv"),
                cancellationToken: cancellationToken);

            // Clean up by deleting the temporary CSV file after sending
            File.Delete(csvFilePath);
        }
        catch (Exception ex)
        {
            // Log error and notify user of failure
            Log($"Error processing /getrecentcalls command: {ex.Message}");
            await botClient.SendMessage(chatId, "❌ *Error processing command*\n\n" +
                                                "Oops, something went wrong while trying to process your command. Please try again later. If the issue persists, contact support.",
                cancellationToken: cancellationToken);
        }
        finally
        {
            // Ensure the user is marked as not busy once the task completes
            userState.IsBusy = false;
        }
    }


    private async Task HandleGetLongCallsCommand(
        string[] commandParts,
        long chatId,
        ITelegramBotClient botClient,
        CancellationToken cancellationToken,
        UserState userState)
    {
        // Check if the user is currently processing another request
        if (userState.IsBusy)
        {
            await botClient.SendMessage(chatId,
                "⚠️ You are currently processing a request. Please wait until it is completed.",
                cancellationToken: cancellationToken);
            return;
        }

        // Mark the user as busy while processing this request
        userState.IsBusy = true;

        // Ensure the user provided both phone number and minimum call duration
        if (commandParts.Length < 3 || !int.TryParse(commandParts[2], out var minimumDuration) || minimumDuration <= 0)
        {
            await botClient.SendMessage(chatId, "Usage: /getlongcalls [phoneNumber] [minimumDurationInSeconds]",
                cancellationToken: cancellationToken);
            userState.IsBusy = false; // Reset user state after invalid input
            return;
        }

        var phoneNumber = commandParts[1];

        try
        {
            // Retrieve the long calls for the provided phone number and minimum duration
            var longCalls = await _callHistoryRepository.GetLongCallsByPhoneNumberAsync(phoneNumber, minimumDuration);

            // If no long calls are found, notify the user
            if (!longCalls.Any())
            {
                await botClient.SendMessage(chatId,
                    "No long calls found for this phone number with the specified minimum duration.",
                    cancellationToken: cancellationToken);
                userState.IsBusy = false; // Reset user state
                return;
            }

            // Generate CSV file with the long calls data
            var csvFilePath = GenerateCsv(longCalls);

            // Use a dynamic timestamp for the file name
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");

            // Send the CSV file to the user
            using var stream = new FileStream(csvFilePath, FileMode.Open, FileAccess.Read);
            await botClient.SendDocument(chatId,
                new InputFileStream(stream, $"getlongcalls_{phoneNumber}_{timestamp}.csv"),
                cancellationToken: cancellationToken);

            // Clean up by deleting the temporary CSV file after sending
            File.Delete(csvFilePath);
        }
        catch (Exception ex)
        {
            // Log error and notify the user of failure
            Log($"Error processing /getlongcalls command: {ex.Message}");
            await botClient.SendMessage(chatId, "❌ *Error processing command*\n\n" +
                                                "Oops, something went wrong while trying to process your command. Please try again later. If the issue persists, contact support.",
                cancellationToken: cancellationToken);
        }
        finally
        {
            // Ensure the user is marked as not busy once the task completes
            userState.IsBusy = false;
        }
    }


    private string GenerateCsv(IEnumerable<CallHistory> callHistories)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"CallHistory_{Guid.NewGuid()}.csv");

        try
        {
            using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
            using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
                   {
                       HasHeaderRecord = true, // Include headers
                       Delimiter = ",", // Use comma as the delimiter
                       ShouldQuote = field => true // Quote all fields to handle special characters
                   }))
            {
                csv.WriteRecords(callHistories);
                writer.Flush();
            }

            _logger.LogInformation($"CSV file successfully created at: {filePath}");
            return filePath;
        }
        catch (IOException ex)
        {
            _logger.LogError($"File IO error while creating CSV at {filePath}: {ex.Message}");
            return null;
        }
        catch (CsvHelperException ex)
        {
            _logger.LogError($"CSV formatting error: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Unexpected error during CSV generation: {ex.Message}");
            return null;
        }
    }

    private static (string FileName, long FileSize, int RowCount) PreviousCallHistoryFileDetails;

    private async Task<bool> IsCallHistoryFileAsync(string filePath)
    {
        // Define expected headers for the CallHistory file
        var expectedHeaders = new List<string>
        {
            "شماره مبدا", // Source Phone
            "شماره مقصد", // Destination Phone
            "تاریخ", // Date
            "ساعت", // Time
            "مدت", // Duration
            "نوع تماس" // Call Type
        }
            .Select(header => CleanHeader(header)) // Clean the expected headers
            .ToList();

        // Set match threshold (70%)
        var matchThreshold = 0.7;
        var requiredMatches = (int)Math.Ceiling(expectedHeaders.Count * matchThreshold);

        try
        {
            var fileInfo = new FileInfo(filePath);
            var fileName = fileInfo.Name;
            var fileSize = fileInfo.Length;

            // Read the Excel file to get the row count (Tedad Satr)
            int rowCount = 0;
            using (var package = new ExcelPackage(fileInfo))
            {
                var worksheet = package.Workbook.Worksheets.FirstOrDefault();
                if (worksheet != null)
                {
                    rowCount = worksheet.Dimension?.Rows ?? 0;
                }
            }

            // Anti-duplicate check: Compare current file with the previous one
            if (PreviousCallHistoryFileDetails.FileName == fileName &&
                PreviousCallHistoryFileDetails.FileSize == fileSize &&
                PreviousCallHistoryFileDetails.RowCount == rowCount)
            {
                Log("Error: Duplicate Call History file detected. Rejecting this file.");
                return false; // Reject the file as it's a duplicate
            }

            // Update the previous file details
            PreviousCallHistoryFileDetails = (fileName, fileSize, rowCount);

            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                // Check if there are any worksheets in the file
                if (package.Workbook.Worksheets.Count == 0)
                {
                    Log("Error: No worksheets found in the Excel file.");
                    return false;
                }

                // Ensure we're not accessing an out-of-range index
                var worksheet = package.Workbook.Worksheets.FirstOrDefault();
                if (worksheet == null)
                {
                    Log("Error: No valid worksheet found.");
                    return false;
                }

                // Retrieve row and column counts
                var rowCountInFile = worksheet.Dimension?.Rows ?? 0;
                var columnCount = worksheet.Dimension?.Columns ?? 0;

                // Check if the file has enough columns (6 expected)
                if (columnCount < 6)
                {
                    Log("Error: The file has fewer than 6 columns.");
                    return false;
                }

                // Retrieve headers from the first row, clean them and compare
                var headers = new List<string>();
                for (var col = 1; col <= columnCount; col++)
                {
                    var header = worksheet.Cells[1, col].Text.Trim();
                    headers.Add(CleanHeader(header));
                }

                // Log found headers for debugging
                Log($"Found headers: {string.Join(", ", headers)}");

                // Check if the headers match the expected headers with the given threshold
                var matchedHeaderCount = headers.Intersect(expectedHeaders).Count();

                // If the matched headers are below the threshold, return false
                if (matchedHeaderCount < requiredMatches)
                {
                    Log(
                        $"Error: Insufficient matching headers. Expected at least {requiredMatches} matches, found {matchedHeaderCount}.");
                    return false;
                }

                // Check if there are enough rows of data (at least 2 rows: 1 header + 1 data row)
                if (rowCountInFile < 2)
                {
                    Log("Error: File contains too few rows (must be at least 2).");
                    return false;
                }

                // Counter for valid rows
                var validRowCount = 0;

                // Validate each data row
                for (var row = 2; row <= 5; row++) // Start from row 2 (skipping header row)
                {
                    var sourcePhone = worksheet.Cells[row, 1].Text.Trim(); // Column A: "شماره مبدا"
                    var destinationPhone = worksheet.Cells[row, 2].Text.Trim(); // Column B: "شماره مقصد"
                    var date = worksheet.Cells[row, 3].Text.Trim(); // Column C: "تاریخ"
                    var time = worksheet.Cells[row, 4].Text.Trim(); // Column D: "ساعت"
                    var durationText = worksheet.Cells[row, 5].Text.Trim(); // Column E: "مدت"
                    var callType = worksheet.Cells[row, 6].Text.Trim(); // Column F: "نوع تماس"

                    // Skip row if phone numbers are not numeric
                    if (!IsNumeric(sourcePhone) || !IsNumeric(destinationPhone))
                        continue; // Skip this row and move to the next one

                    // Validate if essential data is available and non-empty
                    if (string.IsNullOrWhiteSpace(sourcePhone) || string.IsNullOrWhiteSpace(destinationPhone) ||
                        string.IsNullOrWhiteSpace(date) || string.IsNullOrWhiteSpace(time) ||
                        string.IsNullOrWhiteSpace(durationText) || string.IsNullOrWhiteSpace(callType))
                    {
                        Log($"Error: Row {row} contains missing data.");
                        continue; // Skip this row and move to the next one
                    }

                    // Validate the format of Date (yyyy/MM/dd) and Duration (integer)
                    if (!DateTime.TryParseExact(date, "yyyy/MM/dd", null, DateTimeStyles.None, out _) ||
                        !int.TryParse(durationText, out _))
                    {
                        Log($"Error: Invalid data format in row {row}. Date: '{date}', Duration: '{durationText}'");
                        continue; // Skip this row and move to the next one
                    }

                    // Increment valid row counter
                    validRowCount++;

                    // If we have more than 2 valid rows, return true
                    if (validRowCount > 2) return true;
                }

                // If we don't have more than 2 valid rows, return false
                return false;
            }
        }
        catch (Exception ex)
        {
            Log($"Error checking file type: {ex.Message}");
            return false; // If an error occurs, assume the file is not a valid CallHistory file
        }
    }
    // Helper method to check if a string contains only numeric characters
    private bool IsNumeric(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            _logger.LogWarning("Input is null, empty, or whitespace.");
            return false;
        }

        try
        {
            // Return true if all characters in the string are digits
            return input.All(c => char.IsDigit(c));
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error checking if string is numeric: {ex.Message}");
            return false;
        }
    }


    private static (string FileName, long FileSize, int RowCount) PreviousFileDetails;

    private async Task<bool> IsUserFile(string filePath)
    {
        // Define expected headers for user data, trim spaces and remove any special characters like colons
        var expectedHeaders = new List<string> { "شماره تلفن", "نام", "نام خانوادگی", "نام پدر", "تاریخ تولد", "نشانی" }
            .Select(header => CleanHeader(header)).ToList();

        // Set match threshold (70%)
        var matchThreshold = 0.6;
        var requiredMatches = (int)Math.Ceiling(expectedHeaders.Count * matchThreshold);

        try
        {
            var fileInfo = new FileInfo(filePath);
            var fileName = fileInfo.Name;
            var fileSize = fileInfo.Length;

            // Read the Excel file to get the row count (Tedad Satr)
            int rowCount = 0;
            using (var package = new ExcelPackage(fileInfo))
            {
                var worksheet = package.Workbook.Worksheets.FirstOrDefault();
                if (worksheet != null)
                {
                    rowCount = worksheet.Dimension?.Rows ?? 0;
                }
            }

            // Anti-duplicate check: Compare current file with the previous one
            if (PreviousFileDetails.FileName == fileName &&
                PreviousFileDetails.FileSize == fileSize &&
                PreviousFileDetails.RowCount == rowCount)
            {
                Log("Error: Duplicate file detected. Rejecting this file.");
                return false; // Reject the file as it's a duplicate
            }

            // Update the previous file details
            PreviousFileDetails = (fileName, fileSize, rowCount);

            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                // Ensure there is at least one worksheet
                var worksheet = package.Workbook.Worksheets.FirstOrDefault();
                if (worksheet == null)
                {
                    Log("Error: No worksheets found in the file.");
                    return false;
                }

                // Retrieve headers from the first row, trimming spaces and removing special characters
                var headers = new List<string>();
                for (var col = 1; col <= worksheet.Dimension?.Columns; col++)
                {
                    var header = worksheet.Cells[1, col].Text.Trim();
                    if (!string.IsNullOrEmpty(header)) headers.Add(CleanHeader(header));
                }

                // Log found headers for debugging
                Log($"Found headers: {string.Join(", ", headers)}");

                if (!headers.Any())
                {
                    Log("Error: No headers found in the first row.");
                    return false;
                }

                // Count matching headers
                var matchedHeadersCount = headers.Intersect(expectedHeaders).Count();

                // Validate the match count against the threshold
                if (matchedHeadersCount >= requiredMatches)
                {
                    Log(
                        $"Success: Recognized as user file. Matched headers: {string.Join(", ", headers.Intersect(expectedHeaders))}");
                    return true; // Likely a user file
                }

                var missingHeaders = expectedHeaders.Except(headers).ToList();
                Log(
                    $"Error: Insufficient matching headers. Expected at least {requiredMatches} matches, found {matchedHeadersCount}. Missing headers: {string.Join(", ", missingHeaders)}.");
                return false; // Not enough matches
            }
        }
        catch (Exception ex)
        {
            Log($"Error checking file type: {ex.Message}");
            return false;
        }
    }

    // Helper function to clean the header text (removes spaces, colons, etc.)
    private string CleanHeader(string header)
    {
        return header
            .Replace(":", "") // Remove colons
            .Replace(" ", "") // Remove spaces
            .ToLower(); // Convert to lowercase
    }


    // Helper method to validate Phone Number
    /// <summary>
    ///     Validates that the provided phone number starts with "09" or "98", has the appropriate length,
    ///     and contains only numeric characters.
    /// </summary>
    /// <param name="phoneNumber">The phone number to validate.</param>
    /// <returns>True if the phone number is valid, false otherwise.</returns>
    private bool IsValidPhoneNumber(string phoneNumber)
    {
        // Check if phone number starts with "09" and has 11 digits, or starts with "98" and has 12 digits
        return ((phoneNumber.StartsWith("09") && phoneNumber.Length == 11) ||
                (phoneNumber.StartsWith("98") && phoneNumber.Length == 12))
               && phoneNumber.All(char.IsDigit);
    }


    // Async method to handle errors during polling
    private async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception,
        CancellationToken cancellationToken)
    {
        string errorMessage;
        ApiRequestException apiRequestException = null; // Use a different name for clarity

        // Determine the type of exception and generate a detailed error message
        switch (exception)
        {
            case ApiRequestException ex: // Use 'ex' as the variable name here
                apiRequestException = ex; // Store the ApiRequestException for later use
                errorMessage = $"Telegram API Error: {ex.Message} (Error Code: {ex.ErrorCode})";
                // Log additional context if available
                Log($"Request Parameters: {ex.Parameters}");
                break;

            case TaskCanceledException _:
                errorMessage = "Polling was canceled.";
                break;

            case OperationCanceledException _:
                errorMessage = "Operation was canceled.";
                break;

            case UnauthorizedAccessException _:
                errorMessage = "Unauthorized access. Please check your bot token and permissions.";
                break;

            case Exception ex when ex is TimeoutException:
                errorMessage = "The request timed out. Please try again later.";
                break;

            default:
                errorMessage = $"An unexpected error occurred: {exception.Message}";
                break;
        }

        // Log the detailed error message
        Log(errorMessage);

        // Optionally, implement retry logic for certain types of errors
        if (apiRequestException != null && apiRequestException.ErrorCode == 429) // Too Many Requests
        {
            // Implement exponential backoff strategy for rate limiting
            var retryDelay = 5000; // Initial delay in milliseconds
            var maxRetries = 5; // Maximum number of retries

            for (var attempt = 1; attempt <= maxRetries; attempt++)
            {
                Log($"Retry attempt {attempt} after {retryDelay / 1000} seconds.");
                await Task.Delay(retryDelay, cancellationToken); // Wait before retrying
                retryDelay *= 2; // Exponential backoff

                // Optionally, retry the failed operation if appropriate
                // bool success = await RetryFailedOperationAsync(botClient, cancellationToken);
                // if (success) break; // Exit the loop if the retry is successful
            }
        }

        // Additional error handling (e.g., notifying admins, logging to external services, etc.)
        // await NotifyAdminAsync(errorMessage); // Example of notifying an admin

        // Ensure all tasks are completed before returning
        await Task.CompletedTask;
    }


    /// <summary>
    ///     Handles any exceptions that occur during the bot startup process.
    /// </summary>
    /// <param name="ex">The exception that occurred.</param>
    private void HandleStartupException(Exception ex)
    {
        // Log detailed error information
        LogError(ex);

        // Log a more comprehensive message with error type and stack trace for debugging
        Log($"An error occurred while starting the bot: {ex.GetType().Name} - {ex.Message}\n{ex.StackTrace}");

        // Check if the exception is recoverable and handle accordingly
        if (IsRecoverableException(ex))
        {
            // Optionally perform any recovery actions here, e.g., alerting the user or resetting resources
            Log("Attempting to recover from the error...");
            AttemptRecovery();
        }
        else
        {
            Log("The error is not recoverable. Please check the logs for more details.");
        }

        // Reset the cancellation token source to null to allow for a new startup attempt
        _cancellationTokenSource = null;
    }

    /// <summary>
    ///     Determines if the exception is recoverable.
    /// </summary>
    /// <param name="ex">The exception to evaluate.</param>
    /// <returns>True if the exception is recoverable; otherwise, false.</returns>
    private bool IsRecoverableException(Exception ex)
    {
        // Define logic to determine if an exception is recoverable
        // For example, we could consider network-related exceptions as recoverable
        return ex is TimeoutException || ex is InvalidOperationException; // Customize as needed
    }

    /// <summary>
    ///     Attempts to recover from an error that occurred during startup.
    /// </summary>
    private void AttemptRecovery()
    {
        // Implement recovery logic here, such as re-initializing components or notifying the user
        Log("Recovery logic is executed, components are being re-initialized.");
        // E.g., reinitialize components, reset states, etc.
    }

    #region Bot Handlers

    public async Task<string> DownloadFileAsync(Document document, CancellationToken cancellationToken)
    {
        if (document == null)
        {
            Log("Document is null.");
            throw new ArgumentNullException(nameof(document), "Document is null.");
        }

        if (string.IsNullOrEmpty(document.FileId))
        {
            Log("Invalid FileId provided.");
            throw new ArgumentNullException(nameof(document.FileId), "FileId is null or empty.");
        }

        if (string.IsNullOrEmpty(document.FileName))
        {
            Log("FileName is null or empty.");
            throw new ArgumentNullException(nameof(document.FileName), "FileName is null or empty.");
        }

        var filePath = Path.Combine(document.FileName); // Now safe to use FileName

        // Check if file already exists, and if so, handle it (skip or rename)
        if (File.Exists(filePath))
        {
            Log($"File {document.FileName} already exists at {filePath}. Skipping download.");
            return filePath; // Or you can choose to rename the file by adding a suffix
        }

        try
        {
            using (var saveFileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                Log($"Starting download of file {document.FileName} with FileId {document.FileId}.");

                // Direct file download without retry or delay logic
                var file = await _botClient.GetFile(document.FileId, cancellationToken);
                await _botClient.DownloadFile(file.FilePath, saveFileStream, cancellationToken);

                Log($"File {document.FileName} downloaded successfully to {filePath}.");
                return filePath;
            }
        }
        catch (OperationCanceledException)
        {
            Log("File download operation was cancelled.");
            throw; // Rethrow the cancellation exception for upstream handling
        }
        catch (Exception ex)
        {
            Log($"An error occurred during the file download: {ex.Message}");
            throw; // Rethrow for upstream handling
        }
    }

    #endregion

    private void Window_Closed(object sender, EventArgs e)
    {
        // Dispose of resources when the window is closed
        _serverMachineConfigs?.Dispose();
        base.OnClosed(e);
    }


    #region Custom Type Converter

    /// <summary>
    ///     Custom type converter that adds an apostrophe to the beginning of string values to force Excel to treat them as
    ///     text.
    ///     This is important for values like large numbers, ZIP codes, or other numeric values that should not be treated as
    ///     numbers.
    /// </summary>
    public class ApostropheConverter : ITypeConverter
    {
        /// <summary>
        ///     Converts the value to a string with a prefixed apostrophe to enforce text treatment in Excel.
        ///     Useful for ensuring numeric-like data (e.g., IDs, codes) are treated as text when opened in Excel.
        /// </summary>
        /// <param name="value">The object value to convert.</param>
        /// <param name="row">The current row context.</param>
        /// <param name="memberMapData">Metadata for the member being mapped.</param>
        /// <returns>A string prefixed with an apostrophe, or an empty string if null.</returns>
        public string ConvertToString(object value, IWriterRow row, MemberMapData memberMapData)
        {
            // Handle null values
            if (value == null) return string.Empty;

            // Prefix with an apostrophe to ensure Excel treats as text (important for numeric values)
            return $"'{value}";
        }

        /// <summary>
        ///     Not implemented. Conversion from string is not required in this scenario.
        /// </summary>
        /// <param name="text">The text to convert.</param>
        /// <param name="row">The row context for the reader.</param>
        /// <param name="memberMapData">Metadata for the member being mapped.</param>
        /// <returns>Throws NotImplementedException.</returns>
        public object ConvertFromString(string text, IReaderRow row, MemberMapData memberMapData)
        {
            throw new NotImplementedException("Conversion from string is not implemented.");
        }
    }

    #endregion

    #region Fields and Properties

    private ServerMachineConfigs _serverMachineConfigs;

    private static readonly Regex FileNameRegex =
        new(@"^(Getcalls|Getrecentcalls|Getlongcalls)_\d{10,}_\d{8}_\d{6}\.csv$", RegexOptions.Compiled);

    private string timestamp;
    private readonly ILogger<MainWindow> _logger;
    private CancellationTokenSource _cancellationTokenSource;
    private ITelegramBotClient _botClient;
    private readonly NotificationService _notificationService;

    private readonly string
        _token = "7873662243:AAGr-x9Y0UM4jVdWia6UKpl7o6T-UJztOIc"; // Replace with your actual bot token

    private readonly ConcurrentDictionary<long, UserState> _userStates;
    private readonly ICallHistoryImportService _callHistoryImportService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ICallHistoryRepository _callHistoryRepository;
    private readonly List<CallHistoryWithUserNames> callHistoryData; //

    #endregion

    #region Constructor

    public MainWindow(
        ILogger<MainWindow> logger,
        ICallHistoryRepository callHistoryRepository,
        IServiceProvider serviceProvider,
        NotificationService notificationService,
        ICallHistoryImportService callHistoryImportService)
    {
        callHistoryData = new List<CallHistoryWithUserNames>();
        _lastMessageTimes = new ConcurrentDictionary<long, DateTime>();
        _messageTimestamps = new ConcurrentDictionary<long, Queue<DateTime>>();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _callHistoryRepository =
            callHistoryRepository ?? throw new ArgumentNullException(nameof(callHistoryRepository));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _callHistoryImportService = callHistoryImportService ??
                                    throw new ArgumentNullException(nameof(callHistoryImportService));
        _userStates = new ConcurrentDictionary<long, UserState>();
        GetCommandInlineKeyboard();
    }

    private string GetPersianDate()
    {
        var persianCalendar = new PersianCalendar();

        // تاریخ امروز به شمسی
        var year = persianCalendar.GetYear(DateTime.Now);
        var month = persianCalendar.GetMonth(DateTime.Now);
        var day = persianCalendar.GetDayOfMonth(DateTime.Now);
        var hour = DateTime.Now.Hour;
        var minute = DateTime.Now.Minute;
        var second = DateTime.Now.Second;

        // فرمت تاریخ شمسی: yyyyMMdd_HHmmss
        return $"{year:D4}{month:D2}{day:D2}_{hour:D2}{minute:D2}{second:D2}";
    }

    // User state class to track per-user progress
    private class UserState
    {
        public bool IsBusy { get; set; } // To prevent multiple simultaneous requests
    }

    /// <summary>
    ///     Initializes the bot client and verifies if the token is valid.
    /// </summary>
    private async Task InitializeBotClient()
    {
        Log("Initializing bot client...");

        // Validate the token before proceeding
        if (string.IsNullOrWhiteSpace(_token))
        {
            Log("Error: Bot token is missing. Please check your configuration.");
            return;
        }

        try
        {
            Log("Creating TelegramBotClient...");
            _botClient = new TelegramBotClient(_token);
            Log("Bot client successfully initialized.");

            // Verify the bot client by attempting to get the bot username
            Log("Attempting to retrieve bot information...");
            var botInfo = await _botClient.GetMe();

            if (botInfo != null)
            {
                Log($"Bot initialized with username: {botInfo.Username}");
            }
            else
            {
                Log("Error: Bot client could not retrieve bot information.");
                _botClient = null; // Explicitly set to null for safety
            }
        }
        catch (ApiRequestException apiEx)
        {
            LogError(apiEx);
            Log($"ApiRequestException: {apiEx.Message} - Code: {apiEx.ErrorCode}");
        }
        catch (HttpRequestException httpEx)
        {
            LogError(httpEx);
            Log($"HttpRequestException: {httpEx.Message}");
        }
        catch (Exception ex)
        {
            LogError(ex);
            Log($"General Exception: {ex.Message}");
        }
        finally
        {
            // Log a message if the bot client was not initialized successfully
            if (_botClient == null)
                Log("Bot client initialization failed, _botClient is null.");
            else
                Log("Bot client initialized successfully.");
        }
    }

    #endregion


    #region Bot Control Methods

    private void StartBotButton_Click(object sender, RoutedEventArgs e)
    {
        // Check if the bot client is initialized
        if (_botClient == null)
        {
            Log("Error: Bot client is not initialized. Please initialize it before starting.");
            return;
        }

        // Check if the bot is already running
        if (_cancellationTokenSource != null)
        {
            Log("Bot is already running. Please stop it before starting again.");
            return;
        }

        try
        {
            // Initialize the cancellation token source for this session
            _cancellationTokenSource = new CancellationTokenSource();

            // Start receiving updates asynchronously
            StartReceivingUpdates(); // Await the asynchronous method

            Log("Bot started successfully and is waiting for messages...");
        }
        catch (Exception ex)
        {
            HandleStartupException(ex);
        }
    }


    // Refactor StartReceivingUpdates to run in a task
    public void StartReceivingUpdates()
    {
        // Check if the bot client is initialized
        if (_botClient == null)
        {
            Log("Error: Telegram bot client is not initialized. Cannot start receiving updates.");
            return;
        }

        // Check if cancellation token source is initialized
        if (_cancellationTokenSource == null)
        {
            Log("Error: Cancellation token source is not initialized. Cannot start receiving updates.");
            return;
        }

        // Ensure the handlers are not null
        if (HandleUpdateAsync == null)
        {
            Log("Error: Update handler is not set. Cannot start receiving updates.");
            return;
        }

        if (HandleErrorAsync == null)
        {
            Log("Error: Error handler is not set. Cannot start receiving updates.");
            return;
        }

        try
        {
            Log("Starting to receive updates...");

            // Run StartReceiving in a task so it doesn't block the UI thread
            Task.Run(() => _botClient.StartReceiving(
                HandleUpdateAsync,
                HandleErrorAsync,
                new ReceiverOptions
                {
                    AllowedUpdates = new[] { UpdateType.Message } // Only receive message updates
                },
                _cancellationTokenSource.Token
            ));
            SetBotCommandsAsync(_botClient);
            Log("Bot is now receiving updates.");
        }
        catch (Exception ex)
        {
            Log($"An error occurred while starting to receive updates: {ex.Message}");
        }
    }


    private readonly ConcurrentDictionary<long, DateTime> _lastMessageTimes;
    private readonly ConcurrentDictionary<long, Queue<DateTime>> _messageTimestamps;

    private const int MinMessageIntervalInSeconds = 5; // Minimum time interval between two messages (in seconds)

    private const int
        MaxMessagesPerInterval =
            30; // Maximum number of messages allowed per user in a given interval (e.g., 5 messages per 60 seconds)

    private const int MessageWindowInSeconds = 60; // Time window to count the messages (e.g., 60 seconds)

    // Checks if a message has been processed by its MessageId
    /// <summary>
    ///     Tracks the timestamp of the last messages for each chatId, with a 5-minute timeout to avoid excessive message
    ///     queueing.
    ///     Maintains only recent timestamps within a specific time threshold, optimizing memory usage.
    /// </summary>
    /// <param name="chatId">Unique identifier for the chat.</param>
    private void TrackMessageTimestamp(long chatId)
    {
        // Define the message retention threshold and timeout duration
        var messageRetentionThreshold = TimeSpan.FromMinutes(1); // Time to keep recent messages
        var requestTimeout = TimeSpan.FromMinutes(5); // Max time allowed between requests

        var now = DateTime.UtcNow;

        // Initialize queue if it does not already exist for this chatId
        if (!_messageTimestamps.ContainsKey(chatId)) _messageTimestamps[chatId] = new Queue<DateTime>();

        var messageQueue = _messageTimestamps[chatId];

        // Check if the last message timestamp exceeds the request timeout
        if (messageQueue.Count > 0 && now - messageQueue.Last() > requestTimeout)
            // Reset the queue if the timeout has passed
            messageQueue.Clear();

        // Remove timestamps older than the retention threshold to keep only recent messages
        while (messageQueue.Count > 0 && now - messageQueue.Peek() > messageRetentionThreshold) messageQueue.Dequeue();

        // Enqueue the current timestamp to record this message
        messageQueue.Enqueue(now);

        // Optional: Implement a maximum queue size to limit memory usage
        var maxQueueSize = 100; // Define max queue size based on expected message frequency
        if (messageQueue.Count > maxQueueSize)
            messageQueue.Dequeue(); // Remove the oldest message if queue exceeds max size
    }

    private bool HasMessageBeenProcessed(long chatId, int messageId)
    {
        // You can implement logic here to track processed messages by chatId and messageId.
        return false; // Simplified for this example, assuming no message is processed more than once.
    }
    // Checks if the user is spamming based on the number of messages sent in the given time window

    private bool IsSpamming(long chatId)
    {
        var now = DateTime.UtcNow;
        if (!_messageTimestamps.ContainsKey(chatId)) _messageTimestamps[chatId] = new Queue<DateTime>();

        var messageQueue = _messageTimestamps[chatId];

        // Remove old timestamps that are outside the window
        while (messageQueue.Any() && (now - messageQueue.Peek()).TotalSeconds > MessageWindowInSeconds)
            messageQueue.Dequeue();

        // Check if the user exceeded the max messages allowed in the time window
        if (messageQueue.Count >= MaxMessagesPerInterval) return true; // User is spamming

        return false;
    }

    #region Command Handler

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update,
        CancellationToken cancellationToken)
    {
        var chatId = update.Message?.Chat?.Id;
        var messageId = update.Message?.MessageId;


        if (chatId == null || messageId == null)
            //   Log("Error: Invalid chatId or messageId.");
            return;

        // Anti-Spam: Check if the user is spamming by checking the message frequency
        if (IsSpamming(chatId.Value))
            //  Log($"Spam detected from ChatId {chatId}. Skipping message.");
            return; // Skip processing if the user is spamming

        // Add timestamp for the message to prevent spam
        TrackMessageTimestamp(chatId.Value);

        // Skip processing if this message has already been processed
        if (HasMessageBeenProcessed(chatId.Value, messageId.Value))
        {
            Log($"Message with ID {messageId} from ChatId {chatId} already processed. Skipping.");
            return;
        }


        // Initialize or fetch user state
        var userState = _userStates.GetOrAdd(chatId.Value, new UserState());

        // Handle Text Commands for Call History CSV Generation
        if (update.Type == UpdateType.Message && update.Message?.Type == MessageType.Text)
        {
            var messageText = update.Message.Text;
            var commandParts = messageText.Split(' ');
            var command = commandParts[0].ToLower();
            var taskList = new List<Task>();
            try
            {
                switch (command)
                {
                    case "/getcalls":
                        await HandleGetCallsCommand(commandParts, chatId.Value, botClient, cancellationToken,
                            userState);
                        break;
                    case "/getrecentcalls":
                        await HandleGetRecentCallsCommand(commandParts, chatId.Value, botClient, cancellationToken,
                            userState);
                        break;
                    case "/getlongcalls":
                        await HandleGetLongCallsCommand(commandParts, chatId.Value, botClient, cancellationToken,
                            userState);
                        break;
                    case "/getafterhourscalls":
                        await HandleGetAfterHoursCallsCommand(commandParts, chatId.Value, botClient, cancellationToken,
                            userState);
                        break;
                    case "/getfrequentcalldates":
                        await HandleGetFrequentCallDatesCommand(commandParts, chatId.Value, botClient,
                            cancellationToken, userState);
                        break;
                    case "/gettoprecentcalls":
                        await HandleGetTopRecentCallsCommand(commandParts, chatId.Value, botClient, cancellationToken,
                            userState);
                        break;
                    case "/hasrecentcall":
                        await HandleHasRecentCallCommand(commandParts, chatId.Value, botClient, cancellationToken,
                            userState);
                        break;
                    case "/getallcalls":
                        await HandleAllCallWithName(commandParts, chatId.Value, botClient, cancellationToken,
                            userState);
                        break;
                    case "/whois":
                        await HandleWhoIsWithName(commandParts, chatId.Value, botClient, cancellationToken, userState);
                        break;
                    case "/reset":
                        await HandleDeleteAllCallsCommand(chatId.Value, botClient, cancellationToken);
                        break;
                    // New case to delete call histories by file name
                    // New case to delete call histories by file name
                    case "/deletecallsbyfilename":
                        var fileName = commandParts[1]; // Get the file name from the command arguments

                        try
                        {
                            // Call the method to delete call histories by the provided file name
                            await HandleDeleteCallsByFileNameCommand(fileName, chatId.Value, botClient,
                                cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            // Handle any errors that occur during the deletion process
                            await botClient.SendMessage(chatId.Value,
                                $"Error deleting call histories for file '{fileName}': {ex.Message}",
                                cancellationToken: cancellationToken);
                        }

                        break;

                    default:
                        await botClient.SendMessage(
                            chatId,
                            "🤔 *Unknown Command*\n\n" +
                            "Oops! It looks like you've entered a command that I don’t recognize. But don’t worry—I’m here to help! You can try one of these supported commands:\n\n" +
                            "📞 /getcalls - _Retrieve the full call history for a phone number, including date, duration, and participants._\n\n" +
                            "📅 /getrecentcalls - _Get recent calls, showing times and participants for easy reference._\n\n" +
                            "⏳ /getlongcalls - _Find calls that exceeded a specific duration to identify extended conversations._\n\n" +
                            "🔝 /gettoprecentcalls - _Access the top N recent calls, giving you the latest records quickly._\n\n" +
                            "🕒 /hasrecentcall - _Check if a phone number had calls within a specific timeframe._\n\n" +
                            "📞 /getallcalls - _Retrieve a full call history with complete details._\n\n" +
                            "👤 /whois - _Find call history by providing a user's name and family name._\n\n" + // New /whois command
                            "🔄 /reset - _Clear the entire database of calls._\n\n" +
                            "🗑️ /deletebyfilename - _Delete call records by a specific file name._\n\n" +
                            "💬 Need more help? Type `/help` anytime for detailed instructions!\n\n" +
                            "I’m here to make your experience easier. Feel free to reach out anytime! 😊",
                            ParseMode.Markdown,
                            cancellationToken: cancellationToken
                        );
                        break;
                }

                // Execute all commands concurrently
                await Task.WhenAll(taskList);
            }

            catch (Exception ex)
            {
                Log($"Error handling command '{command}' for user {chatId}: {ex.Message}");
                await botClient.SendMessage(
                    chatId.Value,
                    "❌ *Error processing command*\n\n" +
                    "Oops, something went wrong while trying to process your command. Here's the error message I encountered:\n\n" +
                    $"⚠️ _{ex.Message}_\n\n" +
                    "Don't worry, though! I’m here to help you. You can try again, or if you're still having trouble, feel free to send a more detailed request, and I’ll assist you further.\n\n" +
                    "If this issue persists, please try using `/help` to review the available commands and their correct formats. I'm always available to guide you through any issues or questions you have. 😊",
                    replyParameters: messageId.Value, // This ensures your message is a reply to the user's message
                    cancellationToken: cancellationToken
                );
            }
        }

        try
        {
            // Validate the update type and file extension
            if (update.Type == UpdateType.Message && update.Message?.Type == MessageType.Document)
            {
                var document = update.Message.Document;

                if (document != null && Path.GetExtension(document.FileName)!
                        .Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    await botClient.SendMessage(
                        chatId.Value,
                        "📥 *I’ve received your .xlsx file!* \n\n" +
                        "I’m getting to work on processing it right now! 🚀 This may take a little time, depending on the file size and content, but don't worry—I’ll keep you updated.\n\n" +
                        "In the meantime, feel free to relax or ask me anything else! I’ll notify you as soon as the processing is done and you’re ready to proceed. ⏳💼\n\n" +
                        "Thank you for your patience! Your file is in good hands. 😊",
                        replyParameters: messageId.Value,
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken
                    );
                    ;
                    Log($"Received .xlsx file from user {chatId}: {document.FileName}");

                    // Step 1: Download the file
                    string filePath;
                    try
                    {
                        filePath = await DownloadFileAsync(document, cancellationToken);
                        //   Log($"File downloaded successfully: {filePath}");
                        await botClient.SendMessage(
                            chatId.Value,
                            "🧲 File downloaded successfully", replyParameters: messageId.Value,
                            cancellationToken: cancellationToken
                        );
                    }
                    catch (Exception downloadEx)
                    {
                        //     Log($"Error downloading file from user {chatId}: {downloadEx.Message}");
                        await botClient.SendMessage(
                            chatId.Value, replyParameters: messageId.Value,
                            text: "❌ Error downloading the file. Please try again.",
                            cancellationToken: cancellationToken
                        );
                        return;
                    }


                    var isUserFile = await IsUserFile(filePath);
                    // Step 3: Identify file type (CallHistory or UsersUpdate)
                    var isCallHistory = await IsCallHistoryFileAsync(filePath);

                    // 2. Add the parsed data to the repository (saving to database)

                    if (isCallHistory)
                    {
                        try
                        {
                            if (botClient == null) throw new InvalidOperationException("botClient is not initialized.");

                            if (!chatId.HasValue)
                                throw new ArgumentNullException(nameof(chatId), "chatId is not provided.");


                            // Try processing the CallHistory file
                            await _callHistoryImportService.ProcessExcelFileAsync(filePath, document.FileName,
                                cancellationToken);
                            Log($"Successfully processed CallHistory data: {filePath} for user ID:{chatId}");

                            await botClient.SendMessage(
                                chatId.Value, replyParameters: messageId.Value,
                                text: "✅ *Database operations complete, and users added successfully!* \n\n",
                                cancellationToken: cancellationToken,
                                parseMode: ParseMode.Markdown);
                        }


                        catch (ArgumentException argEx)
                        {
                            Log($"Invalid argument error: {argEx.Message}");
                            await botClient.SendMessage(
                                chatId.Value, replyParameters: messageId.Value,
                                text: $"❌ Invalid file or input. Error: {argEx.Message}",
                                cancellationToken: cancellationToken
                            );
                        }
                        catch (Exception ex)
                        {
                            Log($"General error processing CallHistory data: {ex.Message}");
                            await botClient.SendMessage(
                                chatId.Value, replyParameters: messageId.Value,
                                text: $"❌ Internal error during data import. Error: {ex.Message}",
                                cancellationToken: cancellationToken
                            );
                        }
                    }

                    else if (isUserFile)
                    {
                        // Process Users Update data
                        try
                        {
                            await ImportExcelToDatabase(filePath, botClient, chatId.Value, cancellationToken);
                            Log($"File processed and users data saved: {filePath}");
                        }
                        catch (Exception ex)
                        {
                            Log($"Error processing Users data for user {chatId}: {ex.Message}");
                            await botClient.SendMessage(
                                chatId.Value, replyParameters: messageId.Value,
                                text: "❌ Failed to import Users data. Please ensure the file format is correct.",
                                cancellationToken: cancellationToken
                            );
                            return;
                        }
                    }
                    else
                    {
                        // Handle unsupported file types
                        await botClient.SendMessage(
                            chatId.Value, replyParameters: messageId.Value,
                            text: "⚠️ The file type is not supported. Please upload a valid .xlsx file.",
                            cancellationToken: cancellationToken
                        );
                        Log($"User {chatId} uploaded an unsupported file type: {document?.FileName}");
                        return;
                    }

                    // Step 4: Inform the user of success and provide summary
                    await botClient.SendMessage(
                        chatId.Value, replyParameters: messageId.Value,
                        text: "✅ File processed and data imported successfully!",
                        cancellationToken: cancellationToken
                    );
                    Log($"User {chatId} was informed of successful processing.");
                }
                else
                {
                    // Handle incorrect file types with detailed feedback
                    await botClient.SendMessage(
                        chatId.Value, replyParameters: messageId.Value,
                        text: "⚠️ Please upload a valid .xlsx file.",
                        cancellationToken: cancellationToken
                    );
                    Log($"User {chatId} attempted to upload an invalid file type: {document?.FileName}");
                }
            }
            else
            {
                Log("Received unsupported update type.");
            }
        }

        catch (Exception ex)
        {
            Log($"Unhandled error in HandleUpdateAsync for user {chatId}: {ex.Message}");
            if (chatId != null)
                await botClient.SendMessage(
                    chatId.Value, replyParameters: messageId.Value,
                    text: "❌ An unexpected error occurred while processing your file. Please try again later.",
                    cancellationToken: cancellationToken
                );
        }

        finally
        {
            Log($"Completed file processing for user {chatId}");
        }

        // Step 4: Handle CallbackQuery if applicable
        if (update.Type == UpdateType.CallbackQuery)
            await HandleCallbackQueryAsync(botClient, update.CallbackQuery, cancellationToken);
    }

    #region HandleWhoIsWithName

    /// <summary>
    ///     Handles the /whois command by retrieving and sending the user's call history as a CSV file.
    ///     This method supports UTF-8 encoding to handle special characters in Persian names.
    /// </summary>
    private async Task HandleWhoIsWithName(
        string[] commandParts,
        long chatId,
        ITelegramBotClient botClient,
        CancellationToken cancellationToken,
        UserState userState)
    {
        // Check if the user is already busy with another request
        if (userState.IsBusy)
        {
            await botClient.SendMessage(
                chatId,
                "⚠️ You are currently processing a request. Please wait until it is completed.",
                cancellationToken: cancellationToken
            );
            return;
        }

        // Mark user as busy while processing this request
        userState.IsBusy = true;

        // Check if the user provided both name and family name
        if (commandParts.Length < 3)
        {
            await botClient.SendMessage(
                chatId,
                "Usage: /whois [Name] [Family]",
                cancellationToken: cancellationToken
            );
            userState.IsBusy = false;
            return;
        }

        var name = commandParts[1];
        var family = commandParts[2];

        try
        {
            // Fetch call history for the specified user name and family name
            var calls = await _callHistoryRepository.GetCallsByUserNamesAsync(name, family);

            // If no calls are found, inform the user
            if (calls == null || !calls.Any())
                await botClient.SendMessage(
                    chatId,
                    $"❌ No calls found for user with name '{name}' and family '{family}'.",
                    cancellationToken: cancellationToken
                );

            // Send confirmation message after sending the file
            await botClient.SendMessage(
                chatId,
                "📁 Your call history has been sent in a CSV file.",
                cancellationToken: cancellationToken
            );


            await botClient.SendMessage(chatId, "📁 Your call history has been sent in a CSV file.",
                cancellationToken: cancellationToken);
        }
        catch (IOException ioEx)
        {
            // Specific handling for file I/O issues
            Log($"File operation error in HandleWhoIsWithName: {ioEx.Message}");
            await botClient.SendMessage(
                chatId,
                "❌ *File processing error*\n\nUnable to generate your CSV file. Please try again later.",
                ParseMode.Markdown,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception ex)
        {
            // Log the error and notify the user
            Log($"Error processing the /whois command: {ex.Message}");
            await botClient.SendMessage(
                chatId,
                "❌ *Error processing command*\n\nOops, something went wrong. Please try again later.",
                ParseMode.Markdown,
                cancellationToken: cancellationToken
            );
        }
        finally
        {
            // Mark user as no longer busy
            userState.IsBusy = false;
        }
    }

    #endregion


    #region Delete Files From File

    public async Task HandleDeleteCallsByFileNameCommand(string fileName, long chatId, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        try
        {
            // Call the repository method to delete the call histories for the given file name
            await _callHistoryRepository.DeleteCallHistoriesByFileNameAsync(fileName);

            // Send a success message to the user
            await botClient.SendMessage(chatId, $"Successfully deleted all call histories for the file: {fileName}.",
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            // Handle any error during the deletion process
            await botClient.SendMessage(chatId,
                $"Failed to delete call histories for the file: {fileName}. Error: {ex.Message}",
                cancellationToken: cancellationToken);
        }
    }

    #endregion

    #endregion


    private async Task HandleHasRecentCallCommand(
        string[] commandParts,
        long chatId,
        ITelegramBotClient botClient,
        CancellationToken cancellationToken,
        UserState userState)
    {
        // Check if the user is busy processing a request
        if (userState.IsBusy)
        {
            await botClient.SendMessage(chatId,
                "⚠️ You are currently processing a request. Please wait until it is completed.",
                cancellationToken: cancellationToken);
            return;
        }

        // Mark user as busy while processing this request
        userState.IsBusy = true;

        // Ensure the user provided a valid phone number and a time span
        if (commandParts.Length < 3 || !TimeSpan.TryParse(commandParts[2], out var timeSpan))
        {
            await botClient.SendMessage(chatId, "Usage: /hasrecentcall [phoneNumber] [timeSpan]",
                cancellationToken: cancellationToken);
            userState.IsBusy = false; // Reset user state if input is invalid
            return;
        }

        try
        {
            var phoneNumber = commandParts[1];

            // Check if the phone number has had a recent call within the specified time window
            var hasRecentCall = await _callHistoryRepository.HasRecentCallWithinTimeSpanAsync(phoneNumber, timeSpan);

            // Construct the message based on the result
            var message = hasRecentCall
                ? $"✅ The phone number {phoneNumber} has had a recent call within the last {timeSpan.TotalMinutes} minutes."
                : $"❌ No recent calls found for {phoneNumber} within the last {timeSpan.TotalMinutes} minutes.";

            // Send the result back to the user
            await botClient.SendMessage(chatId, message, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            // Log the error and inform the user about the issue
            Log($"Error processing the /hasrecentcall command: {ex.Message}");
            await botClient.SendMessage(
                chatId,
                "❌ *Error processing command*\n\n" +
                "Oops, something went wrong while trying to process your command. Please try again later. If the issue persists, contact support.",
                cancellationToken: cancellationToken
            );
        }
        finally
        {
            // Ensure user is marked as not busy once the task completes
            userState.IsBusy = false;
        }
    }

    /// <summary>
    ///     Retrieves the user details based on phone number and sends it to the user via Telegram.
    /// </summary>
    /// <summary>
    ///     Sends user details to a Telegram chat based on the phone number.
    /// </summary>
    /// <param name="phoneNumber">The phone number of the user.</param>
    /// <param name="chatId">The Telegram chat ID to send the details to.</param>
    /// <param name="cancellationToken">Token to cancel the operation if needed.</param>
    public async Task SendUserDetailsToTelegramAsync(string phoneNumber, long chatId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Fetch user details by phone number
            var user = await _callHistoryRepository.GetUserDetailsByPhoneNumberAsync(phoneNumber);

            // Check if user was found
            if (user == null)
            {
                await _botClient.SendMessage(
                    chatId,
                    "❌ *No user found with the provided phone number.* Please try again with a valid phone number.",
                    ParseMode.Markdown,
                    cancellationToken: cancellationToken
                );
                return;
            }

            // Format the message with user details
            var message = $"👤 *User Details for Phone Number {user.UserNumberFile}*:\n\n" +
                          $"📞 *Phone Number*: {user.UserNumberFile ?? "Not Available"}\n" +
                          $"👤 *Full Name*: {user.UserNameFile ?? "Not Available"} {user.UserFamilyFile ?? "Not Available"}\n" +
                          $"🧔 *Father's Name*: {user.UserFatherNameFile ?? "Not Available"}\n" +
                          $"🎂 *Date of Birth*: {user.UserBirthDayFile ?? "Not Available"}\n" +
                          $"🏠 *Address*: {user.UserAddressFile ?? "Not Available"}\n" +
                          $"📱 *Telegram ID*: {user.UserTelegramID?.ToString() ?? "Not Available"}\n" +
                          $"💬 *Description*: {user.UserDescriptionFile ?? "Not Available"}\n" +
                          $"🌍 *Source*: {user.UserSourceFile ?? "Not Available"}\n" +
                          $"🔢 *User ID*: {user.UserId}\n";

            // Send the user details message to Telegram
            await _botClient.SendMessage(
                chatId,
                message,
                ParseMode.Markdown,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception ex)
        {
            // Handle exceptions by logging the error and sending a generic error message to Telegram
            // Log the exception (log code not shown here, replace with your logging mechanism)
            Console.WriteLine($"Error sending user details to Telegram: {ex.Message}");

            // Send an error message to the Telegram chat
            await _botClient.SendMessage(
                chatId,
                "⚠️ An error occurred while retrieving user details. Please try again later.",
                ParseMode.Markdown,
                cancellationToken: cancellationToken
            );
        }
    }


    #region HandleAllCallWithName Method with Progress

    private async Task HandleAllCallWithName(
        string[] commandParts,
        long chatId,
        ITelegramBotClient botClient,
        CancellationToken cancellationToken,
        UserState userState)
    {
        // Check if the user is busy processing a request
        if (userState.IsBusy)
        {
            await botClient.SendMessage(chatId,
                "⚠️ You are currently processing a request. Please wait until it is completed.",
                cancellationToken: cancellationToken);
            return;
        }

        userState.IsBusy = true;

        // Ensure a phone number is provided
        if (commandParts.Length < 2)
        {
            await botClient.SendMessage(chatId, "Usage: /getallcalls [phoneNumber]",
                cancellationToken: cancellationToken);
            userState.IsBusy = false;
            return;
        }

        var phoneNumber = commandParts[1];

        try
        {
            // Display a "Please Wait" message to the user
            var progressMessage = await botClient.SendMessage(
                chatId, "⚙️ Generating your file. Please wait...", cancellationToken: cancellationToken);

            // Fetch call history
            var calls = await _callHistoryRepository.GetCallsWithUserNamesAsync(phoneNumber, cancellationToken);
            if (calls == null || !calls.Any())
            {
                await botClient.SendMessage(chatId, $"❌ No calls found for the phone number {phoneNumber}.",
                    cancellationToken: cancellationToken);
                return;
            }

            // Show file format options (this method should prompt the user to choose CSV, JSON, or Excel format)
            await DisplayFileFormatButtonsAsync(chatId, botClient, cancellationToken);


            // Generate the CSV file
            var csvFilePath = GenerateCsv(calls, phoneNumber);
            if (string.IsNullOrEmpty(csvFilePath))
            {
                await botClient.SendMessage(chatId, "❌ Failed to generate CSV file. Please try again later.",
                    cancellationToken: cancellationToken);
                return;
            }

            // Handle CSV file sending in chunks (large files need chunking)
            await SendChunkedCsvFilesAsync(csvFilePath, chatId, botClient, cancellationToken, phoneNumber);

            // Let the user know the file has been sent successfully
            await botClient.SendMessage(chatId, "📁 Your call history has been sent in a CSV file.",
                cancellationToken: cancellationToken);

            // Optional: Clean up the generated file if no longer needed
            File.Delete(csvFilePath);
        }
        catch (Exception ex)
        {
            Log($"Error processing the /getallcalls command: {ex.Message}");
            await botClient.SendMessage(chatId,
                "❌ *Error processing command*\n\nOops, something went wrong. Please try again later.",
                ParseMode.Markdown, cancellationToken: cancellationToken);
        }
        finally
        {
            userState.IsBusy = false;
        }
    }


    #region CSV Generation with Enhanced Error Handling

    public string GenerateCsv(IEnumerable<CallHistoryWithUserNames> calls, string phoneNumber)
    {
        // Construct the CSV file path with a timestamp and phone number
        var csvFilePath = Path.Combine(Path.GetTempPath(),
            $"{phoneNumber}_CallHistory_{DateTime.UtcNow:yyyyMMddHHmmss}.csv");

        try
        {
            // Use StreamWriter with UTF-8 encoding and BOM to ensure proper encoding for the CSV file
            using (var streamWriter =
                   new StreamWriter(csvFilePath, false, new UTF8Encoding(true))) // 'true' ensures BOM is included
            using (var csv = new CsvWriter(streamWriter, CultureInfo.InvariantCulture))
            {
                // Writing CSV headers
                csv.WriteField("Configure");
                csv.WriteField("Incoming Calls");
                csv.WriteField("Outgoing Calls");
                csv.WriteField("CallDateTime");
                csv.WriteField("Duration");
                csv.WriteField("CallType");
                csv.WriteField("FileName");
                csv.WriteField("CallerName");
                csv.WriteField("ReceiverName");
                csv.WriteField("CallId");
                csv.NextRecord(); // Move to the next record

                // Writing data rows
                foreach (var call in calls)
                {
                    // Write each field of the current call history to the CSV
                    csv.WriteField(0); // Placeholder for "Configure"
                    csv.WriteField("0" + call.SourcePhoneNumber); // Adjust prefix if needed
                    csv.WriteField("0" + call.DestinationPhoneNumber); // Adjust prefix if needed
                    csv.WriteField(call.CallDateTime); // Date and time of the call
                    csv.WriteField(call.Duration); // Duration of the call
                    csv.WriteField(call.CallType); // Type of the call (incoming/outgoing)
                    csv.WriteField(call.FileName); // Associated file name, if applicable
                    csv.WriteField(call.CallerName ?? string.Empty); // Caller name (or empty if null)
                    csv.WriteField(call.ReceiverName ?? string.Empty); // Receiver name (or empty if null)
                    csv.WriteField(call.CallId); // Unique Call ID
                    csv.NextRecord(); // Move to the next record
                }
            }
        }
        catch (Exception ex)
        {
            // Log error and delete file if an exception occurs during CSV generation
            Log($"Error generating CSV for {phoneNumber}: {ex}");
            if (File.Exists(csvFilePath)) File.Delete(csvFilePath); // Clean up by deleting the file if an error occurs

            return null; // Return null if CSV generation fails
        }

        return csvFilePath; // Return the file path if CSV generation is successful
    }

    #endregion

    #region Async Chunked File Sending with Error Handling

    public async Task SendChunkedCsvFilesAsync(string csvFilePath, long chatId, ITelegramBotClient botClient,
        CancellationToken cancellationToken, string phoneNumber)
    {
        const int maxFileSize = 48 * 1024 * 1024; // 50 MB limit for Telegram
        var fileInfo = new FileInfo(csvFilePath);

        // If the file size is under the limit, send the file directly
        if (fileInfo.Length <= maxFileSize)
        {
            await SendCsvFileAsync(csvFilePath, chatId, botClient, cancellationToken, phoneNumber);
            return;
        }

        // Split the file into smaller chunks
        var chunkIndex = 0;
        using (var fileStream = new FileStream(csvFilePath, FileMode.Open, FileAccess.Read))
        {
            var buffer = new byte[maxFileSize];
            int bytesRead;
            while ((bytesRead = await fileStream.ReadAsync(buffer, 0, maxFileSize, cancellationToken)) > 0)
            {
                var chunkFileName = GetPersianFileName(phoneNumber);
                var chunkFilePath = Path.Combine(Path.GetDirectoryName(csvFilePath), chunkFileName);

                // Write the chunk to a new file
                await File.WriteAllBytesAsync(chunkFilePath, buffer.Take(bytesRead).ToArray(), cancellationToken);

                // Send the chunk file
                await SendCsvFileAsync(chunkFilePath, chatId, botClient, cancellationToken, phoneNumber);

                // Clean up the chunk file after sending
                File.Delete(chunkFilePath);

                chunkIndex++;
            }
        }
    }

    private string GetPersianFileName(string phoneNumber)
    {
        // Get the current UTC time and convert it to the Persian calendar
        var persianCalendar = new PersianCalendar();
        var currentDate = DateTime.UtcNow;
        var persianYear = persianCalendar.GetYear(currentDate);
        var persianMonth = persianCalendar.GetMonth(currentDate);
        var persianDay = persianCalendar.GetDayOfMonth(currentDate);
        var persianHour = persianCalendar.GetHour(currentDate);
        var persianMinute = persianCalendar.GetMinute(currentDate);
        var persianSecond = persianCalendar.GetSecond(currentDate);

        // Format the Persian date for the file name (yyyyMMddHHmmss)
        var persianDate =
            $"{persianYear:D4}{persianMonth:D2}{persianDay:D2}{persianHour:D2}{persianMinute:D2}{persianSecond:D2}";

        // Return the formatted file name
        return $"getcalls_{phoneNumber}_{persianDate}.csv";
    }

    #endregion

    #endregion


    #region Async Chunked File Sending with Error Handling

    private async Task SendHtmlFileAsync(string phoneNumber, long chatId, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        var htmlFilePath = Path.Combine(Path.GetTempPath(),
            $"{phoneNumber}_CallHistory_{DateTime.UtcNow:yyyyMMddHHmmss}.html");

        try
        {
            var htmlContent = new StringBuilder();
            htmlContent.Append(
                "<html><body><table border='1'><tr><th>Configure</th><th>Incoming Calls</th><th>Outgoing Calls</th><th>CallDateTime</th><th>Duration</th></tr>");

            foreach (var call in callHistoryData)
            {
                htmlContent.Append("<tr>");
                htmlContent.Append(
                    $"<td>0</td><td>0{call.SourcePhoneNumber}</td><td>0{call.DestinationPhoneNumber}</td><td>{call.CallDateTime}</td><td>{call.Duration}</td>");
                htmlContent.Append("</tr>");
            }

            htmlContent.Append("</table></body></html>");

            await File.WriteAllTextAsync(htmlFilePath, htmlContent.ToString(), cancellationToken);
            await SendFileAsync(htmlFilePath, chatId, botClient, cancellationToken, "HTML");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error exporting HTML file: {ex.Message}");
        }
    }

    private async Task SendFileAsync(string filePath, long chatId, ITelegramBotClient botClient,
        CancellationToken cancellationToken, string fileType)
    {
        try
        {
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                var inputFile = new InputFileStream(stream, Path.GetFileName(filePath));
                await botClient.SendDocumentAsync(chatId, inputFile, caption: $"{fileType} file generated",
                    cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending {fileType} file: {ex.Message}");
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    #endregion


    #region Utility for Persian Date in File Names

    private string GetPersianFileName(string phoneNumber, int chunkIndex)
    {
        var persianCalendar = new PersianCalendar();
        var currentDate = DateTime.UtcNow;
        var persianDate =
            $"{persianCalendar.GetYear(currentDate):D4}{persianCalendar.GetMonth(currentDate):D2}{persianCalendar.GetDayOfMonth(currentDate):D2}_{chunkIndex:D2}";

        return $"getallcalls_{phoneNumber}_{persianDate}.csv";
    }

    #endregion


    #region Display Buttons for File Options

    private async Task DisplayFileFormatButtonsAsync(long chatId, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        var buttons = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📄 CSV"),
                InlineKeyboardButton.WithCallbackData("📑 TSV")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📊 Excel (XLSX)"),
                InlineKeyboardButton.WithCallbackData("📊 Excel (XLSM)")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📘 PDF"),
                InlineKeyboardButton.WithCallbackData("📝 JSON")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🔤 XML"),
                InlineKeyboardButton.WithCallbackData("📋 TXT")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🔗 HTML"),
                InlineKeyboardButton.WithCallbackData("📈 ODS") // OpenDocument Spreadsheet format
            }
        });

        var message = await botClient.SendMessage(chatId, "Choose the file format:", replyMarkup: buttons,
            cancellationToken: cancellationToken);
        await botClient.PinChatMessage(chatId, message.MessageId, cancellationToken: cancellationToken);
    }

    #endregion


    private async Task SendCsvFileAsync(string csvFilePath, long chatId, ITelegramBotClient botClient,
        CancellationToken cancellationToken, string phoneNumber)
    {
        try
        {
            using (var stream = new FileStream(csvFilePath, FileMode.Open, FileAccess.Read))
            {
                var inputFile = new InputFileStream(stream, Path.GetFileName(csvFilePath));
                await botClient.SendDocument(chatId, inputFile, $"CSV data for phone number: {phoneNumber}",
                    cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending CSV file: {ex.Message}");
        }
    }

    #endregion


    #region Main Conversion Method

    public async Task ZipCsvFileAsync(string csvFilePath, string zipFilePath)
    {
        try
        {
            // Ensure the CSV file exists before proceeding
            if (!File.Exists(csvFilePath))
            {
                _logger.LogError($"The file '{csvFilePath}' does not exist.");
                return;
            }

            // Create a new ZipFile and add the CSV file to it
            using (var zipArchive = ZipFile.Open(zipFilePath, ZipArchiveMode.Create))
            {
                // Add the CSV file to the zip archive
                var zipEntry = zipArchive.CreateEntry(Path.GetFileName(csvFilePath));

                // Open the CSV file and copy its content to the zip entry
                using (var entryStream = zipEntry.Open())
                using (var fileStream = new FileStream(csvFilePath, FileMode.Open, FileAccess.Read))
                {
                    await fileStream.CopyToAsync(entryStream);
                }

                _logger.LogInformation($"File successfully zipped at: {zipFilePath}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error zipping the file: {ex.Message}");
        }
    }


    /// <summary>
    ///     Converts a collection of items to a CSV-formatted string, with custom handling to force Excel to treat strings as
    ///     text.
    ///     This method can be extended to better handle Excel import by ensuring data is presented in a specific way that
    ///     mimics a theme.
    /// </summary>
    /// <typeparam name="T">The type of items to be converted.</typeparam>
    /// <param name="items">The collection of items to convert to CSV.</param>
    /// <returns>A CSV string representation of the collection, or null if conversion fails.</returns>
    private string ConvertToCsv<T>(IEnumerable<T> items)
    {
        if (items == null || !items.Any())
        {
            _logger.LogWarning("ConvertToCsv called with null or empty collection. Returning an empty CSV string.");
            return string.Empty;
        }

        try
        {
            // Manually trim all string properties of the items to remove unnecessary spaces
            var trimmedItems = items.Select(item =>
            {
                // You can add more logic here if you want to trim specific fields in a class
                var properties = item.GetType().GetProperties();
                foreach (var property in properties)
                    if (property.PropertyType == typeof(string))
                    {
                        var value = (string)property.GetValue(item);
                        if (value != null) property.SetValue(item, value.Trim()); // Trim the string value
                    }

                return item;
            }).ToList();

            using (var writer = new StringWriter())
            using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
                   {
                       HasHeaderRecord = true, // Include a header row
                       Delimiter = ",", // Customize delimiter if needed
                       ShouldQuote = field => true // Quote all fields
                   }))
            {
                // Register custom converter for string types to ensure Excel treats them as text
                csv.Context.TypeConverterCache.AddConverter<string>(new ApostropheConverter());

                // Write records to CSV
                csv.WriteRecords(trimmedItems);

                // Return the CSV string (trimmed and optimized)
                _logger.LogInformation("CSV conversion successful.");
                return writer.ToString().Trim();
            }
        }
        catch (CsvHelperException ex)
        {
            _logger.LogError($"CSV conversion error at row or field level: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Unexpected error during CSV conversion: {ex.Message}");
            return null;
        }
    }

    #endregion


    #region Logging

    /// <summary>
    ///     Logs a message with a specified log level.
    /// </summary>
    /// <param name="message">The log message to log.</param>
    /// <param name="logLevel">The level of logging (INFO, WARNING, ERROR).</param>
    private void Log(string message, string logLevel = "INFO")
    {
        // Define color and font weight based on log level
        Color color;
        FontWeight fontWeight;
        switch (logLevel.ToUpper())
        {
            case "ERROR":
                color = Colors.Red;
                fontWeight = FontWeights.Bold;
                break;
            case "WARNING":
                color = Colors.Orange;
                fontWeight = FontWeights.Bold;
                break;
            case "INFO":
            default:
                color = Colors.Black;
                fontWeight = FontWeights.Normal;
                break;
        }

        // Create log message with a timestamp
        var logMessage = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {logLevel}: {message}";

        // Append to RichTextBox with formatting
        AppendLogToRichTextBox(logMessage, color, 14, fontWeight: fontWeight);

        // Log to a file
        try
        {
            LogToFile(logMessage);
        }
        catch (Exception ex)
        {
            // Handle file logging errors
            AppendLogToRichTextBox($"Failed to log to file: {ex.Message}", Colors.Red, 14,
                fontWeight: FontWeights.Bold);
        }
    }


    // Adds error details to the UI and writes them to a file
    private void LogError(Exception ex)
    {
        var errorLog = $"[{DateTime.UtcNow}] ERROR: {ex.Message}\nStack Trace: {ex.StackTrace}";
        AppendLogToRichTextBox(errorLog, Colors.Red, 16); // Specify namespace
        LogToFile(errorLog);
    }

    private const long MaxLogFileSize = 10 * 1024 * 1024; // 10 MB

    /// <summary>
    ///     Persists log messages to a file with structured logging and error handling.
    /// </summary>
    /// <param name="message">The log message to be saved.</param>
    private async Task LogToFile(string message)
    {
        // Create the log file path
        var logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        var logFilePath = Path.Combine(logDirectory, "error_log.txt");

        // Ensure the directory exists
        Directory.CreateDirectory(logDirectory);

        try
        {
            // Check if the log file exists and its size
            if (File.Exists(logFilePath) && new FileInfo(logFilePath).Length >= MaxLogFileSize)
            {
                // If the file size exceeds the limit, rename it for rotation
                var newLogFilePath = Path.Combine(logDirectory, $"error_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.Move(logFilePath, newLogFilePath);
            }

            // Prepare the log message with a timestamp
            var logMessage = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} - {message}";

            // Append the log message to the file asynchronously
            await File.AppendAllTextAsync(logFilePath, logMessage + Environment.NewLine);
        }
        catch (Exception ex)
        {
            // Handle any exceptions that occur during logging
            // Consider logging to a secondary error log or showing a message to the user
            Console.WriteLine($"Failed to log to file: {ex.Message}");
        }
    }


    private bool _isUpdating; // Flag to prevent recursive calls

    private async void AppendLogToRichTextBox(
        string message,
        Color defaultColor,
        double defaultFontSize,
        string fontFamily = "Segoe UI",
        FontWeight? fontWeight = null)
    {
        // Ensure thread-safe UI operations
        if (!Dispatcher.CheckAccess())
        {
            // Use async invoke to ensure UI updates are safe
            await Dispatcher.InvokeAsync(() =>
                AppendLogToRichTextBox(message, defaultColor, defaultFontSize, fontFamily, fontWeight));
            return;
        }

        // Avoid recursion by checking the flag
        if (_isUpdating) return;

        try
        {
            _isUpdating = true; // Set flag to prevent recursive calls

            // Ensure that the LogTextBox is of type RichTextBox at the start
            if (LogTextBox is RichTextBox richTextBox)
            {
                // Parse the message with inline formatting
                var paragraph = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };

                // ParseFormattedMessage should return an IEnumerable<Run> for inline formatting
                var runs = ParseFormattedMessage(message, defaultColor, defaultFontSize, fontFamily, fontWeight);

                // Add parsed inline elements to the paragraph
                foreach (var run in runs) paragraph.Inlines.Add(run);

                // Append the paragraph to the RichTextBox's document blocks
                richTextBox.Document.Blocks.Add(paragraph);

                // Auto-scroll to the bottom after adding new content
                richTextBox.ScrollToEnd();
            }
            else
            {
                // If LogTextBox isn't a RichTextBox, show a type mismatch error
                var errorMessage = "LogTextBox must be a RichTextBox for formatted logging.";
                LogError(new Exception(errorMessage)); // Log error if type mismatch occurs
                /// MessageBox.Show(errorMessage, "Type Mismatch", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            // Catch any exception that occurs while appending and handle it properly
            var errorMessage = $"An error occurred while appending to the log: {ex.Message}";
            LogError(new Exception(errorMessage)); // Log error to file or external system
            MessageBox.Show(errorMessage, "Error", MessageBoxButton.OK,
                MessageBoxImage.Error); // Show error to the user
        }
        finally
        {
            _isUpdating = false; // Reset the flag after the operation completes
        }
    }

    private List<Run> ParseFormattedMessage(
        string message,
        Color defaultColor,
        double defaultFontSize,
        string fontFamily,
        FontWeight? fontWeight = null,
        FontStyle? fontStyle = null)
    {
        var runs = new List<Run>();
        var regex = new Regex(
            @"\[(color|bold|italic|underline)=(?<property>[^]]+)\](?<text>.+?)\[/\1\]|" +
            @"\[(bold|italic|underline)\](?<styledText>.+?)\[/\1\]",
            RegexOptions.Singleline);

        var lastIndex = 0;

        foreach (Match match in regex.Matches(message))
        {
            if (match.Index > lastIndex)
            {
                // Add preceding text as normal Run
                var precedingText = message.Substring(lastIndex, match.Index - lastIndex);
                runs.Add(new Run(precedingText)
                {
                    Foreground = new SolidColorBrush(defaultColor),
                    FontSize = defaultFontSize,
                    FontFamily = new FontFamily(fontFamily),
                    FontWeight = fontWeight ?? FontWeights.Normal,
                    FontStyle = fontStyle ?? FontStyles.Normal
                });
            }

            // Handle color formatting [color=xxx]
            if (match.Groups["color"].Success && match.Groups["text"].Success)
            {
                var colorName = match.Groups["color"].Value;
                var text = match.Groups["text"].Value;
                var color = (Color)ColorConverter.ConvertFromString(colorName);
                if (color == null) color = defaultColor;

                runs.Add(new Run(text)
                {
                    Foreground = new SolidColorBrush(color),
                    FontSize = defaultFontSize,
                    FontFamily = new FontFamily(fontFamily),
                    FontWeight = fontWeight ?? FontWeights.Normal,
                    FontStyle = fontStyle ?? FontStyles.Normal
                });
            }
            // Handle bold, italic, or underline tags
            else if (match.Groups["boldText"].Success)
            {
                var text = match.Groups["boldText"].Value;
                runs.Add(new Run(text)
                {
                    Foreground = new SolidColorBrush(defaultColor),
                    FontSize = defaultFontSize,
                    FontFamily = new FontFamily(fontFamily),
                    FontWeight = FontWeights.Bold,
                    FontStyle = fontStyle ?? FontStyles.Normal
                });
            }
            else if (match.Groups["italicText"].Success)
            {
                var text = match.Groups["italicText"].Value;
                runs.Add(new Run(text)
                {
                    Foreground = new SolidColorBrush(defaultColor),
                    FontSize = defaultFontSize,
                    FontFamily = new FontFamily(fontFamily),
                    FontWeight = fontWeight ?? FontWeights.Normal,
                    FontStyle = FontStyles.Italic
                });
            }
            else if (match.Groups["underlineText"].Success)
            {
                var text = match.Groups["underlineText"].Value;
                runs.Add(new Run(text)
                {
                    Foreground = new SolidColorBrush(defaultColor),
                    FontSize = defaultFontSize,
                    FontFamily = new FontFamily(fontFamily),
                    FontWeight = fontWeight ?? FontWeights.Normal,
                    TextDecorations = TextDecorations.Underline
                });
            }

            lastIndex = match.Index + match.Length;
        }

        // Add the remaining text after the last match
        if (lastIndex < message.Length)
        {
            var remainingText = message.Substring(lastIndex);
            runs.Add(new Run(remainingText)
            {
                Foreground = new SolidColorBrush(defaultColor),
                FontSize = defaultFontSize,
                FontFamily = new FontFamily(fontFamily),
                FontWeight = fontWeight ?? FontWeights.Normal,
                FontStyle = fontStyle ?? FontStyles.Normal
            });
        }

        return runs;
    }

    #endregion


    #region Excel File Import

    private async Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery,
        CancellationToken cancellationToken)
    {
        // Store the user response
        var userResponses = new ConcurrentDictionary<long, string>();

        // Store the response in the dictionary
        userResponses[callbackQuery.From.Id] = callbackQuery.Data;

        // Answer the callback query to acknowledge the button press
        await botClient.AnswerCallbackQuery(callbackQuery.Id, "Received your choice!",
            cancellationToken: cancellationToken);

        // Process the callback query based on the data
        switch (callbackQuery.Data)
        {
            case "confirm_import":
                await botClient.SendMessage(callbackQuery.From.Id, "You have confirmed the import.",
                    cancellationToken: cancellationToken);
                break;
            case "cancel_import":
                await botClient.SendMessage(callbackQuery.From.Id, "You have cancelled the import.",
                    cancellationToken: cancellationToken);
                break;
            default:
                await botClient.SendMessage(callbackQuery.From.Id, "Unknown option selected.",
                    cancellationToken: cancellationToken);
                break;
        }
    }


    // Use SemaphoreSlim to limit the number of concurrent tasks
    private async Task ProcessTasksWithLimitedConcurrency(IEnumerable<Task> tasks, int maxDegreeOfParallelism)
    {
        var semaphore = new SemaphoreSlim(maxDegreeOfParallelism); // Limit concurrency
        var batchTasks = new List<Task>();

        foreach (var task in tasks)
        {
            await semaphore.WaitAsync(); // Wait for an available slot

            var taskToRun = task.ContinueWith(async t =>
            {
                try
                {
                    await t; // Run the task
                }
                finally
                {
                    semaphore.Release(); // Release the slot when the task is done
                }
            });

            batchTasks.Add(taskToRun);
        }

        // Wait for all tasks to complete
        await Task.WhenAll(batchTasks);
    }

    private async Task ImportExcelToDatabase(string filePath, ITelegramBotClient botClient, long chatId,
        CancellationToken cancellationToken)
    {
        var usersToImport = new List<User>();
        var errors = new List<string>();
        var processedRows = 0;
        var startTime = DateTime.Now;
        var totalRows = 0;

        try
        {
            // Validate file existence
            if (!File.Exists(filePath) || new FileInfo(filePath).Length == 0)
            {
                await NotifyUser(botClient, chatId, "Error: Invalid file. Please resend.", cancellationToken);
                return;
            }

            Log("File verified. Loading...");
            using var package = new ExcelPackage(new FileInfo(filePath));
            var worksheet = package.Workbook.Worksheets.FirstOrDefault();
            if (worksheet?.Dimension?.Rows == 0)
            {
                await NotifyUser(botClient, chatId, "Error: Empty worksheet.", cancellationToken);
                return;
            }

            totalRows = worksheet.Dimension.Rows;

            // Dynamically calculate optimal batch size
            const int maxBatchSize = 5;
            const int minBatchSize = 15;
            var batchSize = Math.Max(minBatchSize, Math.Min(totalRows / 10, maxBatchSize));

            var maxConcurrency = Math.Max(1, Math.Min(Environment.ProcessorCount * 2, totalRows / batchSize));

            var batchProcessingQueue = new Queue<(int startRow, int endRow)>();

            // Add batches to queue for processing
            for (var row = 2; row <= totalRows; row += batchSize)
            {
                var batchEnd = Math.Min(row + batchSize - 1, totalRows);
                batchProcessingQueue.Enqueue((row, batchEnd));
            }

            // Process rows in parallel batches
            var semaphore = new SemaphoreSlim(maxConcurrency);
            var rowProcessingTasks = new List<Task>();

            while (batchProcessingQueue.Any())
            {
                var (startRow, endRow) = batchProcessingQueue.Dequeue();
                if (cancellationToken.IsCancellationRequested) break;

                var batchTask = Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var batchUsers = new List<User>();
                        var batchErrors = new List<string>();

                        // Process each row within the batch
                        Parallel.For(startRow, endRow + 1,
                            new ParallelOptions { MaxDegreeOfParallelism = maxConcurrency }, i =>
                            {
                                if (cancellationToken.IsCancellationRequested) return;

                                try
                                {
                                    var user = CreateUserFromRow(worksheet, i, chatId,filePath,batchErrors);
                                    if (user != null)
                                        lock (batchUsers)
                                        {
                                            batchUsers.Add(user);
                                        }

                                    Interlocked.Increment(ref processedRows);
                                }
                                catch (Exception ex)
                                {
                                    lock (batchErrors)
                                    {
                                        batchErrors.Add($"Row {i} error: {ex.Message}");
                                    }

                                    Log($"Row {i} error: {ex.Message}");
                                }
                            });

                        // Add to global lists
                        lock (usersToImport)
                        {
                            usersToImport.AddRange(batchUsers);
                        }

                        lock (errors)
                        {
                            errors.AddRange(batchErrors);
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken);

                rowProcessingTasks.Add(batchTask);

                if (rowProcessingTasks.Count >= maxConcurrency)
                {
                    await Task.WhenAll(rowProcessingTasks);
                    rowProcessingTasks.Clear();
                }
            }

            if (rowProcessingTasks.Count > 0) await Task.WhenAll(rowProcessingTasks);

            // Bulk insert remaining users to database
            if (usersToImport.Count > 0)
            {
                var databaseSaved = false;
                var retryCount = 0;
                while (!databaseSaved && retryCount < 5)
                    try
                    {
                        await BulkSaveUsersToDatabase(usersToImport, botClient, chatId, cancellationToken, filePath);
                        databaseSaved = true;
                    }
                    catch (Exception dbEx)
                    {
                        retryCount++;
                        Log($"Database error: {dbEx.Message}. Retrying {retryCount}/5...");
                        await Task.Delay(5000, cancellationToken); // Delay before retry
                    }

                if (!databaseSaved) errors.Add("❌ Failed to save users to database after multiple retries.");
            }

            var endTime = DateTime.Now;
            var resultMessage = errors.Count > 0
                ? $"Processing complete. Imported {processedRows} rows with {errors.Count} errors. Duration: {(endTime - startTime).TotalSeconds} seconds."
                : $"Processing complete. Successfully imported {processedRows} rows in {(endTime - startTime).TotalSeconds} seconds.";

            await NotifyUser(botClient, chatId, resultMessage, cancellationToken);
            Log($"Import completed in {(endTime - startTime).TotalSeconds} seconds.");
        }
        catch (Exception ex)
        {
            Log($"Unexpected Error: {ex.Message}");
            await NotifyUser(botClient, chatId, "❌ An unexpected error occurred. Please try again.", cancellationToken);
        }
    }


    private async Task BulkSaveUsersToDatabase(List<User> usersToImport, ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken, string filePath)
    {
        try
        {
            int totalUsers = usersToImport.Count;
            int processedUsers = 0;
            List<string> validationErrors = new List<string>();  // List to collect validation error messages
            List<User> validUsers = new List<User>();           // List to collect valid users

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlServer("Data Source=.;Integrated Security=True;Encrypt=True;Trust Server Certificate=True");

            using (var dbContext = new AppDbContext(optionsBuilder.Options))
            {
                // Start a new transaction for the entire import to ensure data integrity
                using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
                {
                    try
                    {
                        // Manually validate users one by one before saving
                        foreach (var user in usersToImport)
                        {
                            var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>(); // Use fully qualified name

                            var validationContext = new ValidationContext(user);

                            bool isValid = Validator.TryValidateObject(user, validationContext, validationResults, true);
                            if (!isValid)
                            {
                                // Collect validation errors for the user
                                foreach (var validationResult in validationResults)
                                {
                                    validationErrors.Add($"User {user.UserId} - {validationResult.ErrorMessage}");
                                }
                            }
                            else
                            {
                                // If valid, add to the list of valid users
                                validUsers.Add(user);
                            }
                        }

                        // If there are valid users, perform the bulk insert
                        if (validUsers.Any())
                        {
                            await dbContext.Users.AddRangeAsync(validUsers, cancellationToken);

                            // Save changes to the database
                            await dbContext.SaveChangesAsync(cancellationToken);

                            // Commit the transaction after successful insert
                            await transaction.CommitAsync(cancellationToken);

                            processedUsers = validUsers.Count;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Rollback transaction on error
                        await transaction.RollbackAsync(cancellationToken);
                        LogError($"Error during database transaction: {ex.Message}");
                        validationErrors.Add("An unexpected error occurred during the transaction.");
                    }
                }
            }

            // Notify the user with the final result
            if (processedUsers == totalUsers)
            {
                await NotifyUser(botClient, chatId, $"Successfully imported {processedUsers} users.", cancellationToken);
            }
            else
            {
                if (validationErrors.Any())
                {
                    // If there are errors, display them
                    string errorMessage = string.Join("\n", validationErrors);
                    await NotifyUser(botClient, chatId, $"❌ Failed to import some users. Errors:\n{errorMessage}", cancellationToken);
                }
                else
                {
                    await NotifyUser(botClient, chatId, $"❌ Failed to import users. Only {processedUsers}/{totalUsers} were saved.", cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            LogError($"Error during database transaction: {ex.Message}");
            await NotifyUser(botClient, chatId, "❌ An unexpected error occurred. Please try again.", cancellationToken);
        }
    }



    private async Task NotifyProgress(ITelegramBotClient botClient, long chatId, int progress,
        CancellationToken cancellationToken)
    {
        // Sends progress updates to the user (this can be adjusted to minimize API calls)
        await botClient.SendTextMessageAsync(
            chatId,
            $"Import in progress: {progress}%",
            cancellationToken: cancellationToken
        );
    }


    #region User Creation and Update Methods

    // In-memory list to simulate database or user storage
    private readonly List<User> _users = new();


    public User CreateUserFromRow(
       ExcelWorksheet worksheet,
       int row,
       long chatId,
       string path,
       List<string> errors)
    {
        // Initialize fields as null to allow missing data to be saved as null in the database
        string? userPhone = null;
        string? userName = null;
        string? userFamily = null;
        string? userFatherName = null;
        string? userBirthDayString = null; // Shamsi date stored as string
        string? userAddress = null;
        string? userDescription = null;
        string? userSource = null;

        try
        {
            // Safe access to worksheet cells with null checks and default fallbacks
            userPhone = GetCellValue(worksheet, row, 1) ?? "Not Found";
            userPhone = NormalizeIranianPhoneNumber(userPhone);
            userFamily = GetCellValue(worksheet, row, 3) ?? "No Family";
            userFatherName = GetCellValue(worksheet, row, 4) ?? "No Father Name";
            userBirthDayString = GetCellValue(worksheet, row, 5) ?? "No BirthDay";
            userAddress = GetCellValue(worksheet, row, 6) ?? "No Address";
            userDescription = GetCellValue(worksheet, row, 7) ?? "No Desc";
            userSource = path ?? GetCellValue(worksheet, row, 9) ?? "No Source";
        }
        catch (Exception ex)
        {
            // Log any errors processing the row and skip the problematic row
            errors.Add($"Row {row}: Error processing row - {ex.Message}");
            return null; // Skip the row by returning null
        }

        // Normalize phone number: If it starts with "98", convert it to "09"
        if (!string.IsNullOrEmpty(userPhone) && userPhone.StartsWith("98"))
        {
            userPhone = "09" + userPhone.Substring(2);
        }

        // Trim fields to their respective maximum lengths
        userPhone = TrimToMaxLength(userPhone, 50); // UserNumberFile max length 50
        userName = TrimToMaxLength(userName, 100); // UserNameProfile max length 100
        userFamily = TrimToMaxLength(userFamily, 150); // UserFamilyFile max length 150
        userFatherName = TrimToMaxLength(userFatherName, 100); // UserFatherNameFile max length 100
        userBirthDayString = TrimToMaxLength(userBirthDayString, 20); // UserBirthDayFile max length 20
        userAddress = TrimToMaxLength(userAddress, 250); // UserAddressFile max length 250
        userDescription = TrimToMaxLength(userDescription, 50); // UserDescriptionFile max length 50
        userSource = TrimToMaxLength(userSource, 50); // UserSourceFile max length 50

        // After processing, create and return the user object with normalized values
        return CreateNewUserFromRow(
            userPhone ?? "No UserPhone",
            userName ?? "No UserName",
            userFamily ?? "No UserFamily",
            userFatherName ?? "No UserFatherName",
            userBirthDayString ?? "NoBirthDay", // Pass birthdate as a string in Shamsi format
            userAddress ?? "No Address",
            userDescription ?? "No Desc",
            userSource,
            chatId,
            row);
    }
    // Normalize Iranian phone numbers (converts numbers starting with "98" to "09")
    private string NormalizeIranianPhoneNumber(string? userPhone)
    {
        if (string.IsNullOrEmpty(userPhone))
            return userPhone ?? string.Empty;

        // Remove all non-numeric characters (spaces, dashes, parentheses, etc.)
        var cleanedPhone = new string(userPhone.Where(char.IsDigit).ToArray());

        // Check if the phone number starts with "98" (Iran's international dialing code)
        if (cleanedPhone.StartsWith("98"))
        {
            // Replace "98" with "09" to convert to local format
            cleanedPhone = "09" + cleanedPhone.Substring(2);
        }

        // Ensure the phone number is 11 digits (Iranian phone numbers are usually 11 digits after formatting)
        if (cleanedPhone.Length != 11)
        {
            // Log error or handle invalid length as needed
            return string.Empty; // Invalid phone number, return empty string or custom error handling
        }

        return cleanedPhone;
    }


    // Helper method to trim fields to the maximum allowed length
    private string? TrimToMaxLength(string? value, int maxLength)
    {
        if (value == null)
            return null;

        return value.Length > maxLength ? value.Substring(0, maxLength) : value;
    }

    // Utility method to safely get cell value from worksheet with enhanced error handling
    private string? GetCellValue(ExcelWorksheet worksheet, int row, int column)
    {
        try
        {
            // Validate inputs: worksheet and valid row/column indices
            if (worksheet == null || row <= 0 || column <= 0 || row > worksheet.Dimension?.Rows || column > worksheet.Dimension?.Columns)
            {
                LogError($"Invalid worksheet or out-of-bounds indices. Row: {row}, Column: {column}");
                return null;
            }

            // Retrieve the cell at the specified row and column
            var cell = worksheet.Cells[row, column];

            // Check if the cell itself is null or if the value is empty or null
            if (cell == null || cell.Value == null)
            {
                LogError($"Cell at Row: {row}, Column: {column} is empty or null.");
                return null;
            }

            // Check if the cell contains a formula (using Formula property for older versions of EPPlus)
            if (!string.IsNullOrEmpty(cell.Formula))
            {
                return cell.Text?.Trim();  // Return the evaluated formula result as text
            }

            // Check for different types of cell values and format them accordingly
            return FormatCellValue(cell);
        }
        catch (Exception ex)
        {
            LogError($"Error accessing cell at Row: {row}, Column: {column}. Exception: {ex.Message}");
            return null;
        }
    }

    // Helper method for logging errors
    private void LogError(string message)
    {
        // You can replace this with a proper logging framework like NLog, Serilog, etc.
        // For now, just outputting to the console or log file
        Console.WriteLine($"[ERROR] {message}");
    }

    // Helper method to format cell values based on their type
    private string? FormatCellValue(ExcelRange cell)
    {
        // Handle different types of data in the cell
        if (cell.Value is string stringValue)
        {
            return stringValue.Trim();
        }

        if (cell.Value is double doubleValue)
        {
            return doubleValue.ToString("G");  // General format for numbers
        }

        if (cell.Value is DateTime dateTimeValue)
        {
            return dateTimeValue.ToString("yyyy-MM-dd");  // Custom date format
        }

        if (cell.Value is bool boolValue)
        {
            return boolValue.ToString();
        }

        // Handle null or unrecognized data types
        return cell.Text?.Trim();  // Return the cell's text representation
    }


    public User CreateNewUserFromRow(
        string? userPhone,
        string? userName,
        string? userFamily,
        string? userFatherName,
        string? userBirthDay,
        string? userAddress,
        string? userDescription,
        string? userSource,
        long chatId,
        int row)
    {
        // Log to verify values being passed
        Console.WriteLine($"Row {row}: Creating user with phone '{userPhone}'");

        return new User
        {
            UserNumberFile = string.IsNullOrEmpty(userPhone) ? null : userPhone,
            UserNameFile = string.IsNullOrEmpty(userName) ? null : userName,
            UserFamilyFile = string.IsNullOrEmpty(userFamily) ? null : userFamily,
            UserFatherNameFile = string.IsNullOrEmpty(userFatherName) ? null : userFatherName,
            UserBirthDayFile = string.IsNullOrEmpty(userBirthDay) ? null : userBirthDay,
            UserAddressFile = string.IsNullOrEmpty(userAddress) ? null : userAddress,
            UserDescriptionFile = string.IsNullOrEmpty(userDescription) ? null : userDescription,
            UserSourceFile = string.IsNullOrEmpty(userSource) ? null : userSource,
            UserTelegramID = chatId
        };
    }

    // Helper method to find users by phone number
    private List<User> FindUsersByPhoneNumber(string phoneNumber)
    {
        // Normalize phone number format (if necessary)
        phoneNumber = NormalizePhoneNumber(phoneNumber);

        // Log the search operation
        //   Log($"Searching for user with PhoneNumber: {phoneNumber}.");

        // Return all users with the matching phone number
        return _users.Where(u => u.UserNumberFile == phoneNumber).ToList();
    }

    #endregion


    private string NormalizePhoneNumber(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return string.Empty;

        // Example normalization: remove spaces, dashes, and parentheses
        var normalizedNumber = phoneNumber
            .Replace(" ", "") // Remove spaces
            .Replace("-", "") // Remove dashes
            .Replace("(", "") // Remove opening parentheses
            .Replace(")", ""); // Remove closing parentheses

        // You can add more normalization rules as needed

        return normalizedNumber;
    }


    public async Task SetBotCommandsAsync(ITelegramBotClient botClient)
    {
        // Define the list of bot commands with descriptions and emojis
        var commands = new[]
        {
            new BotCommand
            {
                Command = "/getcalls",
                Description =
                    "📞 Retrieve the full call history for a phone number, including date, duration, and participants."
            },
            new BotCommand
            {
                Command = "/getrecentcalls",
                Description = "📅 Get recent calls, showing times and participants for easy reference."
            },
            new BotCommand
            {
                Command = "/getlongcalls",
                Description = "⏳ Find calls that exceeded a specific duration to identify extended conversations."
            },
            new BotCommand
            {
                Command = "/gettoprecentcalls",
                Description = "🔝 Access the top N recent calls, giving you the latest records quickly."
            },
            new BotCommand
            {
                Command = "/hasrecentcall",
                Description = "🕒 Check if a phone number had calls within a specific timeframe."
            },
            new BotCommand
                { Command = "/getallcalls", Description = "📞 Retrieve a full call history with complete details." },
            new BotCommand
            {
                Command = "/whois", Description = "👤 Find call history by providing a user's name and family name."
            },
            new BotCommand { Command = "/reset", Description = "🔄 Clear the entire database of calls." },
            new BotCommand
                { Command = "/deletebyfilename", Description = "🗑️ Delete call records by a specific file name." }
        };

        try
        {
            // Await the asynchronous SetMyCommands call to set bot commands
            await botClient.SetMyCommandsAsync(commands);
            Console.WriteLine("✅ Bot commands have been successfully set.");
        }
        catch (Exception ex)
        {
            // Handle any errors that may occur during the process
            Console.WriteLine($"🚨 Error while setting bot commands: {ex.Message}");
        }
    }


    /// <summary>
    ///     Sends a message to the specified chat ID using the Telegram bot client.
    /// </summary>
    /// <param name="chatId">The chat ID to send the message to.</param>
    /// <param name="message">The message content, formatted as needed.</param>
    private async Task SendTelegramMessageAsync(long chatId, string message)
    {
        if (_botClient == null)
        {
            Log("Error: Telegram bot client is not initialized.");
            return;
        }

        // Store failed messages for later analysis or retry
        var failedMessages = new List<(long chatId, string message)>();

        // Define retry policy for specific ApiRequestExceptions
        var retryPolicy = Policy
            .Handle<ApiRequestException>(ex =>
                ex.Message.Contains("can't parse entities") || ex.Message.Contains("too many requests"))
            .WaitAndRetryAsync(3,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // Exponential backoff
                (exception, timeSpan, context) =>
                {
                    Log($"Retrying due to: {exception.Message}. Waiting {timeSpan} before next retry.");
                });

        try
        {
            // Execute the policy
            await retryPolicy.ExecuteAsync(async () =>
            {
                await _botClient.SendMessage(
                    chatId,
                    message,
                    ParseMode.Markdown // Use MarkdownV2 for rich formatting
                );

                Log($"Message sent to chat ID {chatId}.");
            });
        }
        catch (ApiRequestException ex)
        {
            // Add failed message to the list for further handling
            failedMessages.Add((chatId, message));
            Log($"Error sending message to chat ID {chatId}: {ex.Message}");

            // Optionally implement further error-specific logic
            HandleSpecificError(ex);
        }
        catch (Exception ex)
        {
            Log($"Unexpected error sending message: {ex.Message}");
        }
        finally
        {
            // If there are failed messages, process them
            if (failedMessages.Any())
            {
                Log($"Total failed messages: {failedMessages.Count}");
                ProcessFailedMessages(failedMessages);
            }
        }
    }


    /// <summary>
    ///     Handles specific errors by implementing custom logic.
    /// </summary>
    /// <param name="ex">The ApiRequestException to handle.</param>
    private void HandleSpecificError(ApiRequestException ex)
    {
        // Implement custom handling logic based on the type of error
        switch (ex.Message)
        {
            case "can't parse entities":
                // Handle parsing errors, maybe sanitize input
                Log("Sanitizing message content due to parsing error.");
                break;

            case "too many requests":
                // Handle rate limiting, maybe wait and retry later
                Log("Rate limit exceeded. Consider implementing exponential backoff.");
                break;

            // Add cases for other specific errors as needed

            default:
                Log("Unhandled error type.");
                break;
        }
    }

    /// <summary>
    ///     Processes the list of failed messages to retry sending or log them for analysis.
    /// </summary>
    /// <param name="failedMessages">List of failed messages to process.</param>
    private async Task ProcessFailedMessages(List<(long chatId, string message)> failedMessages)
    {
        foreach (var (chatId, message) in failedMessages)
        {
            // Optionally implement custom logic for each failed message
            Log($"Retrying failed message to chat ID {chatId}.");

            // You can call the SendTelegramMessageAsync again here if desired
            // or implement logic to store them for later processing
            await SendTelegramMessageAsync(chatId, message);
        }
    }

    private DateTime? ParsePersianDate(string persianDateString)
    {
        if (string.IsNullOrWhiteSpace(persianDateString))
        {
            Log("Empty or null Persian date string provided.");
            return new DateTime(2000, 1, 1, 12, 0, 0); // Default date (00/00/00 12:00:00)
        }

        // Convert Persian numerals to standard Arabic numerals if necessary
        persianDateString = ConvertPersianNumeralsToArabic(persianDateString);

        // Define a regex pattern for the expected Persian date format (YYYY/MM/DD)
        var regexPattern = @"^(13[0-9]{2}|14[0-0][0-2])/(0[1-9]|1[0-2])/(0[1-9]|[12][0-9]|3[01])$";
        var regex = new Regex(regexPattern);

        // Validate the date string format using regex
        if (!regex.IsMatch(persianDateString))
        {
            Log($"Invalid Persian date format: {persianDateString}");
            return new DateTime(1, 1, 1, 12, 0, 0); // Default date (00/00/00 12:00:00)
        }

        // Split the string into parts
        var parts = persianDateString.Split('/');

        // Parse year, month, and day with additional validation
        if (!int.TryParse(parts[0], out var year) ||
            !int.TryParse(parts[1], out var month) ||
            !int.TryParse(parts[2], out var day))
        {
            Log($"Failed to parse date components from: {persianDateString}");
            return new DateTime(1, 1, 1, 12, 0, 0); // Default date (00/00/00 12:00:00)
        }

        var persianCalendar = new PersianCalendar();
        try
        {
            // Create a DateTime object using the Persian calendar
            var dateTime = persianCalendar.ToDateTime(year, month, day, 0, 0, 0, 0);

            // Return the DateTime object, including default time if not valid
            return dateTime.Date == DateTime.MinValue.Date
                ? new DateTime(1, 1, 1, 12, 0, 0) // Return the default date (00/00/00 12:00:00)
                : dateTime; // Otherwise, return the valid date
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Log($"Date out of range for {persianDateString}: {ex.Message}");
            return new DateTime(1, 1, 1, 12, 0, 0); // Default date (00/00/00 12:00:00)
        }
    }


    private string ConvertPersianNumeralsToArabic(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;

        var persianToArabicMap = new Dictionary<char, char>
        {
            { '۰', '0' }, { '۱', '1' }, { '۲', '2' }, { '۳', '3' }, { '۴', '4' },
            { '۵', '5' }, { '۶', '6' }, { '۷', '7' }, { '۸', '8' }, { '۹', '9' }
        };

        var converted = new StringBuilder(input.Length);
        foreach (var c in input)
            if (persianToArabicMap.TryGetValue(c, out var arabicChar))
                converted.Append(arabicChar);
            else
                converted.Append(c);

        return converted.ToString();
    }


    private async Task LoadUsersFromDatabaseAsync()
    {
        _serverMachineConfigs = new ServerMachineConfigs();
    }

    // Summary: This method imports a list of users into a SQL Server database, ensuring high performance with batch inserts and resilience using Polly.
    //        
    /// <summary>
    ///     It incorporates enhanced stability measures, better error handling, and user notifications through a Telegram bot.
    /// </summary>
    /// <param name="usersToImport"></param>
    /// <param name="botClient"></param>
    /// <param name="chatId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async Task SaveUsersToDatabase(List<User> usersToImport, ITelegramBotClient botClient, long chatId,
        CancellationToken cancellationToken, string path)
    {
        var connectionString = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        // Region 1: Database context setup and Polly policy configuration
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        // Configure a Polly retry policy for transient SQL failures with advanced handling
        var retryPolicy = Policy
            .Handle<SqlException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(
                3, // Limit the number of retry attempts
                attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)), // Exponential back-off
                (exception, duration, attempt, context) =>
                {
                    Log($"Retry {attempt} due to: {exception.Message} (Waiting {duration.TotalSeconds} seconds)");
                }
            );

        // Enhanced performance tracking
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        // Region 2: Main logic for batch processing and data insertion with transaction handling and optimization
        var batchSize = 20000; // Adjust batch size for optimized memory usage and performance
        using var dbContext = new AppDbContext(options);
        using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // Filter out invalid users
            var validUsers = usersToImport.Where(user => IsValidUser(user)).ToList();

            await retryPolicy.ExecuteAsync(async () =>
            {
                for (var i = 0; i < validUsers.Count; i += batchSize)
                {
                    cancellationToken.ThrowIfCancellationRequested(); // Ensure the task can be cancelled if needed

                    var batch = validUsers.Skip(i).Take(batchSize).ToList();

                    // Use SqlBulkCopy for higher efficiency
                    await BulkInsertUsersAsync(batch, connectionString, cancellationToken, path);

                    Log($"Batch of {batch.Count} users imported successfully in {stopwatch.ElapsedMilliseconds} ms.");
                }

                await transaction.CommitAsync(cancellationToken);
            });

            stopwatch.Stop();
            // await NotifyUser(botClient, chatId, $"Data imported successfully in {stopwatch.Elapsed.TotalSeconds} seconds.", cancellationToken);
            Log($"All rows imported successfully. Total time: {stopwatch.Elapsed.TotalSeconds} seconds.");
        }
        catch (OperationCanceledException)
        {
            // Handle task cancellation gracefully
            await transaction.RollbackAsync(cancellationToken);
            await NotifyUser(botClient, chatId, "Import cancelled by user. All changes have been rolled back.",
                cancellationToken);
            Log("Transaction cancelled by user.");
        }
        catch (Exception ex)
        {
            // Region 3: Error handling, notifications, and performance logging
            await transaction.RollbackAsync(cancellationToken);
            await NotifyUser(botClient, chatId,
                "Import failed. All changes have been rolled back. Please check the file format and content.",
                cancellationToken);
            Log($"Transaction failed: {ex.Message} | StackTrace: {ex.StackTrace}");
        }
    }


    private bool IsValidUser(User user)
    {
        return true;
    }

    public async Task BulkInsertUsersAsync(
        List<User> users,
        string connectionString,
        CancellationToken cancellationToken,
        string path)
    {
        if (users == null || users.Count == 0)
        {
            Console.WriteLine("No users to insert. The list is empty.");
            return;
        }

        try
        {
            using var bulkCopy = new SqlBulkCopy(connectionString)
            {
                DestinationTableName = "Users",
                BatchSize = 10000, // Process users in batches for performance
                BulkCopyTimeout = 60 // Timeout in seconds
            };

            var dataTable = CreateUserDataTable();

            // Prepare the data for insertion
            foreach (var user in users)
            {
                var row = dataTable.NewRow();
                PopulateDataRow(row, user, path);
                dataTable.Rows.Add(row);
            }

            // Map DataTable columns to SQL table columns
            MapColumns(bulkCopy);

            // Execute the bulk insert
            await bulkCopy.WriteToServerAsync(dataTable, cancellationToken);
            Console.WriteLine("Bulk insert completed successfully.");
        }
        catch (SqlException sqlEx)
        {
            Console.WriteLine($"SQL Error during bulk insert: {sqlEx.Message}");
            // Handle specific SQL exceptions if needed (e.g., unique constraint violations)
        }
        catch (Exception ex)
        {
            Console.WriteLine($"General Error during bulk insert: {ex.Message}");
        }
    }

    // Create the DataTable schema for Users
    private DataTable CreateUserDataTable()
    {
        var dataTable = new DataTable();
        dataTable.Columns.Add("UserNumberFile", typeof(string));
        dataTable.Columns.Add("UserNameFile", typeof(string));
        dataTable.Columns.Add("UserFamilyFile", typeof(string));
        dataTable.Columns.Add("UserFatherNameFile", typeof(string));
        dataTable.Columns.Add("UserBirthDayFile", typeof(string));
        dataTable.Columns.Add("UserAddressFile", typeof(string));
        dataTable.Columns.Add("UserDescriptionFile", typeof(string));
        dataTable.Columns.Add("UserSourceFile", typeof(string));
        dataTable.Columns.Add("UserTelegramID", typeof(long));
        return dataTable;
    }

    // Populate a DataRow based on the user data and file path
    private void PopulateDataRow(DataRow row, User user, string path)
    {
        // Clean and validate phone number
        var phoneNumber = CleanPhoneNumber(user.UserNumberFile);
        row["UserNumberFile"] = string.IsNullOrEmpty(phoneNumber) ? "Not Found" : phoneNumber;

        // Handle user name, family name, father name, and birth date with "Not Found" for missing data
        row["UserNameFile"] = string.IsNullOrEmpty(user.UserNameFile) ? "Not Found" : user.UserNameFile;
        row["UserFamilyFile"] = string.IsNullOrEmpty(user.UserFamilyFile) ? "Not Found" : user.UserFamilyFile;
        row["UserFatherNameFile"] =
            string.IsNullOrEmpty(user.UserFatherNameFile) ? "Not Found" : user.UserFatherNameFile;
        row["UserBirthDayFile"] = string.IsNullOrEmpty(user.UserBirthDayFile) ? user.UserBirthDayFile : "Not Found";

        // Truncate the address to 50 characters and handle empty address case
        row["UserAddressFile"] = string.IsNullOrEmpty(user.UserAddressFile)
            ? "Not Found"
            : TruncateString(user.UserAddressFile, 50);

        // Provide default value for description
        row["UserDescriptionFile"] =
            string.IsNullOrEmpty(user.UserDescriptionFile) ? "Not Found" : user.UserDescriptionFile;

        // Use the provided path for UserSourceFile
        row["UserSourceFile"] = string.IsNullOrEmpty(path) ? "Not Found" : path;

        // If Telegram ID is null, set it to 0L, otherwise use the provided Telegram ID
        row["UserTelegramID"] = user.UserTelegramID ?? 0L;
    }


    // Ensure all column mappings are added to the SqlBulkCopy instance
    private void MapColumns(SqlBulkCopy bulkCopy)
    {
        bulkCopy.ColumnMappings.Add("UserNumberFile", "UserNumberFile");
        bulkCopy.ColumnMappings.Add("UserNameFile", "UserNameFile");
        bulkCopy.ColumnMappings.Add("UserFamilyFile", "UserFamilyFile");
        bulkCopy.ColumnMappings.Add("UserFatherNameFile", "UserFatherNameFile");
        bulkCopy.ColumnMappings.Add("UserBirthDayFile", "UserBirthDayFile");
        bulkCopy.ColumnMappings.Add("UserAddressFile", "UserAddressFile");
        bulkCopy.ColumnMappings.Add("UserDescriptionFile", "UserDescriptionFile");
        bulkCopy.ColumnMappings.Add("UserSourceFile", "UserSourceFile");
        bulkCopy.ColumnMappings.Add("UserTelegramID", "UserTelegramID");
    }

    // Clean phone numbers to a standardized format
    private string CleanPhoneNumber(string phoneNumber)
    {
        if (string.IsNullOrEmpty(phoneNumber)) return string.Empty;

        if (phoneNumber.StartsWith("98"))
            return "0" + phoneNumber.Substring(2);
        if (!phoneNumber.StartsWith("09"))
            return "0" + phoneNumber;
        return phoneNumber;
    }

    // Truncate string to a specified length, if necessary
    private string TruncateString(string input, int maxLength)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return input.Length > maxLength ? input.Substring(0, maxLength) : input;
    }


    private async Task NotifyUser(ITelegramBotClient botClient, long chatId, string message,
        CancellationToken cancellationToken)
    {
        await botClient.SendMessage(chatId, message, cancellationToken: cancellationToken);
        Log(message); // Log message as well
    }

    #endregion


    #region Button Color Definitions

    public static class ButtonColors
    {
        public static Color LighterShade => Color.FromRgb(94, 0, 255); // Lighter shade for mouse enter
        public static Color OriginalColor => Color.FromRgb(98, 0, 238); // Original color
        public static Color DarkerShade => Color.FromRgb(70, 0, 200); // Darker shade (optional)
        public static Color DisabledColor => Color.FromRgb(150, 150, 150); // Disabled state color
    }

    #region Button Event Handlers

    private void StartBotButton_MouseEnter(object sender, MouseEventArgs e)
    {
        ChangeButtonBackgroundColor(StartBotButton, ButtonColors.LighterShade); // Lighter shade on hover
    }

    private void StartBotButton_MouseLeave(object sender, MouseEventArgs e)
    {
        ChangeButtonBackgroundColor(StartBotButton, ButtonColors.OriginalColor); // Original color
    }

    #endregion

    #region Button Initialization

    private void InitializeButton()
    {
        if (StartBotButton == null)
            throw new InvalidOperationException("StartBotButton is not initialized. Ensure it exists in XAML.");

        StartBotButton.MouseEnter += StartBotButton_MouseEnter;
        StartBotButton.MouseLeave += StartBotButton_MouseLeave;
        StartBotButton.Cursor = Cursors.Hand;
    }

    #endregion

    #region Helper Methods

    /// <summary>
    ///     Changes the background color of the specified button.
    /// </summary>
    /// <param name="button">The button to change the background color of.</param>
    /// <param name="color">The new background color.</param>
    private void ChangeButtonBackgroundColor(Button button, Color color)
    {
        if (button != null) button.Background = new SolidColorBrush(color);
    }

    #endregion

    #endregion
}