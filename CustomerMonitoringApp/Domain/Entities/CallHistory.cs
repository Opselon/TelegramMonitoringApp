using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CustomerMonitoringApp.Domain.Entities
{
    /// <summary>
    /// Represents a call record between two phone numbers.
    /// </summary>
    public class CallHistory
    {
        /// <summary>
        /// Unique identifier for the call record.
        /// </summary>
        [Key]
        public int CallId { get; set; }

        /// <summary>
        /// The phone number from which the call was made (source).
        /// </summary>
        [StringLength(50)]
        public string SourcePhoneNumber { get; set; } = string.Empty;

        /// <summary>
        /// The phone number that received the call (destination).
        /// </summary>
        [StringLength(50)]
        public string DestinationPhoneNumber { get; set; } = string.Empty;

        /// <summary>
        /// The date and time the call occurred.
        /// </summary>
        public DateTime CallDateTime { get; set; }

        /// <summary>
        /// The duration of the call in seconds.
        /// </summary>
        public int Duration { get; set; }

        /// <summary>
        /// The type of the call (e.g., inbound or outbound).
        /// </summary>
        [StringLength(50)]
        public string CallType { get; set; } = string.Empty;

        // Navigation properties for associated users (caller and recipient)
        public int? CallerUserId { get; set; }
        public virtual User CallerUser { get; set; }

        public int? RecipientUserId { get; set; }
        public virtual User RecipientUser { get; set; }
    }
}