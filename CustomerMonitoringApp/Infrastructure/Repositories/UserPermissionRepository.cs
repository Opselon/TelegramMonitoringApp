using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CustomerMonitoringApp.Domain.Entities;
using CustomerMonitoringApp.Domain.Interfaces;
using CustomerMonitoringApp.Infrastructure.Data;

namespace CustomerMonitoringApp.Infrastructure.Repositories
{
    /// <summary>
    /// Repository for managing user permissions.
    /// </summary>
    public class UserPermissionRepository : IUserPermissionRepository
    {
        private readonly AppDbContext _context;

        /// <summary>
        /// Initializes a new instance of the <see cref="UserPermissionRepository"/> class.
        /// </summary>
        /// <param name="context">The application database context.</param>
        public UserPermissionRepository(AppDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>
        /// Retrieves permissions associated with a specific user ID.
        /// </summary>
        /// <param name="userId">The ID of the user.</param>
        /// <returns>A task representing the asynchronous operation. The task result contains a collection of <see cref="UserPermission"/>.</returns>
        public async Task<IEnumerable<UserPermission>> GetPermissionsByUserIdAsync(long userId)
        {
            try
            {
                return await _context.UserPermissions
                    .AsNoTracking() // Optimizes query for read-only purposes
                    .Where(p => p.UserTelegramID == userId)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                // Log the error here if logging is configured
                throw new InvalidOperationException("Error retrieving user permissions.", ex);
            }
        }

        /// <summary>
        /// Adds a new user permission to the database.
        /// </summary>
        /// <param name="permission">The <see cref="UserPermission"/> entity to be added.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task AddPermissionAsync(UserPermission permission)
        {
            if (permission == null)
            {
                throw new ArgumentNullException(nameof(permission), "The permission to add cannot be null.");
            }

            try
            {
                await _context.UserPermissions.AddAsync(permission);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException dbEx)
            {
                // Log the error here if logging is configured
                throw new InvalidOperationException("Error adding user permission to the database.", dbEx);
            }
        }
    }
}
