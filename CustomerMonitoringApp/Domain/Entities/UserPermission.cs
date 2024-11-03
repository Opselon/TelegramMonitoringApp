namespace CustomerMonitoringApp.Domain.Entities
{
    /// <summary>
    /// Represents a permission assigned to a user in the customer monitoring application.
    /// The UserPermission class encapsulates the details about the type of permission
    /// and its associated description, as well as a link to the user who holds this permission.
    /// </summary>
    public class UserPermission
    {
        /// <summary>
        /// Gets or sets the unique identifier for each permission.
        /// This ID is used to uniquely identify a permission in the system.
        /// </summary>
        public int PermissionId { get; set; }  // Unique identifier for each permission

        /// <summary>
        /// Gets or sets the Telegram ID of the user associated with this permission.
        /// This property links the permission to a specific user, allowing for permissions
        /// to be assigned based on user identification in Telegram.
        /// </summary>
        public int UserTelegramID { get; set; }  // Changed to int to match UserId

        /// <summary>
        /// Gets or sets the type of permission granted to the user.
        /// This property describes what kind of access or actions the user is permitted to perform.
        /// It is initialized to an empty string to prevent null reference exceptions.
        /// </summary>
        public string PermissionType { get; set; } = string.Empty;  // Initialized to avoid null warnings

        /// <summary>
        /// Gets or sets a description of the permission.
        /// This property provides additional context or details about what the permission entails.
        /// It is also initialized to an empty string to avoid null warnings.
        /// </summary>
        public string PermissionDescription { get; set; } = string.Empty;  // Initialized to avoid null warnings

        /// <summary>
        /// Gets or sets the user associated with this permission.
        /// This navigation property creates a relationship between the UserPermission and User entities.
        /// </summary>
        public virtual User? User { get; set; }  // Navigation property
    }
}
