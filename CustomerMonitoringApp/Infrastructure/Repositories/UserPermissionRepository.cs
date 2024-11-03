using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CustomerMonitoringApp.Domain.Entities;
using CustomerMonitoringApp.Domain.Interfaces;
using CustomerMonitoringApp.Infrastructure.Data;

namespace CustomerMonitoringApp.Infrastructure.Repositories
{
    public class UserPermissionRepository : IUserPermissionRepository
    {
        private readonly AppDbContext _context;

        public UserPermissionRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<UserPermission>> GetPermissionsByUserIdAsync(long userId)
        {
            return await _context.UserPermissions
                .Where(p => p.UserTelegramID == userId)
                .ToListAsync();
        }

        public async Task AddPermissionAsync(UserPermission permission)
        {
            await _context.UserPermissions.AddAsync(permission);
            await _context.SaveChangesAsync();
        }
    }
}