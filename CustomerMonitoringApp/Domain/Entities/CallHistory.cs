using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace CustomerMonitoringApp.Domain.Entities
{
    /// <summary>
    /// Represents a call record between two phone numbers.
    /// </summary>
    [Index(nameof(SourcePhoneNumber))] // Index to optimize caller phone lookups
    [Index(nameof(DestinationPhoneNumber))] // Index to optimize receiver phone lookups
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

     
        
        [StringLength(50)]
        public string CallDateTime { get; set; }

        /// <summary>
        /// The duration of the call in seconds.
        /// </summary>
        public int Duration { get; set; }

        /// <summary>
        /// The type of the call (e.g., inbound or outbound).
        /// </summary>
        [StringLength(50)]
        public string CallType { get; set; } = string.Empty;

        [StringLength(50)]
        // New Field to store the File Name
        public string FileName { get; set; }  // This is the new field


    }
}