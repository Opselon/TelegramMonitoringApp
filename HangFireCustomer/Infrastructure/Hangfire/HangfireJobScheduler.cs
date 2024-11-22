using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Extensions.DependencyInjection;

namespace HangFireCustomer.Infrastructure.Hangfire
{
    public static class HangfireJobScheduler
    {
        public static void AddHangfireServices(this IServiceCollection services, string connectionString)
        {
            services.AddHangfire(configuration => configuration
                .UseSqlServerStorage(connectionString));
            services.AddHangfireServer();
        }
    }
}