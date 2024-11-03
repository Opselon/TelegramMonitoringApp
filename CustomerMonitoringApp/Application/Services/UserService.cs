using System.Collections.Generic;
using System.Threading.Tasks;
using CustomerMonitoringApp.Domain.Entities;
using CustomerMonitoringApp.Application.Interfaces;
using CustomerMonitoringApp.Domain.Interfaces; // Correct namespace for the interface

namespace CustomerMonitoringApp.Application.Services
{
    public class UserService : IUserService // Implement the IUserService interface
    {
        private readonly IUserRepository _userRepository;

        public UserService(IUserRepository userRepository)
        {
            _userRepository = userRepository;
        }

        public async Task<User> GetUserByTelegramIdAsync(long telegramId)
        {
            return await _userRepository.GetUserByTelegramIdAsync(telegramId);
        }

        public async Task<IEnumerable<User>> GetAllUsersAsync()
        {
            return await _userRepository.GetAllUsersAsync();
        }

        public async Task AddUserAsync(User user)
        {
            await _userRepository.AddUserAsync(user);
        }

        public async Task UpdateUserAsync(User user)
        {
            await _userRepository.UpdateUserAsync(user);
        }

        public async Task DeleteUserAsync(int userId)
        {
            await _userRepository.DeleteUserAsync(userId);
        }
    }
}