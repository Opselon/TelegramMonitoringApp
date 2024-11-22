using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using CustomerMonitoringApp.Application.DTOs;
using Telegram.Bot;

namespace CustomerMonitoringApp.Infrastructure.Services
{
    public class CommandJobService
    {
        private readonly ConcurrentDictionary<long, UserState> _userStates;
        private readonly ITelegramBotClient _botClient;

        public CommandJobService(ConcurrentDictionary<long, UserState> userStates, ITelegramBotClient botClient)
        {
            _userStates = userStates;
            _botClient = botClient;
        }

        /// <summary>
        /// Execute a generic Hangfire job with access to user state and the bot client.
        /// </summary>
        /// <param name="chatId">Chat ID for the job.</param>
        /// <param name="jobAction">The asynchronous action to perform as part of the job.</param>
        public async Task ExecuteJob(long chatId, Func<UserState, ITelegramBotClient, CancellationToken, Task> jobAction)
        {
            try
            {
                UserState userState = ResolveUserState(chatId);

                // Pass the resolved bot client, user state, and cancellation token to the job action
                await jobAction(userState, _botClient, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing Hangfire job for ChatId {chatId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Resolve or create a UserState for the specified chat ID.
        /// </summary>
        /// <param name="chatId">Chat ID to resolve.</param>
        /// <returns>UserState instance for the chat.</returns>
        private UserState ResolveUserState(long chatId)
        {
            return _userStates.GetOrAdd(chatId, new UserState { ChatId = chatId });
        }
    }
}