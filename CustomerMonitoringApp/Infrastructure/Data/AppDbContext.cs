using Microsoft.EntityFrameworkCore;
using CustomerMonitoringApp.Domain.Entities;

namespace CustomerMonitoringApp.Infrastructure.Data
{
    /// <summary>
    /// Represents the application database context for interacting with the database.
    /// </summary>
    public class AppDbContext : DbContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AppDbContext"/> class.
        /// </summary>
        /// <param name="options">The options to configure the context.</param>
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        /// <summary>
        /// Gets or sets the Users table in the database.
        /// </summary>
        public DbSet<User> Users { get; set; }

        /// <summary>
        /// Gets or sets the UserPermissions table in the database.
        /// </summary>
        public DbSet<UserPermission> UserPermissions { get; set; }

        /// <summary>
        /// Configures the model properties and relationships.
        /// </summary>
        /// <param name="modelBuilder">The model builder used to configure the model.</param>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Call the base method to configure additional settings
            base.OnModelCreating(modelBuilder);

            // Configure the User entity
            modelBuilder.Entity<User>(entity =>
            {
                // Specify the primary key for the User table
                entity.HasKey(e => e.UserId);

                // Configure properties for the User entity
                entity.Property(e => e.UserNameFile)
                      .IsRequired() // UserName is required
                      .HasMaxLength(100); // Set maximum length

                entity.Property(e => e.UserTelegramID)
                      .IsRequired(); // UserTelegramID is required and should be unique

                // Configure the relationship between User and UserPermission
                entity.HasMany(e => e.UserPermissions)
                      .WithOne(e => e.User)
                      .HasForeignKey(e => e.UserTelegramID)
                      .OnDelete(DeleteBehavior.Cascade); // Optional: Configure delete behavior
            });

            // Configure the UserPermission entity
            modelBuilder.Entity<UserPermission>(entity =>
            {
                // Specify the primary key for the UserPermission table
                entity.HasKey(e => e.PermissionId);

                // Configure properties for the UserPermission entity
                entity.Property(e => e.PermissionType)
                      .IsRequired() // PermissionName is required
                      .HasMaxLength(50); // Set maximum length

                entity.Property(e => e.UserTelegramID)
                      .IsRequired(); // UserTelegramID is required

                // Configure the relationship between UserPermission and User
                entity.HasOne(e => e.User)
                      .WithMany(e => e.UserPermissions)
                      .HasForeignKey(e => e.UserTelegramID)
                      .OnDelete(DeleteBehavior.Cascade); // Optional: Configure delete behavior
            });
        }
    }
}
