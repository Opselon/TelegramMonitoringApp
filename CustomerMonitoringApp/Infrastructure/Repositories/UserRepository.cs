using CustomerMonitoringApp.Domain.Entities;
using CustomerMonitoringApp.Domain.Interfaces;
using CustomerMonitoringApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CustomerMonitoringApp.Infrastructure.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly AppDbContext _context;

        public UserRepository(AppDbContext context)
        {
            _context = context;

        }

        public async Task<User?> GetUserByTelegramIdAsync(long telegramId)
        {
            return null;
        }

        public async Task<IEnumerable<User>> GetAllUsersAsync()
        {
            return null;
        }

        public async Task AddUserAsync(User user)
        {
            await _context.Users.AddAsync(user);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateUserAsync(User user)
        {
            _context.Users.Update(user);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteUserAsync(int userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user != null)
            {
                _context.Users.Remove(user);
                await _context.SaveChangesAsync();
            }
        }




        public async Task<User?> GetUserByPhoneNumberAsync(string phoneNumber)
        {
            try
            {
                // Assuming there is a relation between phone numbers and users.
                // If you store phone numbers in a separate table, join them accordingly.
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserNumberFile == phoneNumber); // Or use a proper relationship
                return user;
            }
            catch (Exception ex)
            {
                // Handle logging the error here
                throw new Exception("An error occurred while fetching user by phone number.", ex);
            }
        }




        public Task<IEnumerable<User>> GetUsersBornBeforeYearAsync(int year)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<User>> GetUsersBySourceAsync(int source)
        {
            throw new NotImplementedException();
        }

        public Task<Dictionary<int, int>> CountUsersBySourceAsync()
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<User>> GetUsersByFirstNameAsync(string firstName)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<User>> GetUsersByLastNameContainsAsync(string substring)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<User>> GetUsersByBirthMonthAsync(int month)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<User>> GetUsersByAddressContainsAsync(string location)
        {
            throw new NotImplementedException();
        }

        public Task<Dictionary<int, int>> CountUsersByBirthYearAsync()
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<User>> GetUsersWithMissingFatherNameAsync()
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<string>> GetDuplicatePhoneNumbersAsync()
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<User>> GetUsersByLastNameAsync(string lastName)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<User>> GetUsersByBirthYearRangeAsync(int startYear, int endYear)
        {
            throw new NotImplementedException();
        }

        public Task<int> GetTotalUserCountAsync()
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<User>> GetUsersOrderedByLastNameAsync()
        {
            throw new NotImplementedException();
        }

        public Task<string?> GetMostCommonFirstNameAsync()
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<User>> GetUsersWithNoSourceAsync()
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<User>> GetUsersByAddressPrefixAsync(string prefix)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<User>> GetUsersByBirthDateAsync(string birthDate)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<User>> GetUsersByNameMatchAsync(string name)
        {
            throw new NotImplementedException();
        }
    }
}