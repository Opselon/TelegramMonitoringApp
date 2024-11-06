using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Polly;

namespace CustomerMonitoringApp.Infrastructure.TelegramBot.Handlers
{
    public class TelegramMessageHandler
    {
        private readonly ITelegramBotClient _botClient;

        public TelegramMessageHandler(ITelegramBotClient botClient)
        {
            _botClient = botClient;
        }

        public async Task SendTelegramMessageAsync(long chatId, string message)
        {
            var retryPolicy = Policy
                .Handle<ApiRequestException>(ex => ex.Message.Contains("can't parse entities") || ex.Message.Contains("too many requests"))
                .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

            try
            {
                await retryPolicy.ExecuteAsync(async () =>
                {
                    await _botClient.SendMessage(
                        chatId: chatId,
                        text: message,
                        parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown
                    );
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending message to chat ID {chatId}: {ex.Message}");
            }
        }
    }
}