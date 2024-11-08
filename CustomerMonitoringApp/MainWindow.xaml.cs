﻿using CustomerMonitoringApp.Application.Services;
using CustomerMonitoringApp.Domain.Interfaces;
using CustomerMonitoringApp.Infrastructure.Data;
using CustomerMonitoringApp.Infrastructure.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using Polly;
using Polly.Retry;
using System.Collections.Concurrent;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input; 
using System.Windows.Media;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Color = System.Windows.Media.Color;
using File = System.IO.File;
using Run = System.Windows.Documents.Run;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using CustomerMonitoringApp.Domain.Entities;
using System.Formats.Asn1;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using User = CustomerMonitoringApp.Domain.Entities.User;
namespace CustomerMonitoringApp.WPFApp
{

    public partial class MainWindow : Window
    {
        public MainWindow() : this(

            App.Services.GetRequiredService<ILogger<MainWindow>>(),
            App.Services.GetRequiredService<ICallHistoryRepository>(),
            App.Services.GetRequiredService<IServiceProvider>(),
            App.Services.GetRequiredService<NotificationService>(),
            App.Services.GetRequiredService<ICallHistoryImportService>())
        {
            LoadUsersFromDatabaseAsync();
            InitializeBotClient();
            InitializeComponent();
            InitializeButton();
        }

        #region Fields and Properties
        private static readonly Regex FileNameRegex = new Regex(@"^(Getcalls|Getrecentcalls|Getlongcalls)_\d{10,}_\d{8}_\d{6}\.csv$", RegexOptions.Compiled);

        private readonly ILogger<MainWindow> _logger;
        private CancellationTokenSource _cancellationTokenSource;
        private ITelegramBotClient _botClient;
        private readonly NotificationService _notificationService;
        private readonly string
            _token = "6768055952:AAGSETUCUC76eXuSoAGX6xcsQk1rrt0K4Ng"; // Replace with your actual bot token
        private readonly ICallHistoryImportService _callHistoryImportService;
        private readonly IServiceProvider _serviceProvider;
        private readonly ICallHistoryRepository _callHistoryRepository;

        #endregion

        #region Constructor

        public MainWindow(
            ILogger<MainWindow> logger,
            ICallHistoryRepository callHistoryRepository,
            IServiceProvider serviceProvider,
            NotificationService notificationService,
            ICallHistoryImportService callHistoryImportService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _callHistoryRepository = callHistoryRepository ?? throw new ArgumentNullException(nameof(callHistoryRepository));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _callHistoryImportService = callHistoryImportService ?? throw new ArgumentNullException(nameof(callHistoryImportService));

        }
     
        /// <summary>
        /// Initializes the bot client and verifies if the token is valid.
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
            catch (Telegram.Bot.Exceptions.ApiRequestException apiEx)
            {
                LogError(apiEx);
                Log($"ApiRequestException: {apiEx.Message} - Code: {apiEx.ErrorCode}");
            }
            catch (System.Net.Http.HttpRequestException httpEx)
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
                {
                    Log("Bot client initialization failed, _botClient is null.");
                }
                else
                {
                    Log("Bot client initialized successfully.");
                }
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
                 StartReceivingUpdates();  // Await the asynchronous method

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
                    updateHandler: HandleUpdateAsync,
                    errorHandler: HandleErrorAsync,
                    receiverOptions: new ReceiverOptions
                    {
                        AllowedUpdates = new[] { UpdateType.Message } // Only receive message updates
                    },
                    cancellationToken: _cancellationTokenSource.Token
                ));

                Log("Bot is now receiving updates.");
            }
            catch (Exception ex)
            {
                Log($"An error occurred while starting to receive updates: {ex.Message}");
            }
        }


        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update,
            CancellationToken cancellationToken)
        {
            var chatId = update.Message?.Chat?.Id;
            var startTime = DateTime.UtcNow; // Start time for processing metrics

            if (chatId == null)
            {
                Log("Error: chatId is null.");
                return;
            }
            // Handle Text Commands for Call History CSV Generation
            if (update.Type == UpdateType.Message && update.Message?.Type == MessageType.Text)
            {
                var messageText = update.Message.Text;
                var commandParts = messageText.Split(' ');
                var command = commandParts[0].ToLower();

                try
                {
                    switch (command)
                    {
                        case "/getcalls":
                            await HandleGetCallsCommand(commandParts, chatId.Value, botClient, cancellationToken);
                            break;
                        case "/getrecentcalls":
                            await HandleGetRecentCallsCommand(commandParts, chatId.Value, botClient, cancellationToken);
                            break;
                        case "/getlongcalls":
                            await HandleGetLongCallsCommand(commandParts, chatId.Value, botClient, cancellationToken);
                            break;
                        case "/getafterhourscalls":
                            await HandleGetAfterHoursCallsCommand(commandParts, chatId.Value, botClient, cancellationToken);
                            break;
                        case "/getfrequentcalldates":
                            await HandleGetFrequentCallDatesCommand(commandParts, chatId.Value, botClient, cancellationToken);
                            break;
                        case "/gettoprecentcalls":
                            await HandleGetTopRecentCallsCommand(commandParts, chatId.Value, botClient, cancellationToken);
                            break;
                        case "/hasrecentcall":
                            await HandleHasRecentCallCommand(commandParts, chatId.Value, botClient, cancellationToken);
                            break;
                        default:
                            await botClient.SendMessage(
                                chatId: chatId.Value,
                                text: "🤔 *Unknown Command*\n\nIt looks like you've entered a command I don't recognize. Please try one of the following supported commands:\n\n" +
                                      "📞 /getcalls - _Retrieve a full history of calls_\n" +
                                      "📅 /getrecentcalls - _Get recent call records_\n" +
                                      "⏳ /getlongcalls - _Find calls longer than a specified duration_\n" +
                                      "🔝 /gettoprecentcalls - _View the top N most recent calls_\n" +
                                      "🕒 /hasrecentcall - _Check if a number was contacted within a time span_\n\n" +
                                      "If you need more help, use `/help` for a full list of commands and details on their usage. 😊",
                                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                                cancellationToken: cancellationToken
                            );
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error handling command '{command}' for user {chatId}: {ex.Message}");
                    await botClient.SendMessage(
                        chatId: chatId.Value,
                        text: $"❌ Error processing command: {ex.Message}",
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

                    if (document != null && Path.GetExtension(document.FileName)
                            .Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
                    {
                        // Inform the user that the file is being processed
                        await botClient.SendMessage(
                            chatId: chatId.Value,
                            text: "📥 Received your .xlsx file! Processing...",
                            cancellationToken: cancellationToken
                        );
                        Log($"Received .xlsx file from user {chatId}: {document.FileName}");

                        // Step 1: Download the file
                        string filePath;
                        try
                        {
                            filePath = await DownloadFileAsync(document, cancellationToken);
                            Log($"File downloaded successfully: {filePath}");
                            await botClient.SendMessage(
                                chatId: chatId.Value,
                                text: "🧲 File downloaded successfully",
                                cancellationToken: cancellationToken
                            );
                        }
                        catch (Exception downloadEx)
                        {
                            Log($"Error downloading file from user {chatId}: {downloadEx.Message}");
                            await botClient.SendMessage(
                                chatId: chatId.Value,
                                text: "❌ Error downloading the file. Please try again.",
                                cancellationToken: cancellationToken
                            );
                            return;
                        }
       
                        // Step 3: Identify file type (CallHistory or UsersUpdate)
                        bool isCallHistory = await IsCallHistoryFileAsync(filePath);
                        bool isUserFile = await IsUserFile(filePath);
                        // 2. Add the parsed data to the repository (saving to database)

                        if (isCallHistory)
                        {
                            try
                            {


                                if (botClient == null)
                                {
                                    throw new InvalidOperationException("botClient is not initialized.");
                                }

                                if (!chatId.HasValue)
                                {
                                    throw new ArgumentNullException(nameof(chatId), "chatId is not provided.");
                                }


                                // Try processing the CallHistory file
                                await _callHistoryImportService.ProcessExcelFileAsync(filePath);
                                Log($"Successfully processed CallHistory data: {filePath}");

                                await botClient.SendMessage(
                                    chatId: chatId.Value,
                                    text: "✅ File processed and data imported successfully!",
                                    cancellationToken: cancellationToken
                                );
                            }
                            catch (ArgumentException argEx)
                            {
                                Log($"Invalid argument error: {argEx.Message}");
                                await botClient.SendMessage(
                                    chatId: chatId.Value,
                                    text: $"❌ Invalid file or input. Error: {argEx.Message}",
                                    cancellationToken: cancellationToken
                                );
                            }
                            catch (Exception ex)
                            {
                                Log($"General error processing CallHistory data: {ex.Message}");
                                await botClient.SendMessage(
                                    chatId: chatId.Value,
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
                                    chatId: chatId.Value,
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
                                chatId: chatId.Value,
                                text: "⚠️ The file type is not supported. Please upload a valid .xlsx file.",
                                cancellationToken: cancellationToken
                            );
                            Log($"User {chatId} uploaded an unsupported file type: {document?.FileName}");
                            return;
                        }

                        // Step 4: Inform the user of success and provide summary
                        await botClient.SendMessage(
                            chatId: chatId.Value,
                            text: "✅ File processed and data imported successfully!",
                            cancellationToken: cancellationToken
                        );
                        Log($"User {chatId} was informed of successful processing.");
                    }
                    else
                    {
                        // Handle incorrect file types with detailed feedback
                        await botClient.SendMessage(
                            chatId: chatId.Value,
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
                {
                    await botClient.SendMessage(
                        chatId: chatId.Value,
                        text: "❌ An unexpected error occurred while processing your file. Please try again later.",
                        cancellationToken: cancellationToken
                    );
                }
            }
            finally
            {
                // Calculate processing duration and log for performance analysis
                var duration = DateTime.UtcNow - startTime;
                Log($"Completed file processing for user {chatId} in {duration.TotalSeconds} seconds.");
            }

            // Handle CallbackQuery if applicable
            if (update.Type == UpdateType.CallbackQuery)
            {
                await HandleCallbackQueryAsync(botClient, update.CallbackQuery, cancellationToken);
                return; // Exit early after handling the callback
            }
        }
        private async Task HandleHasRecentCallCommand(string[] commandParts, long chatId, ITelegramBotClient botClient, CancellationToken cancellationToken)
        {
            // Ensure the user provided a valid phone number and a time span
            if (commandParts.Length < 3 || !TimeSpan.TryParse(commandParts[2], out TimeSpan timeSpan))
            {
                await botClient.SendMessage(chatId, "Usage: /hasrecentcall [phoneNumber] [timeSpan]", cancellationToken: cancellationToken);
                return;
            }

            var phoneNumber = commandParts[1];

            // Check if the phone number has a recent call within the specified time window
            var hasRecentCall = await _callHistoryRepository.HasRecentCallWithinTimeSpanAsync(phoneNumber, timeSpan);

            // Send feedback to the user
            var message = hasRecentCall
                ? $"✅ The phone number {phoneNumber} has had a recent call within the last {timeSpan.TotalMinutes} minutes."
                : $"❌ No recent calls found for {phoneNumber} within the last {timeSpan.TotalMinutes} minutes.";

            await botClient.SendMessage(chatId, message, cancellationToken: cancellationToken);
        }

        private async Task HandleGetTopRecentCallsCommand(string[] commandParts, long chatId, ITelegramBotClient botClient, CancellationToken cancellationToken)
        {
            // Ensure the user provided a phone number and the number of rows (calls)
            if (commandParts.Length < 3 || !int.TryParse(commandParts[2], out int topN))
            {
                await botClient.SendMessage(chatId, "Usage: /gettoprecentcalls [phoneNumber] [numberOfCalls]", cancellationToken: cancellationToken);
                return;
            }

            var phoneNumber = commandParts[1];

            // Get the top N most recent calls for the phone number
            var topRecentCalls = await _callHistoryRepository.GetTopNRecentCallsAsync(phoneNumber, topN);

            // If no calls are found, send a message to the user
            if (!topRecentCalls.Any())
            {
                await botClient.SendMessage(chatId, $"No recent calls found for {phoneNumber}.", cancellationToken: cancellationToken);
                return;
            }

            // Generate a CSV file for the top recent calls
            var csvFilePath = GenerateCsv(topRecentCalls);

            // Send the CSV file to the user
            using var stream = new FileStream(csvFilePath, FileMode.Open, FileAccess.Read);
            await botClient.SendDocument(chatId, new InputFileStream(stream, "TopRecentCalls.csv"), cancellationToken: cancellationToken);

            // Clean up by deleting the file after sending
            File.Delete(csvFilePath);
        }


        private async Task HandleGetAfterHoursCallsCommand(string[] commandParts, long chatId, ITelegramBotClient botClient, CancellationToken cancellationToken)
        {
            if (commandParts.Length < 4)
            {
                await botClient.SendMessage(chatId, "Please provide a phone number, start time, and end time. Example: /getafterhourscalls +1234567890 18:00 06:00", cancellationToken: cancellationToken);
                return;
            }

            var phoneNumber = commandParts[1];
            var startTime = TimeSpan.Parse(commandParts[2]);
            var endTime = TimeSpan.Parse(commandParts[3]);

            var afterHoursCalls = await _callHistoryRepository.GetAfterHoursCallsByPhoneNumberAsync(phoneNumber, startTime, endTime);

            // Convert to CSV format and send
            var csvContent = ConvertToCsv(afterHoursCalls);
            await SendCsvAsync(csvContent, chatId, botClient, cancellationToken);
        }
        private string ConvertToCsv<T>(IEnumerable<T> items)
        {
            using (var writer = new StringWriter())
            using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)))
            {
                csv.WriteRecords(items);
                return writer.ToString();
            }
        }

        private async Task SendCsvAsync(string csvContent, long chatId, ITelegramBotClient botClient, CancellationToken cancellationToken)
        {
            var csvBytes = Encoding.UTF8.GetBytes(csvContent);
            using (var stream = new MemoryStream(csvBytes))
            {
                var inputFile = new InputFileStream(stream, FileNameRegex.ToString());
                await botClient.SendDocument(
                    chatId: chatId,
                    document: inputFile,
                    caption: "Here is your requested data.",
                    cancellationToken: cancellationToken
                );
            }
        }


        private async Task HandleGetFrequentCallDatesCommand(string[] commandParts, long chatId, ITelegramBotClient botClient, CancellationToken cancellationToken)
        {
            if (commandParts.Length < 2)
            {
                await botClient.SendMessage(chatId, "Please provide a phone number. Example: /getfrequentcalldates +1234567890", cancellationToken: cancellationToken);
                return;
            }

            var phoneNumber = commandParts[1];
            var frequentCallDates = await _callHistoryRepository.GetFrequentCallDatesByPhoneNumberAsync(phoneNumber);

            // Convert to CSV format and send
            var csvContent = ConvertToCsv(frequentCallDates);
            await SendCsvAsync(csvContent, chatId, botClient, cancellationToken);
        }


        private async Task HandleGetCallsCommand(string[] commandParts, long chatId, ITelegramBotClient botClient, CancellationToken cancellationToken)
        {
            if (commandParts.Length < 2)
            {
                await botClient.SendMessage(chatId, "Usage: /getcalls [phoneNumber]", cancellationToken: cancellationToken);
                return;
            }

            var phoneNumber = commandParts[1];
            var callHistories = await _callHistoryRepository.GetCallsByPhoneNumberAsync(phoneNumber);

            if (!callHistories.Any())
            {
                await botClient.SendMessage(chatId, "No call history found for this phone number.", cancellationToken: cancellationToken);
                return;
            }

            var csvFilePath = GenerateCsv(callHistories);
            using var stream = new FileStream(csvFilePath, FileMode.Open, FileAccess.Read);
            await botClient.SendDocument(chatId, new InputFileStream(stream, FileNameRegex.ToString()), cancellationToken: cancellationToken);

            File.Delete(csvFilePath);
        }



        private async Task HandleGetRecentCallsCommand(string[] commandParts, long chatId, ITelegramBotClient botClient, CancellationToken cancellationToken)
        {
            if (commandParts.Length < 3 || !int.TryParse(commandParts[2], out int days))
            {
                await botClient.SendMessage(chatId, "Usage: /getrecentcalls [phoneNumber] [days]", cancellationToken: cancellationToken);
                return;
            }

            var phoneNumber = commandParts[1];
            var startDate = DateTime.UtcNow.AddDays(-days);
            var recentCalls = await _callHistoryRepository.GetRecentCallsByPhoneNumberAsync(phoneNumber, startDate);

            if (!recentCalls.Any())
            {
                await botClient.SendMessage(chatId, "No recent calls found.", cancellationToken: cancellationToken);
                return;
            }

            var csvFilePath = GenerateCsv(recentCalls);
            using var stream = new FileStream(csvFilePath, FileMode.Open, FileAccess.Read);
            await botClient.SendDocument(chatId, new InputFileStream(stream, FileNameRegex.ToString()), cancellationToken: cancellationToken);

            File.Delete(csvFilePath);
        }


        private async Task HandleGetLongCallsCommand(string[] commandParts, long chatId, ITelegramBotClient botClient, CancellationToken cancellationToken)
        {
            if (commandParts.Length < 3 || !int.TryParse(commandParts[2], out int minimumDuration))
            {
                await botClient.SendMessage(chatId, "Usage: /getlongcalls [phoneNumber] [minimumDurationInSeconds]", cancellationToken: cancellationToken);
                return;
            }

            var phoneNumber = commandParts[1];
            var longCalls = await _callHistoryRepository.GetLongCallsByPhoneNumberAsync(phoneNumber, minimumDuration);

            if (!longCalls.Any())
            {
                await botClient.SendMessage(chatId, "No long calls found.", cancellationToken: cancellationToken);
                return;
            }

            var csvFilePath = GenerateCsv(longCalls);
            using var stream = new FileStream(csvFilePath, FileMode.Open, FileAccess.Read);
            await botClient.SendDocument(chatId, new InputFileStream(stream, FileNameRegex.ToString()), cancellationToken: cancellationToken);

            File.Delete(csvFilePath);
        }


        private string GenerateCsv(IEnumerable<CallHistory> callHistories)
        {
            var filePath = Path.Combine(Path.GetTempPath(), $"CallHistory_{Guid.NewGuid()}.csv");

            using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

            csv.WriteRecords(callHistories);
            writer.Flush();

            return filePath;
        }


        private async Task<bool> IsCallHistoryFileAsync(string filePath)
        {
            // Check if the file follows the expected structure for a CallHistory file
            try
            {
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
                    var rowCount = worksheet.Dimension?.Rows ?? 0;
                    var columnCount = worksheet.Dimension?.Columns ?? 0;

                    // Check if the file has enough columns (6 expected)
                    if (columnCount < 6)
                    {
                        Log("Error: The file has fewer than 6 columns.");
                        return false;
                    }

                    // Expected headers for CallHistory file
                    var expectedHeaders = new List<string>
            {
                "شماره مبدا",  // Source Phone
                "شماره مقصد",  // Destination Phone
                "تاریخ",        // Date
                "ساعت",         // Time
                "مدت",          // Duration
                "نوع تماس"      // Call Type
            };

                    // Validate headers
                    for (int col = 1; col <= columnCount; col++)
                    {
                        var header = worksheet.Cells[1, col].Text.Trim(); // Trim any extra spaces from headers

                        // Check if any header matches the expected ones
                        if (!expectedHeaders.Contains(header))
                        {
                            Log($"Error: Invalid header '{header}' at column {col}.");
                            return false;
                        }
                    }

                    // Check if there are enough rows of data (at least 2 rows: 1 header + 1 data row)
                    if (rowCount < 2)
                    {
                        Log("Error: File contains too few rows (must be at least 2).");
                        return false;
                    }

                    // Counter for valid rows
                    int validRowCount = 0;

                    // Validate each data row
                    for (int row = 2; row <= rowCount; row++)
                    {
                        var sourcePhone = worksheet.Cells[row, 1].Text.Trim();  // Column A: "شماره مبدا"
                        var destinationPhone = worksheet.Cells[row, 2].Text.Trim(); // Column B: "شماره مقصد"
                        var date = worksheet.Cells[row, 3].Text.Trim(); // Column C: "تاریخ"
                        var time = worksheet.Cells[row, 4].Text.Trim(); // Column D: "ساعت"
                        var durationText = worksheet.Cells[row, 5].Text.Trim(); // Column E: "مدت"
                        var callType = worksheet.Cells[row, 6].Text.Trim(); // Column F: "نوع تماس"

                        // Skip row if phone numbers are not numeric
                        if (!IsNumeric(sourcePhone) || !IsNumeric(destinationPhone))
                        {
                            Log($"INFO: Skipping row {row} due to non-numeric phone numbers. Source Phone: '{sourcePhone}', Destination Phone: '{destinationPhone}'");
                            continue;  // Skip this row and move to the next one
                        }

                        // Validate if essential data is available and non-empty
                        if (string.IsNullOrWhiteSpace(sourcePhone) || string.IsNullOrWhiteSpace(destinationPhone) ||
                            string.IsNullOrWhiteSpace(date) || string.IsNullOrWhiteSpace(time) ||
                            string.IsNullOrWhiteSpace(durationText) || string.IsNullOrWhiteSpace(callType))
                        {
                            Log($"Error: Row {row} contains missing data.");
                            continue;  // Skip this row and move to the next one
                        }

                        // Validate the format of Date (yyyy/MM/dd) and Duration (integer)
                        if (!DateTime.TryParseExact(date, "yyyy/MM/dd", null, System.Globalization.DateTimeStyles.None, out _) ||
                            !int.TryParse(durationText, out _))
                        {
                            Log($"Error: Invalid data format in row {row}. Date: '{date}', Duration: '{durationText}'");
                            continue;  // Skip this row and move to the next one
                        }

                        // Increment valid row counter
                        validRowCount++;

                        // If we have more than 30 valid rows, return true
                        if (validRowCount > 30)
                        {
                            return true;
                        }
                    }

                    // If we don't have more than 30 valid rows, return false
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"Error checking file type: {ex.Message}");
                return false;  // If an error occurs, assume the file is not a valid CallHistory file
            }
        }


        // Helper method to check if a string contains only numeric characters
        private bool IsNumeric(string input)
        {
            return input.All(c => Char.IsDigit(c));
        }



        private async Task<bool> IsUserFile(string filePath)
        {
            try
            {
                using (var package = new ExcelPackage(new FileInfo(filePath)))
                {
                    // Ensure that there is at least one worksheet
                    var worksheet = package.Workbook.Worksheets.FirstOrDefault();
                    if (worksheet == null)
                    {
                        Log("Error: No worksheets found in the file.");
                        return false;
                    }

                    // Retrieve row and column counts
                    var rowCount = worksheet.Dimension?.Rows ?? 0;
                    var columnCount = worksheet.Dimension?.Columns ?? 0;

                   

                    // Expected headers for a User file
                    var expectedHeaders = new List<string>
            {
                "شماره تلفن",    // Phone Number
                "نام",           // First Name
                "نام خانوادگی",  // Last Name
                "نام پدر",       // Father's Name
                "تاریخ تولد",    // Date of Birth
                "نشانی"         // Address
            };

                  

                    // Check if there are enough rows of data (at least 2 rows: 1 header + 1 data row)
                    if (rowCount < 2)
                    {
                        Log("Error: File contains too few rows (must be at least 2).");
                        return false;
                    }

                    // Validate each data row
                    for (int row = 2; row <= 5; row++)
                    {
                        var phoneNumber = worksheet.Cells[row, 1].Text.Trim();  // Column A: "شماره تلفن"
                        var firstName = worksheet.Cells[row, 2].Text.Trim();    // Column B: "نام"
                        var lastName = worksheet.Cells[row, 3].Text.Trim();     // Column C: "نام خانوادگی"
                        var fatherName = worksheet.Cells[row, 4].Text.Trim();   // Column D: "نام پدر"
                        var birthDate = worksheet.Cells[row, 5].Text.Trim();    // Column E: "تاریخ تولد"
                        var address = worksheet.Cells[row, 6].Text.Trim();     // Column F: "نشانی"

                        // Validate if essential data is available and non-empty
                        if (string.IsNullOrWhiteSpace(phoneNumber) || string.IsNullOrWhiteSpace(firstName) ||
                            string.IsNullOrWhiteSpace(lastName) || string.IsNullOrWhiteSpace(fatherName) ||
                            string.IsNullOrWhiteSpace(birthDate) || string.IsNullOrWhiteSpace(address))
                        {
                            Log($"Error: Row {row} contains missing data.");
                            return false;  // Incomplete row, invalid file
                        }

                        // Validate the format of Phone Number (e.g., check if it starts with "98" and has the correct number of digits)
                        if (!IsValidPhoneNumber(phoneNumber))
                        {
                            Log($"Error: Invalid phone number format in row {row}. Phone Number: '{phoneNumber}'");
                            return false; // Invalid phone number format
                        }
                   
                    }

                    // If we have passed all checks, return true
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log($"Error checking file type: {ex.Message}");
                return false;  // If an error occurs, assume the file is not a valid User file
            }
        }

        // Helper method to validate Phone Number
        private bool IsValidPhoneNumber(string phoneNumber)
        {
            return phoneNumber.StartsWith("98") && phoneNumber.Length == 12 && phoneNumber.All(char.IsDigit);
        }
    




        // Async method to handle errors during polling
        private async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
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
                    Log($"Request Parameters: {ex.Parameters?.ToString()}");
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
                int retryDelay = 5000; // Initial delay in milliseconds
                int maxRetries = 5; // Maximum number of retries

                for (int attempt = 1; attempt <= maxRetries; attempt++)
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
        /// Handles any exceptions that occur during the bot startup process.
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
        /// Determines if the exception is recoverable.
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
        /// Attempts to recover from an error that occurred during startup.
        /// </summary>
        private void AttemptRecovery()
        {
            // Implement recovery logic here, such as re-initializing components or notifying the user
            Log("Recovery logic is executed, components are being re-initialized.");
            // E.g., reinitialize components, reset states, etc.
        }



        #endregion

        #region Logging





        /// <summary>
        /// Logs a message with a specified log level.
        /// </summary>
        /// <param name="message">The log message to log.</param>
        /// <param name="logLevel">The level of logging (INFO, WARNING, ERROR).</param>
        private void Log(string message, string logLevel = "INFO")
        {
            // Define color and font weight based on log level
            System.Windows.Media.Color color;
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
            string logMessage = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {logLevel}: {message}";

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
                AppendLogToRichTextBox($"Failed to log to file: {ex.Message}", Colors.Red, 14, fontWeight: FontWeights.Bold);
            }
            finally
            {
                _botClient.SendMessage(
                    chatId: -1002344133590,
                    text: message,
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown);
            }
        }


        // Adds error details to the UI and writes them to a file
        private void LogError(Exception ex)
        {
            string errorLog = $"[{DateTime.UtcNow}] ERROR: {ex.Message}\nStack Trace: {ex.StackTrace}";
            AppendLogToRichTextBox(errorLog, System.Windows.Media.Colors.Red, 16); // Specify namespace
            LogToFile(errorLog);
        }

        private const long MaxLogFileSize = 10 * 1024 * 1024; // 10 MB

        /// <summary>
        /// Persists log messages to a file with structured logging and error handling.
        /// </summary>
        /// <param name="message">The log message to be saved.</param>
        private async Task LogToFile(string message)
        {
            // Create the log file path
            string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            string logFilePath = Path.Combine(logDirectory, "error_log.txt");

            // Ensure the directory exists
            Directory.CreateDirectory(logDirectory);

            try
            {
                // Check if the log file exists and its size
                if (File.Exists(logFilePath) && new FileInfo(logFilePath).Length >= MaxLogFileSize)
                {
                    // If the file size exceeds the limit, rename it for rotation
                    string newLogFilePath = Path.Combine(logDirectory, $"error_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                    File.Move(logFilePath, newLogFilePath);
                }

                // Prepare the log message with a timestamp
                string logMessage = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} - {message}";

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




        private bool _isUpdating = false; // Flag to prevent recursive calls

        private async void AppendLogToRichTextBox(
            string message,
            System.Windows.Media.Color defaultColor,
            double defaultFontSize,
            string fontFamily = "Segoe UI",
            FontWeight? fontWeight = null)
        {
            // Ensure thread-safe UI operations
            if (!Dispatcher.CheckAccess())
            {
                // Use async invoke to ensure UI updates are safe
                await Dispatcher.InvokeAsync(() => AppendLogToRichTextBox(message, defaultColor, defaultFontSize, fontFamily, fontWeight));
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
                    foreach (var run in runs)
                    {
                        paragraph.Inlines.Add(run);
                    }

                    // Append the paragraph to the RichTextBox's document blocks
                    richTextBox.Document.Blocks.Add(paragraph);

                    // Auto-scroll to the bottom after adding new content
                    richTextBox.ScrollToEnd();
                }
                else
                {
                    // If LogTextBox isn't a RichTextBox, show a type mismatch error
                    string errorMessage = "LogTextBox must be a RichTextBox for formatted logging.";
                    LogError(new Exception(errorMessage)); // Log error if type mismatch occurs
                    MessageBox.Show(errorMessage, "Type Mismatch", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                // Catch any exception that occurs while appending and handle it properly
                string errorMessage = $"An error occurred while appending to the log: {ex.Message}";
                LogError(new Exception(errorMessage)); // Log error to file or external system
                MessageBox.Show(errorMessage, "Error", MessageBoxButton.OK, MessageBoxImage.Error); // Show error to the user
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
    
    int lastIndex = 0;

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

        #region Bot Handlers



        public async Task<string> DownloadFileAsync(Document document, CancellationToken cancellationToken)
        {
            if (document == null || string.IsNullOrEmpty(document.FileId))
            {
                Log("Invalid document provided.");
                throw new ArgumentNullException(nameof(document), "Document or FileId is null or empty.");
            }

            var filePath = Path.Combine(Environment.CurrentDirectory, $"{document.FileId}{Path.GetExtension(document.FileName)}");

            // Define a retry policy with exponential back-off
            AsyncRetryPolicy retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    onRetry: (exception, duration, attempt, context) =>
                    {
                        Log($"Retry {attempt} due to: {exception.Message}");
                    }
                );

            try
            {
                return await retryPolicy.ExecuteAsync(async () =>
                {
                    using (var saveFileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        try
                        {
                            Log($"Starting download of file {document.FileName} with FileId {document.FileId}.");
                            var file = await _botClient.GetFile(document.FileId, cancellationToken);
                            await _botClient.DownloadFile(file.FilePath, saveFileStream, cancellationToken);

                            Log($"File {document.FileName} downloaded successfully to {filePath}.");
                            return filePath;
                        }
                        catch (Exception ex)
                        {
                            Log($"Failed to download file {document.FileName}: {ex.Message}");
                            throw; // Rethrow to trigger the retry policy
                        }
                    }
                });
            }
            catch (Exception ex) when (ex is OperationCanceledException)
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




        #region Excel File Import




        private async Task<bool> GetConfirmationResponse(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            // Temporary storage for user responses
            var userResponses = new ConcurrentDictionary<long, string>();

            // Create an inline keyboard with buttons
            var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("✅ Confirm", "confirm_import"),
                    InlineKeyboardButton.WithCallbackData("❌ Cancel", "cancel_import")
                }
            });

            // Send a message to prompt for confirmation
            await botClient.SendMessage(chatId, "Do you confirm the action?", replyMarkup: inlineKeyboard, cancellationToken: cancellationToken);

            // Wait for the user's response
            while (!userResponses.ContainsKey(chatId) && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(100); // Small delay to avoid busy waiting
            }

            // Check if a response was received
            if (userResponses.TryRemove(chatId, out var response))
            {
                // Confirm the response
                return response == "confirm_import";
            }

            return false; // No response or cancelled
        }





        private async Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            // Store the user response
            var userResponses = new ConcurrentDictionary<long, string>();

            // Store the response in the dictionary
            userResponses[callbackQuery.From.Id] = callbackQuery.Data;

            // Answer the callback query to acknowledge the button press
            await botClient.AnswerCallbackQuery(callbackQuery.Id, "Received your choice!", cancellationToken: cancellationToken);

            // Process the callback query based on the data
            switch (callbackQuery.Data)
            {
                case "confirm_import":
                    await botClient.SendMessage(callbackQuery.From.Id, "You have confirmed the import.", cancellationToken: cancellationToken);
                    break;
                case "cancel_import":
                    await botClient.SendMessage(callbackQuery.From.Id, "You have cancelled the import.", cancellationToken: cancellationToken);
                    break;
                default:
                    await botClient.SendMessage(callbackQuery.From.Id, "Unknown option selected.", cancellationToken: cancellationToken);
                    break;
            }
        }





        private async Task ImportExcelToDatabase(string filePath, ITelegramBotClient botClient, long chatId,
     CancellationToken cancellationToken)
        {
            var usersToImport = new List<User>();
            var errors = new List<string>(); // To collect error messages
            const int BatchSize = 100; // Define a batch size for processing
            var processedRows = 0; // Track the number of processed rows
            var startTime = DateTime.Now; // Track the start time of the operation

            try
            {
                // Validate file existence
                if (!File.Exists(filePath))
                {
                    await NotifyUser(botClient, chatId, "Error: The specified Excel file does not exist. Please send the file again.",
                        cancellationToken);
                    return;
                }

                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length == 0)
                {
                    await NotifyUser(botClient, chatId, "Error: The specified Excel file is empty. Please send the file again.", cancellationToken);
                    return;
                }

                Log($"File exists and is not empty. Size: {fileInfo.Length} bytes.");

                // Initialize Excel package
                using var package = new ExcelPackage(new FileInfo(filePath));
                var worksheet = package.Workbook.Worksheets.FirstOrDefault();
                if (worksheet == null)
                {
                    await NotifyUser(botClient, chatId, "Error: No worksheet found in the Excel file. Please send the file again.",
                        cancellationToken);
                    return;
                }

                Log($"Worksheet found: {worksheet.Name}");

                // Check if worksheet is valid
                if (worksheet.Dimension == null || worksheet.Dimension.Rows == 0)
                {
                    await NotifyUser(botClient, chatId, "Error: The worksheet is empty. Please send the file again.", cancellationToken);
                    return;
                }

                Log($"Worksheet is valid with {worksheet.Dimension.Rows} rows.");

                // Loop through rows and process each user
                int rowCount = worksheet.Dimension.Rows;
                for (int row = 2; row <= rowCount; row++)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    try
                    {
                        // Create user from the current row
                        var user = CreateUserFromRow(worksheet, row, chatId, errors);
                        if (user != null)
                        {
                            usersToImport.Add(user);
                        }

                        processedRows++;
                        if (processedRows % 10 == 0)
                        {
                            Log($"Processed {processedRows} rows.");
                        }

                        // Save users in batches
                        if (usersToImport.Count >= BatchSize)
                        {
                            await SaveUsersToDatabase(usersToImport, botClient, chatId, cancellationToken);
                            usersToImport.Clear();
                        }

                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Row {row} error: {ex.Message}");
                        Log($"Row {row} error: {ex.Message}");
                    }
                }

                // Save remaining users
                if (usersToImport.Count > 0)
                {
                    await SaveUsersToDatabase(usersToImport, botClient, chatId, cancellationToken);
                }

                // Notify user
                if (errors.Count > 0)
                {
                    string errorMessage = string.Join("\n", errors);
                    await NotifyUser(botClient, chatId,
                        $"Some users were not imported due to the following errors:\n{errorMessage}\nPlease send the file again.",
                        cancellationToken);
                }
                else
                {
                    await NotifyUser(botClient, chatId, $"Successfully imported {rowCount - 1} users.", cancellationToken);
                }

                var endTime = DateTime.Now;
                var duration = endTime - startTime;
                Log($"Import completed in {duration.TotalSeconds} seconds.");
            }
            catch (Exception ex)
            {
                Log($"Unexpected Error: {ex.Message}");
                await NotifyUser(botClient, chatId,
                    "❌ Oops! An unexpected error occurred. Please try again later or contact support. Please send the file again.",
                    cancellationToken);
            }
        }



        #region User Creation and Update Methods

        // In-memory list to simulate database or user storage
        private List<CustomerMonitoringApp.Domain.Entities.User> _users = new List<CustomerMonitoringApp.Domain.Entities.User>();


        private CustomerMonitoringApp.Domain.Entities.User CreateUserFromRow(ExcelWorksheet worksheet, int row, long chatId, List<string> errors)
        {
            // Retrieve and trim values from the worksheet
            var userNumber = worksheet.Cells[row, 1].Text.Trim();
            var userName = worksheet.Cells[row, 2].Text.Trim();
            var userFamily = worksheet.Cells[row, 3].Text.Trim();
            var userFatherName = worksheet.Cells[row, 4].Text.Trim();
            var userBirthDayString = worksheet.Cells[row, 5].Text.Trim();
            var userAddress = worksheet.Cells[row, 6].Text.Trim();
            var userDescription = worksheet.Cells[row, 7].Text.Trim();
            var userSource = worksheet.Cells[row, 8].Text.Trim();

            // Set a default date for userBirthDay
            DateTime userBirthDay = new DateTime(2000, 1, 1);
            DateTime? parsedBirthDay = ParsePersianDate(userBirthDayString);
            if (parsedBirthDay.HasValue)
            {
                userBirthDay = parsedBirthDay.Value;
            }
            else
            {
                errors.Add($"Row {row}: Empty or invalid Persian date string provided. Using default date {userBirthDay.ToShortDateString()}.");
            }

            // Retrieve all users matching the given phone number
            var matchingUsers = FindUsersByPhoneNumber(userNumber);

            if (matchingUsers.Count > 1)
            {
                // Log error if multiple users are found with the same phone number
                errors.Add($"Row {row} error: More than one user with the phone number {userNumber} exists.");
                return null;
            }
            else if (matchingUsers.Count == 1)
            {
                // Update the existing user if found
                return UpdateExistingUser(matchingUsers[0], userName, userFamily, userFatherName, userBirthDay, userAddress, userDescription, userSource, chatId, row);
            }
            else
            {
                // Validate required fields for a new user
                if (string.IsNullOrWhiteSpace(userNumber) || string.IsNullOrWhiteSpace(userName))
                {
                    errors.Add($"Row {row}: Invalid user data - UserNumber or UserName is empty.");
                    return null;
                }

                // Create and add a new user if no match is found
                return CreateNewUser(userNumber, userName, userFamily, userFatherName, userBirthDay, userAddress, userDescription, userSource, chatId, row);
            }
        }

        // Helper method to update an existing user
        private CustomerMonitoringApp.Domain.Entities.User UpdateExistingUser(
            CustomerMonitoringApp.Domain.Entities.User existingUser,
            string userName, string userFamily, string userFatherName, DateTime userBirthDay,
            string userAddress, string userDescription, string userSource, long chatId, int row)
        {
            existingUser.UserNameFile = userName;
            existingUser.UserFamilyFile = userFamily;
            existingUser.UserFatherNameFile = userFatherName;
            existingUser.UserBirthDayFile = userBirthDay;
            existingUser.UserAddressFile = userAddress;
            existingUser.UserDescriptionFile = userDescription;
            existingUser.UserSourceFile = userSource;
            existingUser.UserTelegramID = (int)chatId;

            Log($"Updated user with UserNumber: {existingUser.UserNumberFile} in row {row}.");
            return existingUser;
        }

        // Helper method to create a new user
        private CustomerMonitoringApp.Domain.Entities.User CreateNewUser(
            string userNumber, string userName, string userFamily, string userFatherName, DateTime userBirthDay,
            string userAddress, string userDescription, string userSource, long chatId, int row)
        {
            var newUser = new CustomerMonitoringApp.Domain.Entities.User
            {
                UserNumberFile = userNumber,
                UserNameFile = userName,
                UserFamilyFile = userFamily,
                UserFatherNameFile = userFatherName,
                UserBirthDayFile = userBirthDay,
                UserAddressFile = userAddress,
                UserDescriptionFile = userDescription,
                UserSourceFile = userSource,
                UserTelegramID = (int)chatId
            };

            _users.Add(newUser);
            Log($"Created new user from row {row}: UserNumber: {userNumber}, UserName: {userName}.");
            return newUser;
        }


        // Helper method to find users by phone number
        private List<CustomerMonitoringApp.Domain.Entities.User> FindUsersByPhoneNumber(string phoneNumber)
        {
            // Normalize phone number format (if necessary)
            phoneNumber = NormalizePhoneNumber(phoneNumber);

            // Log the search operation
            Log($"Searching for user with PhoneNumber: {phoneNumber}.");

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
                .Replace(" ", "")        // Remove spaces
                .Replace("-", "")        // Remove dashes
                .Replace("(", "")        // Remove opening parentheses
                .Replace(")", "");       // Remove closing parentheses

            // You can add more normalization rules as needed

            return normalizedNumber;
        }




        /// <summary>
        /// Sends a message to the specified chat ID using the Telegram bot client.
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
                .Handle<ApiRequestException>(ex => ex.Message.Contains("can't parse entities") || ex.Message.Contains("too many requests"))
                .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // Exponential backoff
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
                        chatId: chatId,
                        text: message,
                        parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown // Use MarkdownV2 for rich formatting
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
        /// Handles specific errors by implementing custom logic.
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
        /// Processes the list of failed messages to retry sending or log them for analysis.
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
            if (!int.TryParse(parts[0], out int year) ||
                !int.TryParse(parts[1], out int month) ||
                !int.TryParse(parts[2], out int day))
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
            if (string.IsNullOrWhiteSpace(input))
            {
                return input;
            }

            var persianToArabicMap = new Dictionary<char, char>
            {
                {'۰', '0'}, {'۱', '1'}, {'۲', '2'}, {'۳', '3'}, {'۴', '4'},
                {'۵', '5'}, {'۶', '6'}, {'۷', '7'}, {'۸', '8'}, {'۹', '9'}
            };

            var converted = new StringBuilder(input.Length);
            foreach (char c in input)
            {
                if (persianToArabicMap.TryGetValue(c, out char arabicChar))
                {
                    converted.Append(arabicChar);
                }
                else
                {
                    converted.Append(c);
                }
            }

            return converted.ToString();
        }


        private async Task LoadUsersFromDatabaseAsync()
        {
            try
            {
                // Create a new instance of your DbContext
                var options = new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlServer(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString)
                    .Options;

                using (var dbContext = new AppDbContext(options))
                {
                    // Load users asynchronously
                    _users = await dbContext.Users.ToListAsync();

                    // Bind the loaded users to the ListView
                    //UserListView.ItemsSource = _users;
                }
            }
            catch (Exception ex)
            {
            //    Log($"Error loading users from database: {ex.Message}");
             //   MessageBox.Show($"Failed to load users: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Summary: This method imports a list of users into a SQL Server database, ensuring high performance with batch inserts and resilience using Polly.
        //          It incorporates enhanced stability measures, better error handling, and user notifications through a Telegram bot.
        //
        // Regions:
        // 1. Database context setup and Polly policy configuration
        // 2. Main logic for batch processing and data insertion with transaction handling and optimization
        // 3. Error handling, notifications, and performance logging
        private async Task SaveUsersToDatabase(List<CustomerMonitoringApp.Domain.Entities.User> usersToImport, ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            string connectionString = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

            // Region 1: Database context setup and Polly policy configuration
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(connectionString)
                .Options;

            // Configure a Polly retry policy for transient SQL failures with advanced handling
            var retryPolicy = Policy
                .Handle<SqlException>()
                .Or<TimeoutException>()
                .WaitAndRetryAsync(
                    retryCount: 3, // Limit the number of retry attempts
                    sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)), // Exponential back-off
                    onRetry: (exception, duration, attempt, context) =>
                    {
                        Log($"Retry {attempt} due to: {exception.Message} (Waiting {duration.TotalSeconds} seconds)");
                    }
                );

            // Enhanced performance tracking
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            // Region 2: Main logic for batch processing and data insertion with transaction handling and optimization
            int batchSize = 5000; // Adjust batch size for optimized memory usage and performance
            using var dbContext = new AppDbContext(options);
            using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                // Filter out invalid users
                var validUsers = usersToImport.Where(user => IsValidUser(user)).ToList();

                await retryPolicy.ExecuteAsync(async () =>
                {
                    for (int i = 0; i < validUsers.Count; i += batchSize)
                    {
                        cancellationToken.ThrowIfCancellationRequested(); // Ensure the task can be cancelled if needed

                        var batch = validUsers.Skip(i).Take(batchSize).ToList();

                        // Use SqlBulkCopy for higher efficiency
                        await BulkInsertUsersAsync(batch, connectionString, cancellationToken);

                        Log($"Batch of {batch.Count} users imported successfully in {stopwatch.ElapsedMilliseconds} ms.");
                    }

                    await transaction.CommitAsync(cancellationToken);
                });

                stopwatch.Stop();
                await NotifyUser(botClient, chatId, $"Data imported successfully in {stopwatch.Elapsed.TotalSeconds} seconds.", cancellationToken);
                Log($"All rows imported successfully. Total time: {stopwatch.Elapsed.TotalSeconds} seconds.");
            }
            catch (OperationCanceledException)
            {
                // Handle task cancellation gracefully
                await transaction.RollbackAsync(cancellationToken);
                await NotifyUser(botClient, chatId, "Import cancelled by user. All changes have been rolled back.", cancellationToken);
                Log("Transaction cancelled by user.");
            }
            catch (Exception ex)
            {
                // Region 3: Error handling, notifications, and performance logging
                await transaction.RollbackAsync(cancellationToken);
                await NotifyUser(botClient, chatId, "Import failed. All changes have been rolled back. Please check the file format and content.", cancellationToken);
                Log($"Transaction failed: {ex.Message} | StackTrace: {ex.StackTrace}");
            }
        }


        private bool IsValidUser(CustomerMonitoringApp.Domain.Entities.User user)
        {
            // Implement your validation logic here
            return !string.IsNullOrEmpty(user.UserSourceFile) && !string.IsNullOrEmpty(user.UserAddressFile);
        }


        private async Task BulkInsertUsersAsync(List<CustomerMonitoringApp.Domain.Entities.User> users, string connectionString, CancellationToken cancellationToken)
        {
            using var bulkCopy = new SqlBulkCopy(connectionString);
            using var dataTable = new DataTable();

            // Define the columns of the DataTable to match the Users table in the database
            dataTable.Columns.Add("UserNumberFile", typeof(string));
            dataTable.Columns.Add("UserNameFile", typeof(string));
            dataTable.Columns.Add("UserFamilyFile", typeof(string));
            dataTable.Columns.Add("UserFatherNameFile", typeof(string));
            dataTable.Columns.Add("UserBirthDayFile", typeof(DateTime));
            dataTable.Columns.Add("UserAddressFile", typeof(string));
            dataTable.Columns.Add("UserDescriptionFile", typeof(string));
            dataTable.Columns.Add("UserSourceFile", typeof(string));
            dataTable.Columns.Add("UserTelegramID", typeof(long));
            // Add other columns as needed

            foreach (var user in users)
            {
                dataTable.Rows.Add(
                    user.UserNumberFile,
                    user.UserNameFile,
                    user.UserFamilyFile,
                    user.UserFatherNameFile,
                    user.UserBirthDayFile,
                    user.UserAddressFile,
                    user.UserDescriptionFile,
                    user.UserSourceFile,
                    user.UserTelegramID
                    /*, other fields */
                );
            }

            bulkCopy.DestinationTableName = "Users";
            await bulkCopy.WriteToServerAsync(dataTable, cancellationToken);
        }


        private async Task NotifyUser(ITelegramBotClient botClient, long chatId, string message, CancellationToken cancellationToken)
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
            {
                throw new InvalidOperationException("StartBotButton is not initialized. Ensure it exists in XAML.");
            }

            StartBotButton.MouseEnter += StartBotButton_MouseEnter;
            StartBotButton.MouseLeave += StartBotButton_MouseLeave;
            StartBotButton.Cursor = Cursors.Hand;
        }

        #endregion

        #region Helper Methods
        /// <summary>
        /// Changes the background color of the specified button.
        /// </summary>
        /// <param name="button">The button to change the background color of.</param>
        /// <param name="color">The new background color.</param>
        private void ChangeButtonBackgroundColor(Button button, Color color)
        {
            if (button != null)
            {
                button.Background = new SolidColorBrush(color);
            }
        }

        #endregion


        #endregion


    }
}
