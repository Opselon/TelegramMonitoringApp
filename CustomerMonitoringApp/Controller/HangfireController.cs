using Hangfire;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Exceptions;

namespace CustomerMonitoringApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TelegramBotController : ControllerBase
    {
        private readonly ITelegramBotClient _botClient;

        // Inject Telegram bot client and Hangfire job controller
        public TelegramBotController(ITelegramBotClient botClient)
        {
            _botClient = botClient;
        }

        // Endpoint to receive updates from Telegram (e.g., via Webhook)
        [HttpPost("webhook")]
        public async Task<IActionResult> ReceiveWebhookUpdate([FromBody] Update update)
        {
            // Enqueue a job to process the update asynchronously using Hangfire
            BackgroundJob.Enqueue(() => ProcessTelegramUpdateAsync(update));

            return Ok();  // Respond back to Telegram to acknowledge receipt of the update
        }

        // Method to process incoming Telegram updates asynchronously
        public async Task ProcessTelegramUpdateAsync(Update update)
        {
            try
            {
                // Check if the update contains a message
                if (update.Message != null)
                {
                    var message = update.Message;

                    // Handle text message
                    if (message.Text != null)
                    {
                        await HandleTextMessageAsync(message);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the error (you can also implement a logger here)
                Console.WriteLine($"Error processing update: {ex.Message}");
            }
        }

        // Method to handle text messages
        private async Task HandleTextMessageAsync(Telegram.Bot.Types.Message message)
        {
            try
            {
                // Send a simple response back to the user
                string responseText = $"You sent: {message.Text}";

                await _botClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: responseText
                );

                Console.WriteLine($"Handled text message: {message.Text}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling text message: {ex.Message}");
            }
        }

        // Method to handle photo messages
    }
}
