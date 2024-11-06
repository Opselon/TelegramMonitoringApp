using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using CustomerMonitoringApp.Infrastructure.Data;
using CustomerMonitoringApp.Domain.Interfaces;
using CustomerMonitoringApp.Infrastructure.Repositories;
using CustomerMonitoringApp.Application.Services;
using CustomerMonitoringApp.WPFApp;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        // Initialize the host and configure services
        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((context, config) =>
            {
                // Set the base path for configuration and load App.config for connection string and other settings
                config.SetBasePath(Directory.GetCurrentDirectory())
                    .AddXmlFile("App.config", optional: false, reloadOnChange: true);
            })
            .ConfigureServices((context, services) =>
            {
                // Register the DbContext with the connection string from configuration
                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlServer(context.Configuration.GetConnectionString("DefaultConnection")));

                // Register repositories and services
                services.AddScoped<IUserRepository, UserRepository>();
                services.AddScoped<ICallHistoryRepository, CallHistoryRepository>();
                services.AddScoped<UserService>(); // Add other application services
                services.AddScoped<CallHistoryImportService>();
                // Register MainWindow as a singleton (WPF's main window)
                services.AddSingleton<MainWindow>();

                // Register logging
                services.AddLogging(builder => builder.AddConsole());
            })
            .Build();
    }

    // On startup, ensure the host starts and MainWindow is shown
    protected override async void OnStartup(StartupEventArgs e)
    {
        try
        {
            // Start the host asynchronously
            await _host.StartAsync();

            // Retrieve MainWindow from DI and show it
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();

            // Log that the application started successfully
            var logger = _host.Services.GetRequiredService<ILogger<App>>();
            logger.LogInformation("Application started successfully.");
        }
        catch (Exception ex)
        {
            // Handle errors during startup
            var logger = _host.Services.GetRequiredService<ILogger<App>>();
            logger.LogError(ex, "An error occurred during startup.");
            MessageBox.Show($"An error occurred during startup: {ex.Message}");
            Environment.Exit(1); // Exit application on failure
        }

        base.OnStartup(e);
    }

    // Proper cleanup of services when the application exits
    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            // Log that the application is stopping
            var logger = _host.Services.GetRequiredService<ILogger<App>>();
            logger.LogInformation("Application is stopping.");

            // Stop the host asynchronously and dispose of it
            await _host.StopAsync();
        }
        catch (Exception ex)
        {
            // Handle errors on exit
            var logger = _host.Services.GetRequiredService<ILogger<App>>();
            logger.LogError(ex, "An error occurred during shutdown.");
            MessageBox.Show($"An error occurred during exit: {ex.Message}");
        }
        finally
        {
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
