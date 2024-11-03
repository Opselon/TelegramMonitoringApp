using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CustomerMonitoringApp.Application.Services;
using CustomerMonitoringApp.Infrastructure.Data;
using CustomerMonitoringApp.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Windows;
using CustomerMonitoringApp.Domain.Interfaces;
using CustomerMonitoringApp.WPFApp;

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
        protected override void OnStartup(StartupEventArgs e)
        {
            // Create a host builder to set up application services and configuration
            var host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    // Configure the DbContext with SQL Server using a connection string
                    services.AddDbContext<AppDbContext>(options =>
                        options.UseSqlServer("Data Source=.;Integrated Security=True;Trust Server Certificate=True")); // Replace with your actual connection string

                    // Register the repositories for dependency injection
                    services.AddScoped<IUserRepository, UserRepository>(); // Scoped lifetime for user repository
                    services.AddScoped<IUserPermissionRepository, UserPermissionRepository>(); // Scoped lifetime for user permission repository

                    // Register application services
                    services.AddScoped<UserService>(); // Scoped lifetime for user service

                    // Register the main window as a singleton to ensure one instance exists
                    services.AddSingleton<MainWindow>();
                })
                .Build(); // Build the host with the configured services

            // Ensure the database is created and migrations are applied at startup
            using (var scope = host.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                // Apply any pending migrations to the database
                dbContext.Database.Migrate();
            }

            // Resolve the main window from the service provider and show it
            var mainWindow = host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();

            // Run the host, which starts the application and handles lifetime events
            host.Run();
        }
    }
}
