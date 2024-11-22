// File: Domain/Interfaces/ICallHistoryImportService.cs
using System.Threading;
using System.Threading.Tasks;

namespace CustomerMonitoringApp.Domain.Interfaces
{


    public interface ICallHistoryImportService
    {



        /// <summary>
        /// 
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="fileName"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task ProcessExcelFileAsync(string filePath, string fileName, CancellationToken cancellationToken = default);



    }



}