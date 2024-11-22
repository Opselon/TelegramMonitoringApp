using Hangfire;
using HangFireCustomer.Infrastructure.Telegram;

namespace HangFireCustomer.Application.Telegram
{
    public class TelegramCommandProcessor : ITelegramCommandProcessor
    {
        public void EnqueueCommand(string commandText, long chatId)
        {
            // Use Hangfire to enqueue the command for background processing
            BackgroundJob.Enqueue<ITelegramBotService>(botService =>
                botService.ProcessCommandAsync(commandText, chatId));
        }
    }
}