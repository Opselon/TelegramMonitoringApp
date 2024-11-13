using System.Configuration;
using System.Net.Http;
using System.Windows;
using CustomerMonitoringApp.Application.Services;
using CustomerMonitoringApp.Domain.Interfaces;
using CustomerMonitoringApp.Infrastructure.Data;
using CustomerMonitoringApp.Infrastructure.Repositories;
using CustomerMonitoringApp.Infrastructure.TelegramBot.Handlers;
using CustomerMonitoringApp.WPFApp;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Telegram.Bot;
using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;

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


     ;

            Services = serviceCollection.BuildServiceProvider();

            // Start Hangfire Server (background processing)
            var hangfireServer = Services.GetRequiredService<Hangfire.IBackgroundJobClient>(); // This should now work
            Services = serviceCollection.BuildServiceProvider();

            //  mainWindow.Show();

            base.OnStartup(e);
        }
        public void Configure(IApplicationBuilder app, IHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            #region Additional Middleware Configuration for Large Request Handling

            // Enable buffering of large responses to avoid issues with buffering in middleware.
            app.Use(async (context, next) =>
            {
                // Enable response buffering
                context.Response.Body = new System.IO.MemoryStream();
                await next.Invoke();
            });

            // Note: If you use request/response logging, consider enabling buffering for large requests
            // to avoid premature disposal of request streams.

            #endregion

            app.UseRouting();

            // Additional middleware registrations if needed

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }

        [STAThread]
        private void ConfigureServices(IServiceCollection services)
        {

            // Register services for dependency injection
            services.AddSingleton<TelegramMessageHandler>();
            services.AddSingleton<NotificationService>();

 
            // Set the maximum request body size to handle large requests.
            services.Configure<IISServerOptions>(options =>
            {
                // Unlimited request body size.
                options.MaxRequestBodySize = null;
            });

            // Configure Kestrel options to handle large requests.
            services.Configure<KestrelServerOptions>(options =>
            {
                // Set maximum request body size to unlimited (use long.MaxValue).
                options.Limits.MaxRequestBodySize = long.MaxValue;

                // Extend time limits for reading headers and requests, useful for large requests.
                options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(10);
                options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
            });

            // Configure IIS options if hosted in IIS.
     

            // Configure FormOptions to handle large file uploads in multipart forms.
            services.Configure<FormOptions>(options =>
            {
                // Set the maximum allowable size for multipart form data (1 GB example).
                options.MultipartBodyLengthLimit = 1024 * 1024 * 1024; // 1GB

                // Enable buffering to handle large form data uploads.
                options.BufferBody = true;
            });
            
            
            // Optionally, you can increase buffer limits for responses if dealing with large responses.
            services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(options =>
            {
                // Enable buffering for large responses.
                options.RespectBrowserAcceptHeader = true;
            });
        

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



            

            // Add Polly policies with HttpClient
            services.AddHttpClient("RetryPolicy")
                .AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(600))) // Timeout for each request
                .AddPolicyHandler(Policy<HttpResponseMessage>
                    .Handle<HttpRequestException>() // Only handle HttpRequestExceptions
                    .OrResult(msg => !msg.IsSuccessStatusCode) // Retry on non-success HTTP status codes
                    .RetryAsync(3)) // Retry policy, tries 3 times
                .AddPolicyHandler(Policy<HttpResponseMessage>
                    .Handle<HttpRequestException>()
                    .OrResult(msg => !msg.IsSuccessStatusCode) // Circuit breaker for HTTP failures
                    .CircuitBreakerAsync(5, TimeSpan.FromMinutes(1))); // Break circuit after 5 consecutive failures


            services.AddSingleton<ILogger<MainWindow>, Logger<MainWindow>>();
            services.AddScoped<ICallHistoryRepository, CallHistoryRepository>();
            services.AddScoped<IUserRepository,UserRepository>();
            services.AddTransient<ICallHistoryImportService, CallHistoryImportService>();

            // Register MainWindow for Dependency Injection
            services.AddSingleton<MainWindow>();
        }
    }
}