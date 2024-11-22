using Hangfire;
using System;
using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace CustomerMonitoringAppWpf.Services
{
    public class TelegramCommandHandler
    {
        // Queue the incoming Telegram update for background processing
        public void QueueUpdate(Update update)
        {
            if (update == null)
            {
                Console.WriteLine("Error: Received null update. Skipping processing.");
                return;
            }

            Console.WriteLine($"Queuing update for chat ID: {update.Message?.Chat.Id}");
            BackgroundJob.Enqueue(() => ProcessUpdateAsync(update));
        }

        // Process the queued update in a background job
        public async Task ProcessUpdateAsync(Update update)
        {
            try
            {
                if (update.Message != null)
                {
                    var message = update.Message;

                    // Handle text messages
                    if (!string.IsNullOrEmpty(message.Text))
                    {
                        Console.WriteLine($"Processing text message: {message.Text}");
                        await HandleTextMessageAsync(message);
                    }
                    else
                    {
                        Console.WriteLine("Received a non-text message. Skipping.");
                    }
                }
                else
                {
                    Console.WriteLine("Received update with no message content.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing update: {ex.Message}");
            }
        }

        // Handle text messages
        private async Task HandleTextMessageAsync(Message message)
        {
            try
            {
                // Log message details
                Console.WriteLine($"Received message from Chat ID: {message.Chat.Id}");
                Console.WriteLine($"Message Text: {message.Text}");

                // Simulate a response action (replace with actual bot logic)
                Console.WriteLine($"Processed message: {message.Text}");

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling text message: {ex.Message}");
            }
        }
    }
}
