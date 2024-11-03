using System.Collections.Generic;
using System.Threading.Tasks;
using CustomerMonitoringApp.Domain.Entities;

namespace CustomerMonitoringApp.Domain.Interfaces
{
    public interface IUserRepository
    {
        Task<User?> GetUserByTelegramIdAsync(long telegramId);
        Task<IEnumerable<User>> GetAllUsersAsync();
        Task AddUserAsync(User user);
        Task UpdateUserAsync(User user);
        Task DeleteUserAsync(int userId);
    }
}