﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace CustomerMonitoringApp.Domain.Entities
{
    /// <summary>
    /// Represents a user in the customer monitoring application, storing personal information 
    /// along with collections for permissions and call history related to the user.
    /// </summary>
    [Index(nameof(UserNumberFile))] // Index to optimize search on user phone numbers
    public class User
    {
        #region Properties

        /// <summary>
        /// Primary key for the User entity, uniquely identifies each user in the database.
        /// </summary>
        [Key]
        public int UserId { get; set; }

        /// <summary>
        /// Username as displayed in the user's profile. Required, max length 150 characters.
        /// </summary>
        public string? UserNameProfile { get; set; } = string.Empty;

        /// <summary>
        /// Unique number associated with the user's file. Optional, max length 50 characters.
        /// </summary>
        public string? UserNumberFile { get; set; } = string.Empty;

        /// <summary>
        /// First name of the user as recorded in their file. Required, max length 120 characters.
        /// </summary>
        public string? UserNameFile { get; set; } = string.Empty;

        /// <summary>
        /// Family name of the user as recorded in their file. Required, max length 150 characters.
        /// </summary>
        public string? UserFamilyFile { get; set; } = string.Empty;

        /// <summary>
        /// Father's name of the user as recorded in their file. Optional, max length 100 characters.
        /// </summary>
        public string? UserFatherNameFile { get; set; } = string.Empty;

        /// <summary>
        /// Birth date of the user, stored as a string in "yyyy-MM-dd" format. Optional.
        /// </summary>
        public string? UserBirthDayFile { get; set; }

        /// <summary>
        /// Address of the user as recorded in their file. Optional, max length 250 characters.
        /// </summary>
        public string? UserAddressFile { get; set; } = string.Empty;

        /// <summary>
        /// Additional description about the user. Optional, max length 500 characters.
        /// </summary>
        public string? UserDescriptionFile { get; set; } = string.Empty;

        /// <summary>
        /// Source of the user's data (e.g., data origin). Optional, max length 100 characters.
        /// </summary>
        public string? UserSourceFile { get; set; } = string.Empty;

        public long? UserTelegramID { get; set; }

        #endregion
    }
}
