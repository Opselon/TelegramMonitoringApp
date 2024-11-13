using System.IO;
using System.Threading.Tasks;
using CustomerMonitoringApp.Application.DTOs;
using CustomerMonitoringApp.Domain.Entities;
using CustomerMonitoringApp.Domain.Interfaces;
using CustomerMonitoringApp.Infrastructure.Services;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using User = CustomerMonitoringApp.Domain.Entities.User;

namespace CustomerMonitoringApp.Application.Commands
{
    /// <summary>
    /// Handles incoming commands and messages from Telegram.
    /// </summary>
    public class CommandHandler
    {
        private readonly ITelegramBotClient _botClient;
        private readonly IUserRepository _userRepository;

        /// <summary>
        /// Initializes a new instance of the <see cref="CommandHandler"/> class.
        /// </summary>
        /// <param name="botClient">The Telegram bot client.</param>
        /// <param name="userRepository">The user repository for database operations.</param>
        public CommandHandler(ITelegramBotClient botClient, IUserRepository userRepository)
        {
            _botClient = botClient;
            _userRepository = userRepository;
        }

        /// <summary>
        /// Handles incoming messages and directs them to the appropriate method.
        /// </summary>
        /// <param name="message">The incoming Telegram message.</param>
        public async Task HandleIncomingMessageAsync(Message message)
        {
            if (message.Type == MessageType.Document)
            {
                await HandleFileAsync(message);
            }
            // Handle other message types here...
        }

        /// <summary>
        /// Processes file messages and adds users from the file to the database.
        /// </summary>
        /// <param name="message">The incoming message containing a file.</param>
        private async Task HandleFileAsync(Message message)
        {
            if (message.Type == MessageType.Document && message.Document != null)
            {
                try
                {
                    // Get the file path from Telegram
                    var fileInfo = await _botClient.GetFile(message.Document.FileId);

                    // Create a stream to save the file
                    using (var fileStream = new FileStream(fileInfo.FilePath, FileMode.Create))
                    {
                        // Download the file from Telegram
                        await _botClient.DownloadFile(fileInfo.FilePath, fileStream);
                    }

                    // Use the ExcelReaderService to parse the Excel file
                    var excelReader = new ExcelReaderService();
                    var users = excelReader.ParseExcelFile(fileInfo.FilePath);

                    // Create a list of tasks to process user additions in parallel
                    var userTasks = users.Select(userDto =>
                    {
                        var user = new User
                        {
                            UserTelegramID = userDto.UserTelegramID,
                            UserNameProfile = userDto.UserNameProfile,
                            UserNumberFile = userDto.UserNumberFile,
                            UserNameFile = userDto.UserNameFile,
                            UserFamilyFile = userDto.UserFamilyFile,
                            UserFatherNameFile = userDto.UserFatherNameFile,
                            UserBirthDayFile = userDto.UserBirthDayFile,
                            UserAddressFile = userDto.UserAddressFile,
                            UserDescriptionFile = userDto.UserDescriptionFile,
                            UserSourceFile = userDto.UserSourceFile,
                        };

                        // Add the user to the database asynchronously
                        return _userRepository.AddUserAsync(user);
                    }).ToList();

                    // Wait for all the user insertions to complete in parallel
                    await Task.WhenAll(userTasks);

                    // Notify the user that the file has been processed
                    await _botClient.SendMessage(message.Chat.Id, "File processed and users added to the database.");
                }
                catch (Exception ex)
                {
                    // Log the exception and notify the user of the error
                    await _botClient.SendMessage(message.Chat.Id, "An error occurred while processing the file.");
                    // Log the exception details (e.g., using a logging framework)
                    // _logger.LogError(ex, "Error processing file and adding users.");
                }
            }
        }
    }
}
