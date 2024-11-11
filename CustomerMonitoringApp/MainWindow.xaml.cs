using CustomerMonitoringApp.Application.Services;
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
using CsvHelper.TypeConversion;
using System.IO.Compression;
using CustomerMonitoringApp.Application.Interfaces;
using CustomerMonitoringApp.Domain.Views;
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
            GetCommandInlineKeyboard();
            timestamp = GetPersianDate() + ".csv";
            LoadUsersFromDatabaseAsync();
            InitializeBotClient();
            InitializeComponent();
            InitializeButton();
        }

        #region Fields and Properties
        private static readonly Regex FileNameRegex = new Regex(@"^(Getcalls|Getrecentcalls|Getlongcalls)_\d{10,}_\d{8}_\d{6}\.csv$", RegexOptions.Compiled);
        private string timestamp;
        private readonly ILogger<MainWindow> _logger;
        private CancellationTokenSource _cancellationTokenSource;
        private ITelegramBotClient _botClient;
        private readonly NotificationService _notificationService;
        private readonly string
            _token = "6768055952:AAGSETUCUC76eXuSoAGX6xcsQk1rrt0K4Ng"; // Replace with your actual bot token
        private readonly ConcurrentDictionary<long, UserState> _userStates;
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

            _lastMessageTimes = new ConcurrentDictionary<long, DateTime>();
            _messageTimestamps = new ConcurrentDictionary<long, Queue<DateTime>>();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _callHistoryRepository = callHistoryRepository ?? throw new ArgumentNullException(nameof(callHistoryRepository));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _callHistoryImportService = callHistoryImportService ?? throw new ArgumentNullException(nameof(callHistoryImportService));
            _userStates = new ConcurrentDictionary<long, UserState>();
            GetCommandInlineKeyboard();
        }

        private string GetPersianDate()
        {
            PersianCalendar persianCalendar = new PersianCalendar();

            // تاریخ امروز به شمسی
            int year = persianCalendar.GetYear(DateTime.Now);
            int month = persianCalendar.GetMonth(DateTime.Now);
            int day = persianCalendar.GetDayOfMonth(DateTime.Now);
            int hour = DateTime.Now.Hour;
            int minute = DateTime.Now.Minute;
            int second = DateTime.Now.Second;

            // فرمت تاریخ شمسی: yyyyMMdd_HHmmss
            return $"{year:D4}{month:D2}{day:D2}_{hour:D2}{minute:D2}{second:D2}";
        }
        // User state class to track per-user progress
        private class UserState
        {
            public bool IsBusy { get; set; } = false; // To prevent multiple simultaneous requests
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



        private readonly ConcurrentDictionary<long, DateTime> _lastMessageTimes;
        private readonly ConcurrentDictionary<long, Queue<DateTime>> _messageTimestamps;

        private const int MinMessageIntervalInSeconds = 5; // Minimum time interval between two messages (in seconds)
        private const int MaxMessagesPerInterval = 30; // Maximum number of messages allowed per user in a given interval (e.g., 5 messages per 60 seconds)
        private const int MessageWindowInSeconds = 60; // Time window to count the messages (e.g., 60 seconds)

        // Checks if a message has been processed by its MessageId
        /// <summary>
        /// Tracks the timestamp of the last messages for each chatId, with a 5-minute timeout to avoid excessive message queueing.
        /// Maintains only recent timestamps within a specific time threshold, optimizing memory usage.
        /// </summary>
        /// <param name="chatId">Unique identifier for the chat.</param>
        private void TrackMessageTimestamp(long chatId)
        {
            // Define the message retention threshold and timeout duration
            TimeSpan messageRetentionThreshold = TimeSpan.FromMinutes(1); // Time to keep recent messages
            TimeSpan requestTimeout = TimeSpan.FromMinutes(5); // Max time allowed between requests

            DateTime now = DateTime.UtcNow;

            // Initialize queue if it does not already exist for this chatId
            if (!_messageTimestamps.ContainsKey(chatId))
            {
                _messageTimestamps[chatId] = new Queue<DateTime>();
            }

            var messageQueue = _messageTimestamps[chatId];

            // Check if the last message timestamp exceeds the request timeout
            if (messageQueue.Count > 0 && (now - messageQueue.Last()) > requestTimeout)
            {
                // Reset the queue if the timeout has passed
                messageQueue.Clear();
            }

            // Remove timestamps older than the retention threshold to keep only recent messages
            while (messageQueue.Count > 0 && (now - messageQueue.Peek()) > messageRetentionThreshold)
            {
                messageQueue.Dequeue();
            }

            // Enqueue the current timestamp to record this message
            messageQueue.Enqueue(now);

            // Optional: Implement a maximum queue size to limit memory usage
            int maxQueueSize = 100; // Define max queue size based on expected message frequency
            if (messageQueue.Count > maxQueueSize)
            {
                messageQueue.Dequeue(); // Remove the oldest message if queue exceeds max size
            }
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
            if (!_messageTimestamps.ContainsKey(chatId))
            {
                _messageTimestamps[chatId] = new Queue<DateTime>();
            }

            var messageQueue = _messageTimestamps[chatId];

            // Remove old timestamps that are outside the window
            while (messageQueue.Any() && (now - messageQueue.Peek()).TotalSeconds > MessageWindowInSeconds)
            {
                messageQueue.Dequeue();
            }

            // Check if the user exceeded the max messages allowed in the time window
            if (messageQueue.Count >= MaxMessagesPerInterval)
            {
                return true; // User is spamming
            }

            return false;
        }

        #region Command Handler

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update,
          CancellationToken cancellationToken)
        {
            var chatId = update.Message?.Chat?.Id;
            var messageId = update.Message?.MessageId;


            if (chatId == null || messageId == null)
            {
             //   Log("Error: Invalid chatId or messageId.");
                return;
            }

            // Anti-Spam: Check if the user is spamming by checking the message frequency
            if (IsSpamming(chatId.Value))
            {
              //  Log($"Spam detected from ChatId {chatId}. Skipping message.");
                return; // Skip processing if the user is spamming
            }

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

                try
                {
                    switch (command)
                    {
                        case "/getcalls":
                            await HandleGetCallsCommand(commandParts, chatId.Value, botClient, cancellationToken, userState);
                            break;
                        case "/getrecentcalls":
                            await HandleGetRecentCallsCommand(commandParts, chatId.Value, botClient, cancellationToken, userState);
                            break;
                        case "/getlongcalls":
                            await HandleGetLongCallsCommand(commandParts, chatId.Value, botClient, cancellationToken, userState);
                            break;
                        case "/getafterhourscalls":
                            await HandleGetAfterHoursCallsCommand(commandParts, chatId.Value, botClient, cancellationToken, userState);
                            break;
                        case "/getfrequentcalldates":
                            await HandleGetFrequentCallDatesCommand(commandParts, chatId.Value, botClient, cancellationToken, userState);
                            break;
                        case "/gettoprecentcalls":
                            await HandleGetTopRecentCallsCommand(commandParts, chatId.Value, botClient, cancellationToken, userState);
                            break;
                        case "/hasrecentcall":
                            await HandleHasRecentCallCommand(commandParts, chatId.Value, botClient, cancellationToken, userState);
                            break;
                        case "/getallcalls":
                            await HandleAllCallWithName(commandParts, chatId.Value, botClient, cancellationToken, userState);
                            break;
                        case "/reset":
                            await HandleDeleteAllCallsCommand(chatId.Value, botClient, cancellationToken);
                            break;
                        // New case to delete call histories by file name
                        // New case to delete call histories by file name
                        case "/deletecallsbyfilename":
                            string fileName = commandParts[1]; // Get the file name from the command arguments

                            try
                            {
                                // Call the method to delete call histories by the provided file name
                                await HandleDeleteCallsByFileNameCommand(fileName, chatId.Value, botClient, cancellationToken);
                            }
                            catch (Exception ex)
                            {
                                // Handle any errors that occur during the deletion process
                                await botClient.SendMessage(chatId.Value, $"Error deleting call histories for file '{fileName}': {ex.Message}", cancellationToken: cancellationToken);
                            }
                            break;

                        default:
                            await botClient.SendMessage(
                                chatId: chatId.Value,
                                text: "🤔 *Unknown Command*\n\n" +
                                      "Oops! It looks like you've entered a command that I don’t recognize and can’t process right now. But no worries—I’m here to help! Please try one of the following supported commands:\n\n" +

                                      "📞 /getcalls - _Retrieve a complete history of all calls, including dates, durations, and participants._\n\n" +
                                      "📅 /getrecentcalls - _Get a quick overview of the most recent calls made or received, with details like call times and participants._\n\n" +
                                      "⏳ /getlongcalls - _Find and list calls that lasted longer than a specific duration, making it easy to identify longer conversations._\n\n" +
                                      "🔝 /gettoprecentcalls - _See the top N most recent calls, so you can quickly access the latest records._\n\n" +
                                      "🕒 /hasrecentcall - _Check if a specific phone number has had any calls within a certain time frame to track recent interactions._\n\n" +
                                      "📞 /getallcalls - _Retrieve a complete history of all calls, including dates, durations, and participants._\n\n" +  // New /getallcalls command
                                                                                                                                                           // New commands
                                      "🔄 /reset - _Reset the database of all calls, clearing all data._\n\n" +
                                      "🗑️ /deletebyfilename - _Delete all call records associated with a specific file name._\n\n" +

                                      "If you need help with any commands or want a full list, just use `/help` for detailed instructions. 😊\n\n" +
                                      "I’m here to make things as easy as possible for you, so feel free to reach out anytime!",
                                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                                replyParameters: messageId.Value,
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
                        text: "❌ *Error processing command*\n\n" +
                              "Oops, something went wrong while trying to process your command. Here's the error message I encountered:\n\n" +
                              $"⚠️ _{ex.Message}_\n\n" +
                              "Don't worry, though! I’m here to help you. You can try again, or if you're still having trouble, feel free to send a more detailed request, and I’ll assist you further.\n\n" +
                              "If this issue persists, please try using `/help` to review the available commands and their correct formats. I'm always available to guide you through any issues or questions you have. 😊",
                        replyParameters: messageId.Value,  // This ensures your message is a reply to the user's message
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
                            chatId: chatId.Value,
                            text: "📥 *I’ve received your .xlsx file!* \n\n" +
                                  "I’m getting to work on processing it right now! 🚀 This may take a little time, depending on the file size and content, but don't worry—I’ll keep you updated.\n\n" +
                                  "In the meantime, feel free to relax or ask me anything else! I’ll notify you as soon as the processing is done and you’re ready to proceed. ⏳💼\n\n" +
                                  "Thank you for your patience! Your file is in good hands. 😊",
                            replyParameters: messageId.Value,
                            parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
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
                                chatId: chatId.Value,
                                text: "🧲 File downloaded successfully", replyParameters: messageId.Value,
                                cancellationToken: cancellationToken
                            );
                        }
                        catch (Exception downloadEx)
                        {
                       //     Log($"Error downloading file from user {chatId}: {downloadEx.Message}");
                            await botClient.SendMessage(
                                chatId: chatId.Value, replyParameters: messageId.Value,
                                text: "❌ Error downloading the file. Please try again.",
                                cancellationToken: cancellationToken
                            );
                            return;
                        }


                        bool isUserFile = await IsUserFile(filePath);
                        // Step 3: Identify file type (CallHistory or UsersUpdate)
                        bool isCallHistory = await IsCallHistoryFileAsync(filePath);
                   
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
                                await _callHistoryImportService.ProcessExcelFileAsync(filePath, document.FileName, cancellationToken);
                                Log($"Successfully processed CallHistory data: {filePath} for user ID:{chatId}");

                                await botClient.SendMessage(
                                    chatId: chatId.Value, replyParameters: messageId.Value,
                                    text: "✅ *Database operations complete, and users added successfully!* \n\n",
                                    cancellationToken: cancellationToken,
                                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown);

                            }


                            catch (ArgumentException argEx)
                            {
                                Log($"Invalid argument error: {argEx.Message}");
                                await botClient.SendMessage(
                                    chatId: chatId.Value, replyParameters: messageId.Value,
                                    text: $"❌ Invalid file or input. Error: {argEx.Message}",
                                    cancellationToken: cancellationToken
                                );
                            }
                            catch (Exception ex)
                            {
                                Log($"General error processing CallHistory data: {ex.Message}");
                                await botClient.SendMessage(
                                    chatId: chatId.Value, replyParameters: messageId.Value,
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
                                    chatId: chatId.Value, replyParameters: messageId.Value,
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
                                chatId: chatId.Value, replyParameters: messageId.Value,
                                text: "⚠️ The file type is not supported. Please upload a valid .xlsx file.",
                                cancellationToken: cancellationToken
                            );
                            Log($"User {chatId} uploaded an unsupported file type: {document?.FileName}");
                            return;
                        }

                        // Step 4: Inform the user of success and provide summary
                        await botClient.SendMessage(
                            chatId: chatId.Value, replyParameters: messageId.Value,
                            text: "✅ File processed and data imported successfully!",
                            cancellationToken: cancellationToken
                        );
                        Log($"User {chatId} was informed of successful processing.");
                    }
                    else
                    {
                        // Handle incorrect file types with detailed feedback
                        await botClient.SendMessage(
                            chatId: chatId.Value, replyParameters: messageId.Value,
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
                        chatId: chatId.Value, replyParameters: messageId.Value,
                        text: "❌ An unexpected error occurred while processing your file. Please try again later.",
                        cancellationToken: cancellationToken
                    );
                }
            }
            finally
            {

                Log($"Completed file processing for user {chatId}");
            }

            // Handle CallbackQuery if applicable
            if (update.Type == UpdateType.CallbackQuery)
            {
                await HandleCallbackQueryAsync(botClient, update.CallbackQuery, cancellationToken);
                return; // Exit early after handling the callback
            }
        }



        #region Delete Files From File

        public async Task HandleDeleteCallsByFileNameCommand(string fileName, long chatId, ITelegramBotClient botClient, CancellationToken cancellationToken)
        {
            try
            {
                // Call the repository method to delete the call histories for the given file name
                await _callHistoryRepository.DeleteCallHistoriesByFileNameAsync(fileName);

                // Send a success message to the user
                await botClient.SendMessage(chatId, $"Successfully deleted all call histories for the file: {fileName}.", cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                // Handle any error during the deletion process
                await botClient.SendMessage(chatId, $"Failed to delete call histories for the file: {fileName}. Error: {ex.Message}", cancellationToken: cancellationToken);
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
                await botClient.SendMessage(chatId, "⚠️ You are currently processing a request. Please wait until it is completed.", cancellationToken: cancellationToken);
                return;
            }

            // Mark user as busy while processing this request
            userState.IsBusy = true;

            // Ensure the user provided a valid phone number and a time span
            if (commandParts.Length < 3 || !TimeSpan.TryParse(commandParts[2], out TimeSpan timeSpan))
            {
                await botClient.SendMessage(chatId, "Usage: /hasrecentcall [phoneNumber] [timeSpan]", cancellationToken: cancellationToken);
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
        /// Retrieves the user details based on phone number and sends it to the user via Telegram.
        /// </summary>
        /// <summary>
        /// Sends user details to a Telegram chat based on the phone number.
        /// </summary>
        /// <param name="phoneNumber">The phone number of the user.</param>
        /// <param name="chatId">The Telegram chat ID to send the details to.</param>
        /// <param name="cancellationToken">Token to cancel the operation if needed.</param>
        public async Task SendUserDetailsToTelegramAsync(string phoneNumber, long chatId, CancellationToken cancellationToken)
        {
            try
            {
                // Fetch user details by phone number
                var user = await _callHistoryRepository.GetUserDetailsByPhoneNumberAsync(phoneNumber);

                // Check if user was found
                if (user == null)
                {
                    await _botClient.SendMessage(
                        chatId: chatId,
                        text: "❌ *No user found with the provided phone number.* Please try again with a valid phone number.",
                        parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
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
                    chatId: chatId,
                    text: message,
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
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
                    chatId: chatId,
                    text: "⚠️ An error occurred while retrieving user details. Please try again later.",
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                    cancellationToken: cancellationToken
                );
            }
        }


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
                await botClient.SendMessage(chatId, "⚠️ You are currently processing a request. Please wait until it is completed.", cancellationToken: cancellationToken);
                return;
            }

            // Mark user as busy while processing this request
            userState.IsBusy = true;

            // Ensure the user provided a valid phone number
            if (commandParts.Length < 2)
            {
                await botClient.SendMessage(chatId, "Usage: /getallcalls [phoneNumber]", cancellationToken: cancellationToken);
                userState.IsBusy = false;
                return;
            }

            var phoneNumber = commandParts[1];

            try
            {
                await SendUserDetailsToTelegramAsync(phoneNumber, chatId, cancellationToken);
                // Fetch all calls for the provided phone number
                var calls = await _callHistoryRepository.GetCallsWithUserNamesAsync(phoneNumber);
            
                // If no calls found, inform the user
                if (calls == null || !calls.Any())
                {
                    await botClient.SendMessage(chatId, $"❌ No calls found for the phone number {phoneNumber}.", cancellationToken: cancellationToken);
                    return;
                }

                // Generate the CSV file for the call history
                var csvFilePath = GenerateCsv(calls);

                // Send the CSV file to the user
                using var stream = new FileStream(csvFilePath, FileMode.Open, FileAccess.Read);
                await botClient.SendDocument(chatId, new InputFileStream(stream, $"All_Calls_{phoneNumber}_{DateTime.UtcNow:yyyyMMddHHmmss}.csv"), cancellationToken: cancellationToken);

                // Notify the user that the file has been sent
                await botClient.SendMessage(chatId, "📁 Your call history has been sent in a CSV file.", cancellationToken: cancellationToken);

                // Clean up by deleting the temporary file
                File.Delete(csvFilePath);
            }
            catch (Exception ex)
            {
                // Log the error and inform the user about the issue
                Log($"Error processing the /getallcalls command: {ex.Message}");
                await botClient.SendMessage(chatId, "❌ *Error processing command*\n\nOops, something went wrong. Please try again later.", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
            }
            finally
            {
                // Ensure user is marked as not busy once the task completes
                userState.IsBusy = false;
            }
        }
        public string GenerateCsv(IEnumerable<CallHistoryWithUserNames> calls)
        {
            var csvFilePath = Path.Combine(Path.GetTempPath(), $"CallHistory_{DateTime.UtcNow:yyyyMMddHHmmss}.csv");

            try
            {
                using (var writer = new StreamWriter(csvFilePath, false, Encoding.UTF8)) // Ensure UTF-8 encoding
                using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
                {
                    // Write header row
                    csv.WriteField("CallId");
                    csv.WriteField("SourcePhoneNumber");
                    csv.WriteField("DestinationPhoneNumber");
                    csv.WriteField("CallDateTime");
                    csv.WriteField("Duration");
                    csv.WriteField("CallType");
                    csv.WriteField("FileName");
                    csv.WriteField("CallerName");
                    csv.WriteField("ReceiverName");
                    csv.NextRecord();

                    // Write data rows
                    foreach (var call in calls)
                    {
                        csv.WriteField(call.CallId);
                        csv.WriteField(call.SourcePhoneNumber);
                        csv.WriteField(call.DestinationPhoneNumber);
                        csv.WriteField(call.CallDateTime);
                        csv.WriteField(call.Duration);
                        csv.WriteField(call.CallType);
                        csv.WriteField(call.FileName);
                        csv.WriteField(call.CallerName ?? string.Empty);
                        csv.WriteField(call.ReceiverName ?? string.Empty);
                        csv.NextRecord();
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the error for debugging or tracking
                Log($"Error generating CSV: {ex.Message}");

                // Clean up by deleting the file if it exists
                if (File.Exists(csvFilePath))
                {
                    File.Delete(csvFilePath);
                }

                // Optionally, rethrow or return an empty string / null to indicate failure
                throw new Exception("Failed to generate CSV file.", ex);
            }

            return csvFilePath;
        }


        private Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup GetCommandInlineKeyboard()
        {
            return new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup(new[]
            {
        new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton[]
        {
            new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton
            {
                Text = "📞 /getcalls - Retrieve Complete Call History",
                CallbackData = "/getcalls"
            }
        },
        new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton[]
        {
            new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton
            {
                Text = "📅 /getrecentcalls - Get a Quick Overview of Recent Calls",
                CallbackData = "/getrecentcalls"
            }
        },
        new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton[]
        {
            new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton
            {
                Text = "⏳ /getlongcalls - Find Calls with Duration Exceeding a Set Time",
                CallbackData = "/getlongcalls"
            }
        },
        new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton[]
        {
            new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton
            {
                Text = "🔝 /gettoprecentcalls - View Top Recent Calls",
                CallbackData = "/gettoprecentcalls"
            }
        },
        new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton[]
        {
            new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton
            {
                Text = "🕒 /hasrecentcall - Check if a Number Has Recently Called",
                CallbackData = "/hasrecentcall"
            }
        },
        new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton[]
        {
            new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton
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
                await botClient.SendMessage(chatId, "⚠️ You are currently processing a request. Please wait until it is completed.", cancellationToken: cancellationToken);
                return;
            }

            // Mark user as busy while processing this request
            userState.IsBusy = true;

            // Ensure the user provided a phone number and the number of rows (calls)
            if (commandParts.Length < 3 || !int.TryParse(commandParts[2], out int topN))
            {
                await botClient.SendMessage(chatId, "Usage: /gettoprecentcalls [phoneNumber] [numberOfCalls]", cancellationToken: cancellationToken);
                userState.IsBusy = false; // Reset user state if input is invalid
                return;
            }

            var phoneNumber = commandParts[1];

            try
            {
                // Get the top N most recent calls for the phone number
                var topRecentCalls = await _callHistoryRepository.GetTopNRecentCallsAsync(phoneNumber, topN);

                // If no calls are found, send a message to the user
                if (!topRecentCalls.Any())
                {
                    await botClient.SendMessage(chatId, $"No recent calls found for {phoneNumber}.", cancellationToken: cancellationToken);
                    userState.IsBusy = false; // Reset user state after processing
                    return;
                }

                // Generate a CSV file for the top recent calls
                var csvFilePath = GenerateCsv(topRecentCalls);

                // Send the CSV file to the user
                using var stream = new FileStream(csvFilePath, FileMode.Open, FileAccess.Read);
                await botClient.SendDocument(chatId, new InputFileStream(stream, $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_TopRecentCalls.csv"), cancellationToken: cancellationToken);

                // Clean up by deleting the file after sending
                File.Delete(csvFilePath);
            }
            catch (Exception ex)
            {
                // Log and notify the user if something goes wrong
                Log($"Error processing /gettoprecentcalls command: {ex.Message}");
                await botClient.SendMessage(chatId, "❌ *Error processing command*\n\n" +
                                                             "Oops, something went wrong while trying to process your command. Please try again later. If the issue persists, contact support.",
                                                             cancellationToken: cancellationToken);
            }
            finally
            {
                // Ensure user is marked as not busy once the task completes
                userState.IsBusy = false;
            }
        }

        private async Task HandleDeleteAllCallsCommand(long chatId, ITelegramBotClient botClient, CancellationToken cancellationToken)
        {
            try
            {
                // Delete all call history records
                await _callHistoryRepository.DeleteAllCallHistoriesAsync();

                // Confirm deletion
                await botClient.SendMessage(
                    chatId: chatId,
                    text: "🗑️ All call history records have been successfully deleted.",
                    cancellationToken: cancellationToken
                );
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error deleting all call history records: {ex.Message}");
                await botClient.SendMessage(
                    chatId: chatId,
                    text: "⚠️ Failed to delete call history records. Please try again later.",
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
                await botClient.SendMessage(chatId, "⚠️ You are currently processing a request. Please wait until it is completed.", cancellationToken: cancellationToken);
                return;
            }

            // Mark user as busy while processing this request
            userState.IsBusy = true;

            // Validate input: Ensure the user has provided a phone number, start time, and end time
            if (commandParts.Length < 4 ||
                !TimeSpan.TryParse(commandParts[2], out TimeSpan startTime) ||
                !TimeSpan.TryParse(commandParts[3], out TimeSpan endTime))
            {
                await botClient.SendMessage(chatId, "Usage: /getafterhourscalls [phoneNumber] [startTime] [endTime]. Example: /getafterhourscalls +1234567890 18:00 06:00", cancellationToken: cancellationToken);
                userState.IsBusy = false; // Reset user state in case of invalid input
                return;
            }

            var phoneNumber = commandParts[1];

            try
            {
                // Retrieve after-hours calls based on the phone number and the provided time window
                var afterHoursCalls = await _callHistoryRepository.GetAfterHoursCallsByPhoneNumberAsync(phoneNumber, startTime, endTime);

                // If no calls are found, notify the user
                if (!afterHoursCalls.Any())
                {
                    await botClient.SendMessage(chatId, $"❌ No after-hours calls found for {phoneNumber} between {startTime} and {endTime}.", cancellationToken: cancellationToken);
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
        /// Converts a collection of items to a CSV-formatted string, with custom handling to force Excel to treat strings as text.
        /// This method can be extended to better handle Excel import by ensuring data is presented in a specific way that mimics a theme.
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
                    {
                        if (property.PropertyType == typeof(string))
                        {
                            var value = (string)property.GetValue(item);
                            if (value != null)
                            {
                                property.SetValue(item, value.Trim()); // Trim the string value
                            }
                        }
                    }
                    return item;
                }).ToList();

                using (var writer = new StringWriter())
                using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,        // Include a header row
                    Delimiter = ",",               // Customize delimiter if needed
                    ShouldQuote = (field) => true  // Quote all fields
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

        #endregion

        #region Custom Type Converter

        /// <summary>
        /// Custom type converter that adds an apostrophe to the beginning of string values to force Excel to treat them as text.
        /// This is important for values like large numbers, ZIP codes, or other numeric values that should not be treated as numbers.
        /// </summary>
        public class ApostropheConverter : ITypeConverter
        {
            /// <summary>
            /// Converts the value to a string with a prefixed apostrophe to enforce text treatment in Excel.
            /// Useful for ensuring numeric-like data (e.g., IDs, codes) are treated as text when opened in Excel.
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
                return $"'{value.ToString()}";
            }

            /// <summary>
            /// Not implemented. Conversion from string is not required in this scenario.
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
                await botClient.SendMessage(chatId, "⚠️ You are currently processing a request. Please wait until it is completed.", cancellationToken: cancellationToken);
                return;
            }

            // Mark user as busy while processing this request
            userState.IsBusy = true;

            // Ensure the user has provided a valid phone number
            if (commandParts.Length < 2)
            {
                await botClient.SendMessage(chatId, "Please provide a phone number. Example: /getfrequentcalldates +1234567890", cancellationToken: cancellationToken);
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
                    await botClient.SendMessage(chatId, $"❌ No frequent call dates found for {phoneNumber}.", cancellationToken: cancellationToken);
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
                await botClient.SendMessage(chatId, "⚠️ You are currently processing a request. Please wait until it is completed.", cancellationToken: cancellationToken);
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
                var callHistories = await _callHistoryRepository.GetCallsByPhoneNumberAsync(phoneNumber);

                // If no call history found, notify the user
                if (!callHistories.Any())
                {
                    await botClient.SendMessage(chatId, "No call history found for this phone number.", cancellationToken: cancellationToken);
                    userState.IsBusy = false; // Reset user state after processing
                    return;
                }

                // Generate a CSV file for the call history
                var csvFilePath = GenerateCsv(callHistories);

                // Send the CSV file to the user
                using var stream = new FileStream(csvFilePath, FileMode.Open, FileAccess.Read);
                await botClient.SendDocument(chatId, new InputFileStream(stream, $"getcalls_{phoneNumber}_{DateTime.UtcNow:yyyyMMddHHmmss}.csv"), cancellationToken: cancellationToken);

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
                await botClient.SendMessage(chatId, "⚠️ You are currently processing a request. Please wait until it is completed.", cancellationToken: cancellationToken);
                return;
            }

            // Mark the user as busy while processing this request
            userState.IsBusy = true;

            // Ensure the user provided both phone number and the days parameter
            if (commandParts.Length < 3 || !int.TryParse(commandParts[2], out int days))
            {
                await botClient.SendMessage(chatId, "Usage: /getrecentcalls [phoneNumber] [days]", cancellationToken: cancellationToken);
                userState.IsBusy = false; // Reset user state after invalid input
                return;
            }

            var phoneNumber = commandParts[1];

            // Ensure the 'days' value is positive
            if (days <= 0)
            {
                await botClient.SendMessage(chatId, "Please provide a positive number for days.", cancellationToken: cancellationToken);
                userState.IsBusy = false; // Reset user state
                return;
            }

            try
            {
                // Calculate the start date based on the given days and get the end date as today
                var startDate = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-dd");
                var endDate = DateTime.UtcNow.ToString("yyyy-MM-dd"); // Use current date as the end date

                // Retrieve recent calls for the provided phone number
                var recentCalls = await _callHistoryRepository.GetRecentCallsByPhoneNumberAsync(phoneNumber, startDate, endDate);

                // If no recent calls found, notify the user
                if (!recentCalls.Any())
                {
                    await botClient.SendMessage(chatId, "No recent calls found for this phone number within the specified time frame.", cancellationToken: cancellationToken);
                    userState.IsBusy = false; // Reset user state
                    return;
                }

                // Generate CSV file with recent calls data
                var csvFilePath = GenerateCsv(recentCalls);

                // Send the CSV file to the user
                using var stream = new FileStream(csvFilePath, FileMode.Open, FileAccess.Read);
                await botClient.SendDocument(chatId, new InputFileStream(stream, $"getrecentcalls_{phoneNumber}_{DateTime.UtcNow:yyyyMMddHHmmss}.csv"), cancellationToken: cancellationToken);

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
                await botClient.SendMessage(chatId, "⚠️ You are currently processing a request. Please wait until it is completed.", cancellationToken: cancellationToken);
                return;
            }

            // Mark the user as busy while processing this request
            userState.IsBusy = true;

            // Ensure the user provided both phone number and minimum call duration
            if (commandParts.Length < 3 || !int.TryParse(commandParts[2], out int minimumDuration) || minimumDuration <= 0)
            {
                await botClient.SendMessage(chatId, "Usage: /getlongcalls [phoneNumber] [minimumDurationInSeconds]", cancellationToken: cancellationToken);
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
                    await botClient.SendMessage(chatId, "No long calls found for this phone number with the specified minimum duration.", cancellationToken: cancellationToken);
                    userState.IsBusy = false; // Reset user state
                    return;
                }

                // Generate CSV file with the long calls data
                var csvFilePath = GenerateCsv(longCalls);

                // Use a dynamic timestamp for the file name
                var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");

                // Send the CSV file to the user
                using var stream = new FileStream(csvFilePath, FileMode.Open, FileAccess.Read);
                await botClient.SendDocument(chatId, new InputFileStream(stream, $"getlongcalls_{phoneNumber}_{timestamp}.csv"), cancellationToken: cancellationToken);

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
                           HasHeaderRecord = true,          // Include headers
                           Delimiter = ",",                 // Use comma as the delimiter
                           ShouldQuote = (field) => true    // Quote all fields to handle special characters
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


        private async Task<bool> IsCallHistoryFileAsync(string filePath)
        {
            // Define expected headers for the CallHistory file
            var expectedHeaders = new List<string>
    {
        "شماره مبدا",  // Source Phone
        "شماره مقصد",  // Destination Phone
        "تاریخ",        // Date
        "ساعت",         // Time
        "مدت",          // Duration
        "نوع تماس"      // Call Type
    }
            .Select(header => CleanHeader(header))  // Clean the expected headers
            .ToList();

            // Set match threshold (70%)
            double matchThreshold = 0.7;
            int requiredMatches = (int)Math.Ceiling(expectedHeaders.Count * matchThreshold);

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

                    // Retrieve headers from the first row, clean them and compare
                    var headers = new List<string>();
                    for (int col = 1; col <= columnCount; col++)
                    {
                        var header = worksheet.Cells[1, col].Text.Trim();
                        headers.Add(CleanHeader(header));
                    }

                    // Log found headers for debugging
                    Log($"Found headers: {string.Join(", ", headers)}");

                    // Check if the headers match the expected headers with the given threshold
                    int matchedHeaderCount = headers.Intersect(expectedHeaders).Count();

                    // If the matched headers are below the threshold, return false
                    if (matchedHeaderCount < requiredMatches)
                    {
                        Log($"Error: Insufficient matching headers. Expected at least {requiredMatches} matches, found {matchedHeaderCount}.");
                        return false;
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
                    for (int row = 2; row <= rowCount; row++)  // Start from row 2 (skipping header row)
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
                            // Log($"INFO: Skipping row {row} due to non-numeric phone numbers.");
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

                        // If we have more than 2 valid rows, return true
                        if (validRowCount > 2)
                        {
                            return true;
                        }
                    }

                    // If we don't have more than 2 valid rows, return false
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
            if (string.IsNullOrWhiteSpace(input))
            {
                _logger.LogWarning("Input is null, empty, or whitespace.");
                return false;
            }

            try
            {
                // Return true if all characters in the string are digits
                return input.All(c => Char.IsDigit(c));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error checking if string is numeric: {ex.Message}");
                return false;
            }
        }



        private async Task<bool> IsUserFile(string filePath)
        {
            // Define expected headers for user data, trim spaces and remove any special characters like colons
            var expectedHeaders = new List<string> { "شماره تلفن", "نام", "نام خانوادگی", "نام پدر", "تاریخ تولد", "نشانی" }
                .Select(header => CleanHeader(header)).ToList();

            // Set match threshold (70%)
            double matchThreshold = 0.7;
            int requiredMatches = (int)Math.Ceiling(expectedHeaders.Count * matchThreshold);

            try
            {
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
                    for (int col = 1; col <= worksheet.Dimension?.Columns; col++)
                    {
                        var header = worksheet.Cells[1, col].Text.Trim();
                        if (!string.IsNullOrEmpty(header))
                        {
                            headers.Add(CleanHeader(header));
                        }
                    }

                    // Log found headers for debugging
                    Log($"Found headers: {string.Join(", ", headers)}");

                    if (!headers.Any())
                    {
                        Log("Error: No headers found in the first row.");
                        return false;
                    }

                    // Count matching headers
                    int matchedHeadersCount = headers.Intersect(expectedHeaders).Count();

                    // Validate the match count against the threshold
                    if (matchedHeadersCount >= requiredMatches)
                    {
                        Log($"Success: Recognized as user file. Matched headers: {string.Join(", ", headers.Intersect(expectedHeaders))}");
                        return true;  // Likely a user file
                    }
                    else
                    {
                        var missingHeaders = expectedHeaders.Except(headers).ToList();
                        Log($"Error: Insufficient matching headers. Expected at least {requiredMatches} matches, found {matchedHeadersCount}. Missing headers: {string.Join(", ", missingHeaders)}.");
                        return false;  // Not enough matches
                    }
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
                .Replace(":", "")  // Remove colons
                .Replace(" ", "")  // Remove spaces
                .ToLower();        // Convert to lowercase
        }


        // Helper method to validate Phone Number
        /// <summary>
        /// Validates that the provided phone number starts with "09" or "98", has the appropriate length,
        /// and contains only numeric characters.
        /// </summary>
        /// <param name="phoneNumber">The phone number to validate.</param>
        /// <returns>True if the phone number is valid, false otherwise.</returns>
        private bool IsValidPhoneNumber(string phoneNumber)
        {
            // Check if phone number starts with "09" and has 11 digits, or starts with "98" and has 12 digits
            return (phoneNumber.StartsWith("09") && phoneNumber.Length == 11 ||
                    phoneNumber.StartsWith("98") && phoneNumber.Length == 12)
                   && phoneNumber.All(char.IsDigit);
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




        #region Excel File Import



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





        private async Task ImportExcelToDatabase(string filePath, ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            var usersToImport = new List<User>();
            var errors = new List<string>(); // To collect error messages
            const int BatchSize = 1000; // Define a batch size for processing
            var processedRows = 0; // Track the number of processed rows
            var startTime = DateTime.Now; // Track the start time of the operation
            int totalRows = 0; // Track total number of rows in the worksheet for progress calculation

            // Variable to hold the message reference for editing progress
            Message progressMessage = null;
            int lastProgress = -1; // Track the last progress percentage to prevent redundant updates

            try
            {
                // Validate file existence
                if (!File.Exists(filePath))
                {
                    await NotifyUser(botClient, chatId, "Error: The specified Excel file does not exist. Please send the file again.", cancellationToken);
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
                    await NotifyUser(botClient, chatId, "Error: No worksheet found in the Excel file. Please send the file again.", cancellationToken);
                    return;
                }

                Log($"Worksheet found: {worksheet.Name}");

                // Check if worksheet is valid
                if (worksheet.Dimension == null || worksheet.Dimension.Rows == 0)
                {
                    await NotifyUser(botClient, chatId, "Error: The worksheet is empty. Please send the file again.", cancellationToken);
                    return;
                }

                totalRows = worksheet.Dimension.Rows; // Store the total number of rows
                Log($"Worksheet is valid with {totalRows} rows.");

                // Send initial progress message
                progressMessage = await botClient.SendTextMessageAsync(
                    chatId,
                    "Import in progress: 0%",
                    cancellationToken: cancellationToken
                );

                // Loop through rows and process each user
                for (int row = 2; row <= totalRows; row++)
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

                        // Calculate progress percentage (capped at 100%)
                        int progress = (int)((double)processedRows / totalRows * 100);
                        progress = Math.Min(progress, 100); // Ensure it does not exceed 100%

                        // Only update if progress has changed
                        if (progress != lastProgress)
                        {
                            await EditProgressMessage(botClient, chatId, progressMessage, progress, cancellationToken);
                            lastProgress = progress; // Update last progress to the current one
                        }

                        // Log progress every 10 rows (optional for local debugging)
                        if (processedRows % 10 == 0)
                        {
                            Log($"Processed {processedRows}/{totalRows} rows ({progress}%).");
                        }

                        // Save users in batches
                        if (usersToImport.Count >= BatchSize)
                        {
                            await SaveUsersToDatabase(usersToImport, botClient, chatId, cancellationToken, filePath);
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
                    await SaveUsersToDatabase(usersToImport, botClient, chatId, cancellationToken, filePath);
                }

                // Notify user about completion
                if (errors.Count > 0)
                {
                    string errorMessage = string.Join("\n", errors);
                    await NotifyUser(botClient, chatId, $"Some users were not imported due to the following errors:\n{errorMessage}\nPlease send the file again.", cancellationToken);
                }
                else
                {
                    await NotifyUser(botClient, chatId, $"Successfully imported {processedRows} users.", cancellationToken);
                }

                var endTime = DateTime.Now;
                var duration = endTime - startTime;
                Log($"Import completed in {duration.TotalSeconds} seconds.");
            }
            catch (Exception ex)
            {
                Log($"Unexpected Error: {ex.Message}");
                await NotifyUser(botClient, chatId, "❌ Oops! An unexpected error occurred. Please try again later or contact support. Please send the file again.", cancellationToken);
            }
        }


        // Method to edit the progress message
        private async Task EditProgressMessage(ITelegramBotClient botClient, long chatId, Message progressMessage, int progressPercentage, CancellationToken cancellationToken)
        {
            if (progressMessage != null)
            {
                var progressText = $"Import Progress: {progressPercentage}%";
                try
                {
                    // Only edit if the message content is different
                    if (progressMessage.Text != progressText)
                    {
                        // Edit the progress message with the updated progress
                        await botClient.EditMessageText(
                            chatId,
                            progressMessage.MessageId,
                            progressText,
                            cancellationToken: cancellationToken
                        );
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error editing progress message: {ex.Message}");
                }
            }
        }


        #region User Creation and Update Methods

        // In-memory list to simulate database or user storage
        private List<CustomerMonitoringApp.Domain.Entities.User> _users = new List<CustomerMonitoringApp.Domain.Entities.User>();


        public CustomerMonitoringApp.Domain.Entities.User CreateUserFromRow(
            ExcelWorksheet worksheet,
            int row,
            long chatId,
            List<string> errors)
        {
            // Retrieve and trim values from the worksheet
            var userPhone = worksheet.Cells[row, 1].Text.Trim();

            var userName = worksheet.Cells[row, 2].Text.Trim();
            var userFamily = worksheet.Cells[row, 3].Text.Trim();
            var userFatherName = worksheet.Cells[row, 4].Text.Trim();
            var userBirthDayString = worksheet.Cells[row, 5].Text.Trim();
            var userAddress = worksheet.Cells[row, 6].Text.Trim();
            var userDescription = worksheet.Cells[row, 7].Text.Trim();
            var userSource = worksheet.Cells[row, 9].Text.Trim();

            // Create and return the user with default values as necessary
            return CreateNewUserFromRow(
                userPhone,
                userName,
                userFamily,
                userFatherName,
                userBirthDayString,
                userAddress,
                userDescription,
                userSource,
                chatId,row);
        }


        public CustomerMonitoringApp.Domain.Entities.User CreateNewUserFromRow(
            string userPhone,
            string userName,
            string userFamily,
            string userFatherName,
            string userBirthDay,
            string userAddress,
            string userDescription,
            string userSource,
            long chatId,
            int row)
        {
            // Log to verify values being passed
            Console.WriteLine($"Row {row}: Creating user with phone '{userPhone}'");

            return new CustomerMonitoringApp.Domain.Entities.User
            {
                UserNumberFile = !string.IsNullOrEmpty(userPhone) ? userPhone : "NoPhone",
                UserNameFile = !string.IsNullOrEmpty(userName) ? userName : "NoName",
                UserFamilyFile = !string.IsNullOrEmpty(userFamily) ? userFamily : "NoFamily",
                UserFatherNameFile = !string.IsNullOrEmpty(userFatherName) ? userFatherName : "NoFatherName",
                UserBirthDayFile = !string.IsNullOrEmpty(userBirthDay) ? userBirthDay : "NoBirthDay",
                UserAddressFile = !string.IsNullOrEmpty(userAddress) ? userAddress : "NoAddress",
                UserDescriptionFile = !string.IsNullOrEmpty(userDescription) ? userDescription : "NoDescription",
                UserSourceFile = !string.IsNullOrEmpty(userSource) ? userSource : "NoSource",
                UserTelegramID = chatId
            };
        }


        // Helper method to find users by phone number
        private List<CustomerMonitoringApp.Domain.Entities.User> FindUsersByPhoneNumber(string phoneNumber)
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
        //        
        /// <summary>
        /// It incorporates enhanced stability measures, better error handling, and user notifications through a Telegram bot.
        /// </summary>
        /// <param name="usersToImport"></param>
        /// <param name="botClient"></param>
        /// <param name="chatId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task SaveUsersToDatabase(List<CustomerMonitoringApp.Domain.Entities.User> usersToImport, ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken,string path)
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
            int batchSize = 20000; // Adjust batch size for optimized memory usage and performance
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
                        await BulkInsertUsersAsync(batch, connectionString, cancellationToken,path);

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
            return true;
        }

        private async Task BulkInsertUsersAsync(List<CustomerMonitoringApp.Domain.Entities.User> users, string connectionString, CancellationToken cancellationToken,string path)
        {
            using var bulkCopy = new SqlBulkCopy(connectionString);
            using var dataTable = new DataTable();

            // Define the columns of the DataTable to match the Users table in the database
            dataTable.Columns.Add("UserNumberFile", typeof(string));
            dataTable.Columns.Add("UserNameFile", typeof(string));
            dataTable.Columns.Add("UserFamilyFile", typeof(string));
            dataTable.Columns.Add("UserFatherNameFile", typeof(string));
            dataTable.Columns.Add("UserBirthDayFile", typeof(string));
            dataTable.Columns.Add("UserAddressFile", typeof(string));
            dataTable.Columns.Add("UserDescriptionFile", typeof(string));
            dataTable.Columns.Add("UserSourceFile", typeof(string));
            dataTable.Columns.Add("UserTelegramID", typeof(long));

            foreach (var user in users)
            {

                
                // Apply logic to clean UserNumberFile
                var cleanedUserNumberFile = user.UserNumberFile;

                if (!string.IsNullOrEmpty(cleanedUserNumberFile))
                {
                    // If it starts with 98, remove the prefix and add 0 at the beginning
                    if (cleanedUserNumberFile.StartsWith("98"))
                    {
                        cleanedUserNumberFile = "0" + cleanedUserNumberFile.Substring(2);
                    }
                    // If it starts with 09, ensure it stays as 09
                    else if (!cleanedUserNumberFile.StartsWith("09"))
                    {
                        // If the number doesn't start with 0 or 09, add 0 at the beginning
                        cleanedUserNumberFile = "0" + cleanedUserNumberFile;
                    }
                }

                // Add rows to the DataTable
                dataTable.Rows.Add(
                        cleanedUserNumberFile ?? string.Empty,
                        user.UserNameFile ?? string.Empty,
                        user.UserFamilyFile ?? string.Empty,
                        user.UserFatherNameFile ?? string.Empty,
                        user.UserBirthDayFile ?? string.Empty,
                        user.UserAddressFile?.Substring(0, Math.Min(user.UserAddressFile.Length, 50)) ?? string.Empty, // Truncate if longer than 50
                        user.UserDescriptionFile ?? string.Empty,
                        user.UserSourceFile == path,
                        user.UserTelegramID ?? 0L
                );
            }

            // Ensure the mappings are set
            bulkCopy.DestinationTableName = "Users";
            bulkCopy.ColumnMappings.Add("UserNumberFile", "UserNumberFile");
            bulkCopy.ColumnMappings.Add("UserNameFile", "UserNameFile");
            bulkCopy.ColumnMappings.Add("UserFamilyFile", "UserFamilyFile");
            bulkCopy.ColumnMappings.Add("UserFatherNameFile", "UserFatherNameFile");
            bulkCopy.ColumnMappings.Add("UserBirthDayFile", "UserBirthDayFile");
            bulkCopy.ColumnMappings.Add("UserAddressFile", "UserAddressFile");
            bulkCopy.ColumnMappings.Add("UserDescriptionFile", "UserDescriptionFile");
            bulkCopy.ColumnMappings.Add("UserSourceFile", "UserSourceFile");
            bulkCopy.ColumnMappings.Add("UserTelegramID", "UserTelegramID");

            try
            {
                // Perform the bulk insert operation
                await bulkCopy.WriteToServerAsync(dataTable, cancellationToken);
              //  Log("Bulk insert completed successfully.");
            }
            catch (Exception ex)
            {
              //  Log($"Error during bulk insert: {ex.Message}");
            }
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
