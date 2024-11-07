using System.Windows;
using CustomerMonitoringApp.Application.Services;
using CustomerMonitoringApp.Domain.Interfaces;
using CustomerMonitoringApp.Infrastructure.Data;
using CustomerMonitoringApp.Infrastructure.Repositories;
using CustomerMonitoringApp.Infrastructure.TelegramBot.Handlers;
using CustomerMonitoringApp.WPFApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;

namespace CustomerMonitoringApp
{
    public partial class App : System.Windows.Application
    {
        public static IServiceProvider Services { get; private set; }


        protected override void OnStartup(StartupEventArgs e)
        {
            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);

            Services = serviceCollection.BuildServiceProvider();

            // Manually resolve the MainWindow from the DI container and show it
            //var mainWindow = Services.GetRequiredService<MainWindow>();
            //  mainWindow.Show();

            base.OnStartup(e);
        }

        [STAThread]
        private void ConfigureServices(IServiceCollection services)
        {
            // Register services for dependency injection
            services.AddSingleton<TelegramMessageHandler>();
            services.AddSingleton<NotificationService>();

            // Register ITelegramBotClient with the DI container
            services.AddSingleton<ITelegramBotClient>(new TelegramBotClient("6768055952:AAGSETUCUC76eXuSoAGX6xcsQk1rrt0K4Ng"));

            // Add logging services
            services.AddLogging(configure => configure.AddConsole());

            // Register AppDbContext with a connection string
            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer("Data Source=.;Integrated Security=True;Encrypt=True;Trust Server Certificate=True"));

            // Register other required services
            services.AddSingleton<ILogger<MainWindow>, Logger<MainWindow>>();
            services.AddScoped<ICallHistoryRepository, CallHistoryRepository>();
            services.AddScoped<IServiceProvider, ServiceProvider>();
            services.AddTransient<ICallHistoryImportService, CallHistoryImportService>();

            // Register MainWindow for Dependency Injection
            services.AddSingleton<MainWindow>();
        }
    }
}