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

        // Additional Methods for Useful Queries

        Task<List<User>> GetUsersByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken);
        Task<List<User>> GetAllUsersAsync(CancellationToken cancellationToken);
        Task<List<CallHistory>> GetCallHistoryByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken);
        Task<int> GetCallHistoryCountAsync(CancellationToken cancellationToken);
        Task<int> GetTotalUserCountAsync();
        // 1. Retrieve user by phone number.
        Task<User?> GetUserByPhoneNumberAsync(string phoneNumber);

        // 2. Find all users born before a specific year.
        Task<IEnumerable<User>> GetUsersBornBeforeYearAsync(int year);

        // 3. Retrieve all users from a specific source.
        Task<IEnumerable<User>> GetUsersBySourceAsync(int source);

        // 4. Count users grouped by source.
        Task<Dictionary<int, int>> CountUsersBySourceAsync();

        // 5. Retrieve users by first name.
        Task<IEnumerable<User>> GetUsersByFirstNameAsync(string firstName);

        // 6. Retrieve users whose last name contains a specific substring.
        Task<IEnumerable<User>> GetUsersByLastNameContainsAsync(string substring);

        // 7. Find users born in a specific month.
        Task<IEnumerable<User>> GetUsersByBirthMonthAsync(int month);

        // 8. Retrieve users whose address contains a specific city or location.
        Task<IEnumerable<User>> GetUsersByAddressContainsAsync(string location);

        // 9. Count users by birth year.
        Task<Dictionary<int, int>> CountUsersByBirthYearAsync();

        // 10. Retrieve users with missing or null father’s names.
        Task<IEnumerable<User>> GetUsersWithMissingFatherNameAsync();

        // 11. Retrieve users with duplicate phone numbers.
        Task<IEnumerable<string>> GetDuplicatePhoneNumbersAsync();

        // 12. Retrieve users with a specific last name.
        Task<IEnumerable<User>> GetUsersByLastNameAsync(string lastName);

        // 13. Retrieve users by a range of birth years.
        Task<IEnumerable<User>> GetUsersByBirthYearRangeAsync(int startYear, int endYear);

        // 15. Retrieve users in alphabetical order by last name.
        Task<IEnumerable<User>> GetUsersOrderedByLastNameAsync();

        // 16. Find the most common first name.
        Task<string?> GetMostCommonFirstNameAsync();

        // 17. Retrieve users who do not have a source specified.
        Task<IEnumerable<User>> GetUsersWithNoSourceAsync();

        // 18. Retrieve users with an address starting with a specific prefix.
        Task<IEnumerable<User>> GetUsersByAddressPrefixAsync(string prefix);

        // 19. Retrieve users born on a specific date.
        Task<IEnumerable<User>> GetUsersByBirthDateAsync(string birthDate);

        // 20. Find users by partial or exact name match (first, last, or father name).
        Task<IEnumerable<User>> GetUsersByNameMatchAsync(string name);
    }
}
