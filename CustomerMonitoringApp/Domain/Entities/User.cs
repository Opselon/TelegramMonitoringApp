using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace CustomerMonitoringApp.Domain.Entities
{
    /// <summary>
    /// Represents a user in the customer monitoring application. This entity stores
    /// personal user information along with a collection of permissions related to the user.
    /// </summary>
    [Index(nameof(UserTelegramID), IsUnique = true)] // Creates a unique index on UserTelegramID
    public class User
    {
        /// <summary>
        /// Primary key for the User entity. This unique identifier is automatically generated.
        /// </summary>
        [Key]
        public int UserId { get; set; }

        /// <summary>
        /// The username as displayed in the user's profile. This field is required and
        /// has a maximum length of 100 characters.
        /// </summary>
        [Required]
        [StringLength(100)]
        public string UserNameProfile { get; set; } = string.Empty;

        /// <summary>
        /// The unique number associated with the user's file. This is an optional field
        /// with a maximum length of 50 characters.
        /// </summary>
        [StringLength(50)]
        public string UserNumberFile { get; set; } = string.Empty;

        /// <summary>
        /// The first name of the user as recorded in their file. This is a required field
        /// with a maximum length of 50 characters.
        /// </summary>
        [Required]
        [StringLength(50)]
        public string UserNameFile { get; set; } = string.Empty;

        /// <summary>
        /// The family name of the user as recorded in their file. This is a required field
        /// with a maximum length of 50 characters.
        /// </summary>
        [Required]
        [StringLength(50)]
        public string UserFamilyFile { get; set; } = string.Empty;

        /// <summary>
        /// The father's name of the user as recorded in their file. This is an optional field
        /// with a maximum length of 50 characters.
        /// </summary>
        [StringLength(50)]
        public string UserFatherNameFile { get; set; } = string.Empty;

        /// <summary>
        /// The birth date of the user. This is an optional field, formatted as a date.
        /// </summary>
        [DataType(DataType.Date)]
        public DateTime? UserBirthDayFile { get; set; }

        /// <summary>
        /// The address of the user as recorded in their file. This is an optional field
        /// with a maximum length of 200 characters.
        /// </summary>
        [StringLength(200)]
        public string UserAddressFile { get; set; } = string.Empty;

        /// <summary>
        /// Any additional description regarding the user. This is an optional field
        /// with a maximum length of 500 characters.
        /// </summary>
        [StringLength(500)]
        public string UserDescriptionFile { get; set; } = string.Empty;

        /// <summary>
        /// The source of the user's data. This is an optional field with a maximum length
        /// of 100 characters, used to store reference information about data origin.
        /// </summary>
        [StringLength(100)]
        public string UserSourceFile { get; set; } = string.Empty;

        /// <summary>
        /// The Telegram ID of the user, used as a unique identifier within the application.
        /// This field is required and must be unique for each user.
        /// </summary>
        [Required]
        public long UserTelegramID { get; set; }

        // New relationship to store the call history
        public virtual ICollection<CallHistory> CallHistory { get; set; } = new List<CallHistory>();

        /// <summary>
        /// Gets or sets the collection of user permissions.
        /// </summary>
        public virtual ICollection<UserPermission> UserPermissions { get; set; } = new List<UserPermission>();
    }
}
