// File: Domain/Interfaces/ICallHistoryImportService.cs
using System.Threading;
using System.Threading.Tasks;

namespace CustomerMonitoringApp.Domain.Interfaces
{
    public interface ICallHistoryImportService
    {
        // Updated method to accept CancellationToken for better control over async tasks
        Task ProcessExcelFileAsync(string filePath, CancellationToken cancellationToken = default);
    }
}