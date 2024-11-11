using System.Configuration;
using System.Windows;
using CustomerMonitoringApp.Application.Services;
using CustomerMonitoringApp.Domain.Interfaces;
using CustomerMonitoringApp.Infrastructure.Data;
using CustomerMonitoringApp.Infrastructure.Repositories;
using CustomerMonitoringApp.Infrastructure.TelegramBot.Handlers;
using CustomerMonitoringApp.WPFApp;
using Hangfire;
using Hangfire.SqlServer;
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

            var services = new ServiceCollection();
            services.AddLogging();


            Services = serviceCollection.BuildServiceProvider();
            // Start Hangfire Server (background processing)
            // This ensures that background jobs will be processed
      
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
            services.AddScoped<IServiceProvider, ServiceProvider>();
            // Register AppDbContext with a connection string
            services.AddSingleton<IBackgroundJobClient, BackgroundJobClient>();

            // Register AppDbContext with the connection string retrieved from the configuration
            services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(
                  ("Data Source=.;Integrated Security=True;Encrypt=True;Trust Server Certificate=True"),
                    sqlServerOptions => sqlServerOptions
                        .EnableRetryOnFailure(3, TimeSpan.FromSeconds(10), null) // Enable automatic retries on failure
                        .CommandTimeout(180) // Set the command timeout to prevent hanging
                        .MigrationsAssembly(typeof(AppDbContext).Assembly.FullName) // Ensure migrations are applied automatically
                ));

            services.AddHangfire(configuration => configuration
                .UseSqlServerStorage("Data Source=.;Integrated Security=True;Encrypt=True;Trust Server Certificate=True",
                    new SqlServerStorageOptions
                    {
                        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                        QueuePollInterval = TimeSpan.FromSeconds(15),
                        UseRecommendedIsolationLevel = true,
                        DisableGlobalLocks = true
                    }));

            services.AddHangfireServer();

   


            services.AddSingleton<ILogger<MainWindow>, Logger<MainWindow>>();
            services.AddScoped<ICallHistoryRepository, CallHistoryRepository>();
            services.AddScoped<IUserRepository,UserRepository>();
            services.AddTransient<ICallHistoryImportService, CallHistoryImportService>();

            // Register MainWindow for Dependency Injection
            services.AddSingleton<MainWindow>();
        }
    }
}