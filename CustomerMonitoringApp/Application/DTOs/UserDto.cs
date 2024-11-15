using System;

namespace CustomerMonitoringApp.Application.DTOs
{
    /// <summary>
    /// Data Transfer Object (DTO) representing user information.
    /// </summary>
    public class UserDto
    {
        /// <summary>
        /// Gets or sets the Telegram ID of the user.
        /// </summary>
        public long UserTelegramID { get; set; }

        /// <summary>
        /// Gets or sets the profile name of the user.
        /// </summary>
        public string? UserNameProfile { get; set; }

        /// <summary>
        /// Gets or sets the number file associated with the user.
        /// </summary>
        public string? UserNumberFile { get; set; }

        /// <summary>
        /// Gets or sets the name file associated with the user.
        /// </summary>
        public string? UserNameFile { get; set; }

        /// <summary>
        /// Gets or sets the family file associated with the user.
        /// </summary>
        public string? UserFamilyFile { get; set; }

        /// <summary>
        /// Gets or sets the father's name file associated with the user.
        /// </summary>
        public string? UserFatherNameFile { get; set; }

        /// <summary>
        /// Gets or sets the birth date file associated with the user.
        /// </summary>
        public string? UserBirthDayFile { get; set; }

        /// <summary>
        /// Gets or sets the address file associated with the user.
        /// </summary>
        public string? UserAddressFile { get; set; }

        /// <summary>
        /// Gets or sets the description file associated with the user.
        /// </summary>
        public string? UserDescriptionFile { get; set; }

        /// <summary>
        /// Gets or sets the source file associated with the user.
        /// </summary>
        public string? UserSourceFile { get; set; }

        // Optionally, you can add a constructor to initialize required fields:
        public UserDto(long userTelegramID, string userNameProfile, string userNumberFile)
        {
            UserTelegramID = userTelegramID;
            UserNameProfile = userNameProfile ?? throw new ArgumentNullException(nameof(userNameProfile), "User name profile cannot be null");
            UserNumberFile = userNumberFile ?? throw new ArgumentNullException(nameof(userNumberFile), "User number file cannot be null");
        }

        // Optionally, you can add a method for custom validation if needed
        public bool IsValid()
        {
            return UserTelegramID > 0 && !string.IsNullOrEmpty(UserNameProfile);
        }
    }
}
