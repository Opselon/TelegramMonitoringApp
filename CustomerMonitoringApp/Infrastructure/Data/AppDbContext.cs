using Microsoft.EntityFrameworkCore;
using CustomerMonitoringApp.Domain.Entities;
using Microsoft.EntityFrameworkCore.Design;

namespace CustomerMonitoringApp.Infrastructure.Data
{
    /// <summary>
    /// Represents the application database context for interacting with the database.
    /// </summary>
    public class AppDbContext : DbContext
    {

        public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
        {
            public AppDbContext CreateDbContext(string[] args)
            {
                var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
                optionsBuilder.UseSqlServer("Data Source=.;Integrated Security=True;Encrypt=True;Trust Server Certificate=True");

                return new AppDbContext(optionsBuilder.Options);
            }
        }


        /// <summary>
        /// Initializes a new instance of the <see cref="AppDbContext"/> class.
        /// </summary>
        /// <param name="options">The options to configure the context.</param>
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        /// <summary>
        /// Gets or sets the Users table in the database.
        /// </summary>
        public DbSet<User> Users { get; set; }
        
        public DbSet<CallHistory> CallHistories { get; set; }
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
            base.OnModelCreating(modelBuilder);

            // Configure the User entity
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.UserId); // Set the primary key

                entity.Property(e => e.UserNameFile)
                      .IsRequired()
                      .HasMaxLength(100); // Set max length and required

                entity.Property(e => e.UserTelegramID)
                      .IsRequired(); // Set as required field

                // Configure the relationship between User and UserPermission
                entity.HasMany(e => e.UserPermissions) // Navigation property
                      .WithOne(up => up.User) // Related entity
                      .HasForeignKey(up => up.UserTelegramID) // Specify foreign key
                      .OnDelete(DeleteBehavior.Cascade); // Define delete behavior
            });

            // Configure the UserPermission entity
            modelBuilder.Entity<UserPermission>(entity =>
            {
                entity.HasKey(e => e.PermissionId); // Set the primary key

                entity.Property(e => e.PermissionType)
                      .IsRequired()
                      .HasMaxLength(50); // Set max length and required

                entity.Property(e => e.UserTelegramID)
                      .IsRequired(); // Set as required field

                // Configure the relationship back to User
                entity.HasOne(up => up.User) // Navigation property
                      .WithMany(u => u.UserPermissions) // Related entity
                      .HasForeignKey(up => up.UserTelegramID) // Specify foreign key
                      .OnDelete(DeleteBehavior.Cascade); // Define delete behavior
            });
            modelBuilder.Entity<CallHistory>(entity =>
            {
                entity.HasKey(e => e.CallId);

                entity.Property(e => e.SourcePhoneNumber)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(e => e.DestinationPhoneNumber)
                    .IsRequired()
                    .HasMaxLength(50);

                // Remove references to 'Date' and 'Time' and use 'CallDateTime' instead
                entity.Property(e => e.CallDateTime)
                    .IsRequired()
                    .HasColumnType("nvarchar(max)"); // Use nvarchar(max) for Persian string

                entity.Property(e => e.Duration)
                    .IsRequired()
                    .HasDefaultValue(0); // Set default for Duration in seconds

                entity.Property(e => e.CallType)
                    .IsRequired()
                    .HasMaxLength(50);

                // Define relationships with the User entity for Caller and Recipient
                entity.HasOne(e => e.CallerUser)
                    .WithMany() // Optional: User can have many CallHistories as caller
                    .HasForeignKey(e => e.CallerUserId)
                    .OnDelete(DeleteBehavior.Restrict); // Prevent cascading delete

                entity.HasOne(e => e.RecipientUser)
                    .WithMany() // Optional: User can have many CallHistories as recipient
                    .HasForeignKey(e => e.RecipientUserId)
                    .OnDelete(DeleteBehavior.Restrict); // Prevent cascading delete
            });
        }
    }
}
