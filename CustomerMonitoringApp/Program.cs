using System;
using System.Configuration; // Add this using directive
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Windows;
using CustomerMonitoringApp.Application.Services;
using CustomerMonitoringApp.Infrastructure.Data;
using CustomerMonitoringApp.Infrastructure.Repositories;
using CustomerMonitoringApp.Domain.Interfaces;
using CustomerMonitoringApp.WPFApp;
using Microsoft.Data.SqlClient;

namespace CustomerMonitoringApp
{
    /// <summary>
    /// The main application class for the Customer Monitoring Application.
    /// </summary>
    public partial class App : System.Windows.Application
    {
        /// <summary>
        /// Called when the application starts.
        /// Initializes the application and sets up the dependency injection container.
        /// </summary>
        /// <param name="e">The event data for the startup event.</param>
        protected override async void OnStartup(StartupEventArgs e)
        {
            // Create a host builder to set up application services and configuration
            var host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    // Retrieve the connection string from App.config
                    var connectionString = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

                    if (string.IsNullOrEmpty(connectionString))
                    {
                        throw new InvalidOperationException("The connection string 'DefaultConnection' is not configured.");
                    }

                    // Log the connection string (for debugging purposes only; avoid logging sensitive info in production)
                    Console.WriteLine($"Connection String: {connectionString}");

                    // Configure DbContext with SQL Server using the connection string
                    services.AddDbContext<AppDbContext>(options =>
                        options.UseSqlServer(connectionString));
                    services.AddLogging();
                    // Register repositories and services for dependency injection
                    services.AddScoped<IUserRepository, UserRepository>();
                    services.AddTransient<ICallHistoryRepository, CallHistoryRepository>(); // Ensure your implementation is registered
                    services.AddTransient<CallHistoryImportService>(); // Register CallHistoryImportService


                    services.AddScoped<IUserPermissionRepository, UserPermissionRepository>();
                    services.AddScoped<UserService>();
        
                    // Register the main window as a singleton
                    services.AddSingleton<MainWindow>();

                    // Configure logging
                    services.AddLogging(configure =>
                    {
                        configure.AddConsole();
                        configure.AddDebug(); // Add debug logging
                    });
                })
                .Build();

            // Ensure the database is created and migrations are applied at startup asynchronously
            await ApplyDatabaseMigrationsAsync(host);

            // Run the host, which starts the application and handles lifetime events
            await host.RunAsync(); // Use RunAsync for better async handling
        }

        /// <summary>
        /// Applies pending database migrations and ensures the database is created.
        /// </summary>
        /// <param name="host">The host that contains the services.</param>
        private async Task ApplyDatabaseMigrationsAsync(IHost host)
        {
            using (var scope = host.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<App>>();

                try
                {
                    // Log migration start
                    logger.LogInformation("Applying database migrations...");

                    // Ensure the database is created
                    await dbContext.Database.EnsureCreatedAsync();

                    // Apply any pending migrations
                    await dbContext.Database.MigrateAsync();

                    // Log migration complete
                    logger.LogInformation("Database migrations applied successfully.");
                }
                catch (SqlException sqlEx)
                {
                    // Log SQL exceptions specifically
                    logger.LogError(sqlEx, "SQL error occurred while applying migrations: {Message}", sqlEx.Message);
                }
                catch (Exception ex)
                {
                    // Log any other exceptions that occur
                    logger.LogError(ex, "Error applying migrations: {Message}", ex.Message);
                }
            }
        }
    }
}
