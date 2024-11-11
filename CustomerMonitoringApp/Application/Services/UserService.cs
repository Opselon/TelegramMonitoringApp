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

        // Additional Methods for Useful Queries

        // 1. Retrieve user by phone number.
        public async Task<User?> GetUserByPhoneNumberAsync(string phoneNumber)
        {
            return await _userRepository.GetUserByPhoneNumberAsync(phoneNumber);
        }

        // 2. Find all users born before a specific year.
        public async Task<IEnumerable<User>> GetUsersBornBeforeYearAsync(int year)
        {
            return await _userRepository.GetUsersBornBeforeYearAsync(year);
        }

        // 3. Retrieve all users from a specific source.
        public async Task<IEnumerable<User>> GetUsersBySourceAsync(int source)
        {
            return await _userRepository.GetUsersBySourceAsync(source);
        }

        // 4. Count users grouped by source.
        public async Task<Dictionary<int, int>> CountUsersBySourceAsync()
        {
            return await _userRepository.CountUsersBySourceAsync();
        }

        // 5. Retrieve users by first name.
        public async Task<IEnumerable<User>> GetUsersByFirstNameAsync(string firstName)
        {
            return await _userRepository.GetUsersByFirstNameAsync(firstName);
        }

        // 6. Retrieve users whose last name contains a specific substring.
        public async Task<IEnumerable<User>> GetUsersByLastNameContainsAsync(string substring)
        {
            return await _userRepository.GetUsersByLastNameContainsAsync(substring);
        }

        // 7. Find users born in a specific month.
        public async Task<IEnumerable<User>> GetUsersByBirthMonthAsync(int month)
        {
            return await _userRepository.GetUsersByBirthMonthAsync(month);
        }

        // 8. Retrieve users whose address contains a specific city or location.
        public async Task<IEnumerable<User>> GetUsersByAddressContainsAsync(string location)
        {
            return await _userRepository.GetUsersByAddressContainsAsync(location);
        }

        // 9. Count users by birth year.
        public async Task<Dictionary<int, int>> CountUsersByBirthYearAsync()
        {
            return await _userRepository.CountUsersByBirthYearAsync();
        }

        // 10. Retrieve users with missing or null father’s names.
        public async Task<IEnumerable<User>> GetUsersWithMissingFatherNameAsync()
        {
            return await _userRepository.GetUsersWithMissingFatherNameAsync();
        }

        // 11. Retrieve users with duplicate phone numbers.
        public async Task<IEnumerable<string>> GetDuplicatePhoneNumbersAsync()
        {
            return await _userRepository.GetDuplicatePhoneNumbersAsync();
        }

        // 12. Retrieve users with a specific last name.
        public async Task<IEnumerable<User>> GetUsersByLastNameAsync(string lastName)
        {
            return await _userRepository.GetUsersByLastNameAsync(lastName);
        }

        // 13. Retrieve users by a range of birth years.
        public async Task<IEnumerable<User>> GetUsersByBirthYearRangeAsync(int startYear, int endYear)
        {
            return await _userRepository.GetUsersByBirthYearRangeAsync(startYear, endYear);
        }

        // 14. Count total users.
        public async Task<int> GetTotalUserCountAsync()
        {
            return await _userRepository.GetTotalUserCountAsync();
        }

        // 15. Retrieve users in alphabetical order by last name.
        public async Task<IEnumerable<User>> GetUsersOrderedByLastNameAsync()
        {
            return await _userRepository.GetUsersOrderedByLastNameAsync();
        }

        // 16. Find the most common first name.
        public async Task<string?> GetMostCommonFirstNameAsync()
        {
            return await _userRepository.GetMostCommonFirstNameAsync();
        }

        // 17. Retrieve users who do not have a source specified.
        public async Task<IEnumerable<User>> GetUsersWithNoSourceAsync()
        {
            return await _userRepository.GetUsersWithNoSourceAsync();
        }

        // 18. Retrieve users with an address starting with a specific prefix.
        public async Task<IEnumerable<User>> GetUsersByAddressPrefixAsync(string prefix)
        {
            return await _userRepository.GetUsersByAddressPrefixAsync(prefix);
        }

        // 19. Retrieve users born on a specific date.
        public async Task<IEnumerable<User>> GetUsersByBirthDateAsync(string birthDate)
        {
            return await _userRepository.GetUsersByBirthDateAsync(birthDate);
        }

        // 20. Find users by partial or exact name match (first, last, or father name).
        public async Task<IEnumerable<User>> GetUsersByNameMatchAsync(string name)
        {
            return await _userRepository.GetUsersByNameMatchAsync(name);
        }
    }
}
