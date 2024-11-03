using System.Collections.Generic;
using System.Threading.Tasks;
using CustomerMonitoringApp.Domain.Entities;

namespace CustomerMonitoringApp.Domain.Interfaces
{
    public interface IUserPermissionRepository
    {
        Task<IEnumerable<UserPermission>> GetPermissionsByUserIdAsync(long userId);
        Task AddPermissionAsync(UserPermission permission);
    }
}