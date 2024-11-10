// File: Domain/Interfaces/ICallHistoryImportService.cs
using System.Threading;
using System.Threading.Tasks;

namespace CustomerMonitoringApp.Domain.Interfaces
{
    public interface ICallHistoryImportService
    {
        // Updated method to accept CancellationToken for better control over async tasks
        // Added fileName parameter to ensure that file name can be passed directly to the service
        Task ProcessExcelFileAsync(string filePath, string fileName, CancellationToken cancellationToken = default);
    }
}