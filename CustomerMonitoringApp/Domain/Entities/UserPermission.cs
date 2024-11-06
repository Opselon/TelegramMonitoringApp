using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CustomerMonitoringApp.Domain.Entities;

public class UserPermission
{
    /// <summary>
    /// Primary key for UserPermission entity.
    /// </summary>
    [Key]
    public int PermissionId { get; set; }

    /// <summary>
    /// Foreign key linking this permission to a specific user.
    /// </summary>
    [ForeignKey("User")]
    public int UserId { get; set; }

    /// <summary>
    /// The Telegram ID of the user associated with this permission.
    /// </summary>
    public int UserTelegramID { get; set; } // Optional: Include this only if you need it

    [Required, StringLength(50)]
    public string PermissionType { get; set; } = string.Empty;

    [StringLength(200)]
    public string PermissionDescription { get; set; } = string.Empty;

    /// <summary>
    /// Navigation property for the associated user.
    /// </summary>
    public virtual User? User { get; set; }
}