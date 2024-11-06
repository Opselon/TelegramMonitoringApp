using System;
using System.Threading.Tasks;
using CustomerMonitoringApp.Infrastructure.TelegramBot.Handlers;

namespace CustomerMonitoringApp.Application.Services
{
    public class NotificationService  // Change 'internal' to 'public' here
    {
        public readonly TelegramMessageHandler _telegramMessageHandler;

        // Constructor with dependency injection
        public NotificationService(TelegramMessageHandler telegramMessageHandler)
        {
            _telegramMessageHandler = telegramMessageHandler;
        }

        public async Task SendUserNotificationAsync(long chatId, string userNumber, string userName, string userFamily, string userFatherName, DateTime userBirthDay, string userAddress, string userDescription, string userSource)
        {
            try
            {
                // Create a formatted message for user information
                string message =
                    $"📋 *User Information Updated Successfully!*\n\n" +
                    $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                    $"| {"**📄 Field**",-25} | {"**📊 Value**",-30} |\n" +
                    $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                    $"| {"**🆔 User Number**",-25} | {"***" + userNumber + "***",-30} |\n" +
                    $"| {"**👤 Name**",-25} | {userName} {userFamily,-30} |\n" +
                    $"| {"**👨‍👩‍👧 Father Name**",-25} | {userFatherName,-30} |\n" +
                    $"| {"**🎂 Date of Birth**",-25} | {"***" + userBirthDay.ToShortDateString() + "***",-30} |\n" +
                    $"| {"**🏠 Address**",-25} | {userAddress,-30} |\n" +
                    $"| {"**📝 Description**",-25} | {userDescription,-30} |\n" +
                    $"| {"**🔗 Source**",-25} | {userSource,-30} |\n" +
                    $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                    $"✅ Your information has been updated successfully!";

                await _telegramMessageHandler.SendTelegramMessageAsync(chatId, message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send notification to user with chat ID: {chatId}. Error: {ex.Message}");
            }
        }
    }
}
