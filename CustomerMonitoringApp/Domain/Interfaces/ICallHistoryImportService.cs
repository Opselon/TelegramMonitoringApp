// File: Domain/Interfaces/ICallHistoryImportService.cs
using System.Threading.Tasks;

namespace CustomerMonitoringApp.Domain.Interfaces
{
    public interface ICallHistoryImportService
    {
        Task ProcessExcelFileAsync(string filePath);
    }
}