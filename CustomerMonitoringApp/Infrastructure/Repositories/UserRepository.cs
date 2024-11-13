using CustomerMonitoringApp.Domain.Entities;
using CustomerMonitoringApp.Domain.Interfaces;
using CustomerMonitoringApp.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Polly;

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
            try
            {
                // Use retry policy for I/O operations
                var retryPolicy = Policy
                    .Handle<SqlException>()
                    .Or<TimeoutException>()
                    .WaitAndRetryAsync(
                        3,
                        attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))
                    );

                // Using retry policy to execute the query
                return await retryPolicy.ExecuteAsync(async () =>
                {
                    // Efficiently fetch all users, applying AsNoTracking for performance on read-only operations
                    return await _context.Users
                        .AsNoTracking()  // Improve performance for read-only operations
                        .ToListAsync();  // Efficiently fetch all users from the database
                });
            }
            catch (Exception ex)
            {
                // Handle any error that occurs during the operation
                throw new ApplicationException("Error retrieving all users.", ex);
            }
        }


        public async Task AddUserAsync(User user)
        {
            try
            {
                // Retry policy for transient I/O errors (e.g., network issues, SQL server timeouts)
                var retryPolicy = Policy
                    .Handle<SqlException>()  // Handle SQL exceptions (e.g., deadlocks, timeouts)
                    .Or<TimeoutException>()  // Handle timeout exceptions specifically
                    .WaitAndRetryAsync(
                        3,  // Retry 3 times
                        attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))  // Exponential backoff (1s, 2s, 4s)
                    );

                // Execute the retry policy logic for adding the user
                await retryPolicy.ExecuteAsync(async () =>
                {
                    // Start a new transaction for adding the user
                    using var transaction = await _context.Database.BeginTransactionAsync();

                    try
                    {
                        // Add the new user
                        await _context.Users.AddAsync(user);

                        // Save the changes to the database
                        await _context.SaveChangesAsync();

                        // Commit the transaction if no error occurs
                        await transaction.CommitAsync();
                    }
                    catch (Exception ex)
                    {
                        // In case of an error, roll back the transaction to ensure consistency
                        await transaction.RollbackAsync();

                        // Log the exception (optional)
                        //_logger.LogError($"Error while adding user: {ex.Message}");

                        // Rethrow the exception with additional context
                        throw new ApplicationException("Failed to add user. Transaction rolled back.", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                // Log the failure (optional)
                //_logger.LogError($"Failed to add user after retries: {ex.Message}");

                // Re-throw the exception with more context
                throw new ApplicationException("Error during user addition process.", ex);
            }
        }



        public async Task UpdateUserAsync(User user)
        {
            try
            {
                // Use retry policy for I/O operations
                var retryPolicy = Policy
                    .Handle<SqlException>()
                    .Or<TimeoutException>()
                    .WaitAndRetryAsync(
                        3,
                        attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))
                    );

                await retryPolicy.ExecuteAsync(async () =>
                {
                    // Update the existing user and save changes
                    _context.Users.Update(user);
                    await _context.SaveChangesAsync();
                });
            }
            catch (Exception ex)
            {
                // Handle errors during the user update process
                throw new ApplicationException("Error updating user.", ex);
            }
        }



        public async Task DeleteUserAsync(int userId)
        {
            try
            {
                // Use retry policy for I/O operations
                var retryPolicy = Policy
                    .Handle<SqlException>()
                    .Or<TimeoutException>()
                    .WaitAndRetryAsync(
                        3,
                        attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))
                    );

                await retryPolicy.ExecuteAsync(async () =>
                {
                    // Find the user to delete
                    var user = await _context.Users.FindAsync(userId);

                    if (user != null)
                    {
                        _context.Users.Remove(user);
                        await _context.SaveChangesAsync();
                    }
                });
            }
            catch (Exception ex)
            {
                // Handle errors during the user deletion process
                throw new ApplicationException($"Error deleting user with ID {userId}.", ex);
            }
        }


        public async Task<User?> GetUserByPhoneNumberAsync(string phoneNumber)
        {
            User? user = null;

            // Retry policy for transient I/O operations (like database queries)
            var retryPolicy = Policy
                .Handle<SqlException>()  // Handle specific SQL exceptions (e.g., timeouts, deadlocks)
                .Or<TimeoutException>()  // Handle timeout exceptions specifically
                .WaitAndRetryAsync(
                    3,  // Retry 3 times
                    attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),  // Exponential backoff (e.g., 1s, 2s, 4s)
                    (exception, timespan, retryAttempt, context) =>
                    {
                        // Log retry attempt, if necessary
                        // _logger.LogWarning($"Retry {retryAttempt} for phone number {phoneNumber} due to error: {exception.Message}");
                    });

            try
            {
                // Using retry policy to execute the database query
                user = await retryPolicy.ExecuteAsync(async () =>
                {
                    // Optimized query with potential early exit if the user is not found
                    return await _context.Users
                        .Where(u => u.UserNumberFile == phoneNumber)  // Apply query filtering
                        .AsNoTracking()  // AsNoTracking() to improve performance when read-only operations
                        .SingleOrDefaultAsync();  // Ensure uniqueness of result and return the user
                });

                if (user == null)
                {
                    // Handle if no user found, optionally log this event
                    // Example: _logger.LogInformation($"No user found with phone number {phoneNumber}");
                }
            }
            catch (Exception ex)
            {
                // Capture detailed error information and provide context (e.g., failed retry after all attempts)
                throw new ApplicationException($"Failed to retrieve user by phone number {phoneNumber} after retries.", ex);
            }

            return user;
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