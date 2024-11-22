using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace CustomerMonitoringApp.Domain.Views
{
    /// <summary>
    /// Represents a view that combines call history with user information.
    /// </summary>
    [Index(nameof(SourcePhoneNumber))] // Index to optimize caller phone lookups
    [Index(nameof(DestinationPhoneNumber))] // Index to optimize receiver phone lookups
    public class CallHistoryWithUserNames
    {
        /// <summary>
        /// Unique identifier for the call record.
        /// </summary>
        [Key]
        public int CallId { get; set; }




        /// <summary>
        /// The phone number from which the call was made (source).
        /// Indexed for performance optimization.
        /// </summary>
        [Required]
        [StringLength(13)] // Ensuring length to avoid database issues
        public string SourcePhoneNumber { get; set; } = string.Empty;




        /// <summary>
        /// The phone number that received the call (destination).
        /// Indexed for performance optimization.
        /// </summary>
        [Required]
        [StringLength(13)] // Ensuring length to avoid database issues
        public string DestinationPhoneNumber { get; set; } = string.Empty;




        /// <summary>
        /// Date and time of the call.
        /// Stored as a string, formatted as "yyyy-MM-dd HH:mm:ss".
        /// </summary>
        [Required]
        [StringLength(20)] // For optimized storage of datetime as a string
        public string CallDateTime { get; set; } = string.Empty;




        /// <summary>
        /// Duration of the call in seconds.
        /// Defaults to 0 if not provided.
        /// </summary>
        [Range(0, int.MaxValue)] // Range to ensure validity of the duration
        public int Duration { get; set; } = 0; // Default to 0 for unrecorded calls



        /// <summary>
        /// Type of the call (e.g., "Incoming", "Outgoing").
        /// </summary>
        [Required]
        [StringLength(15)] // Set a length limit for call type
        public string CallType { get; set; } = string.Empty;




        /// <summary>
        /// The file name related to the call.
        /// </summary>
        [StringLength(20)] // Assuming the file name can be long
        public string FileName { get; set; } = string.Empty;



        /// <summary>
        /// Name of the caller (if available).
        /// This can be null if not available.
        /// </summary>
        [StringLength(100)] // Set limit for the name field
        public string? CallerName { get; set; }



        /// <summary>
        /// Name of the receiver (if available).
        /// This can be null if not available.
        /// </summary>
        [StringLength(100)] // Set limit for the name field
        public string? ReceiverName { get; set; }

    }
}
