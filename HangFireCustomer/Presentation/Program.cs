using Hangfire;
using HangFireCustomer.Application.Telegram;
using HangFireCustomer.Infrastructure.Hangfire;
using HangFireCustomer.Infrastructure.Telegram;
using Microsoft.Extensions.DependencyInjection;

class Program
{
    static void Main(string[] args)
    {
        var services = new ServiceCollection();

        // Add Telegram Bot services
        services.AddTransient<ITelegramBotService, TelegramBotService>();
        services.AddTransient<ITelegramCommandProcessor, TelegramCommandProcessor>();

        // Add Hangfire
        services.AddHangfireServices("YourConnectionString");

        var serviceProvider = services.BuildServiceProvider();

        // Start Hangfire Dashboard
        using (var server = new BackgroundJobServer())
        {
            Console.WriteLine("Hangfire Server started. Press Enter to exit...");
            Console.ReadLine();
        }
    }
}