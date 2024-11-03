namespace CustomerMonitoringApp.Domain.Entities
{
    /// <summary>
    /// Represents a user in the customer monitoring application.
    /// The User class contains properties that store personal information
    /// and other relevant details about the user, including their permissions.
    /// </summary>
    public class User
    {
        /// <summary>
        /// Gets or sets the unique identifier for each user.
        /// This ID is used to differentiate between users in the system.
        /// </summary>
        public int UserId { get; set; }  // Unique identifier for each user

        /// <summary>
        /// Gets or sets the user's profile name.
        /// This property is initialized to an empty string to prevent null reference exceptions.
        /// </summary>
        public string UserNameProfile { get; set; } = string.Empty; // Initialized to avoid null warnings

        /// <summary>
        /// Gets or sets the user's number file.
        /// This property is used to store the identification number or reference for the user.
        /// Initialized to prevent null reference issues.
        /// </summary>
        public string UserNumberFile { get; set; } = string.Empty;  // Initialized to avoid null warnings

        /// <summary>
        /// Gets or sets the user's name file.
        /// This property contains the user's first name or given name.
        /// Initialized to avoid null reference exceptions.
        /// </summary>
        public string UserNameFile { get; set; } = string.Empty;    // Initialized to avoid null warnings

        /// <summary>
        /// Gets or sets the user's family name file.
        /// This property holds the user's last name or surname.
        /// Initialized to ensure it is not null.
        /// </summary>
        public string UserFamilyFile { get; set; } = string.Empty;  // Initialized to avoid null warnings

        /// <summary>
        /// Gets or sets the user's father's name file.
        /// This property can be used for additional identification or personal context.
        /// Initialized to prevent null reference exceptions.
        /// </summary>
        public string UserFatherNameFile { get; set; } = string.Empty; // Initialized to avoid null warnings

        /// <summary>
        /// Gets or sets the user's birth date.
        /// This property is a value type, so it cannot be null.
        /// </summary>
        public DateTime UserBirthDayFile { get; set; } // Considered a value type, cannot be null

        /// <summary>
        /// Gets or sets the user's address file.
        /// This property stores the user's residential address.
        /// Initialized to avoid null warnings.
        /// </summary>
        public string UserAddressFile { get; set; } = string.Empty;   // Initialized to avoid null warnings

        /// <summary>
        /// Gets or sets a description of the user.
        /// This property can hold any additional information about the user.
        /// Initialized to prevent null reference exceptions.
        /// </summary>
        public string UserDescriptionFile { get; set; } = string.Empty; // Initialized to avoid null warnings

        /// <summary>
        /// Gets or sets the source of user data.
        /// This property is used to indicate where the user data was sourced from.
        /// Initialized to avoid null reference issues.
        /// </summary>
        public string UserSourceFile { get; set; } = string.Empty;     // Initialized to avoid null warnings

        /// <summary>
        /// Gets or sets the user's unique Telegram ID.
        /// This property allows for identification of the user in Telegram services.
        /// </summary>
        public int UserTelegramID { get; set; }  // Unique Telegram ID, changed to int

        /// <summary>
        /// Gets or sets the collection of user permissions.
        /// This navigation property holds the permissions associated with the user.
        /// The collection is initialized to an empty list to prevent null reference exceptions.
        /// </summary>
        public virtual ICollection<UserPermission> UserPermissions { get; set; } = new List<UserPermission>();  // Navigation property


    }
}
