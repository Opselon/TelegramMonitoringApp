using System;
using System.Threading.Tasks;

namespace HangFireCustomer.Infrastructure.Telegram
{
    public class TelegramBotService : ITelegramBotService
    {
        public async Task ProcessCommandAsync(string commandText, long chatId)
        {
            // Simulate processing of commands
            Console.WriteLine($"Processing command: {commandText} for Chat ID: {chatId}");
            await Task.Delay(100); // Simulate async work
        }
    }
}