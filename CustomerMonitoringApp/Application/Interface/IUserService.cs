using System.Collections.Generic;
using System.Threading.Tasks;
using CustomerMonitoringApp.Domain.Entities;

namespace CustomerMonitoringApp.Application.Interfaces
{
    public interface IUserService
    {
        Task<User> GetUserByTelegramIdAsync(long telegramId);
        Task<IEnumerable<User>> GetAllUsersAsync();

        Task AddUserAsync(User user);

        Task UpdateUserAsync(User user);

        Task DeleteUserAsync(int userId);

        Task<User> GetUserByPhoneNumberAsync(string phoneNumber);
    }
}