/// <summary>
/// A data transfer object representing a call history record.
/// </summary>
public class CallHistoryDto
{
    /// <summary>
    /// Gets or sets the source phone number of the call.
    /// </summary>
    public string SourcePhoneNumber { get; set; }

    /// <summary>
    /// Gets or sets the destination phone number of the call.
    /// </summary>
    public string DestinationPhoneNumber { get; set; }

    /// <summary>
    /// Gets or sets the date and time the call was made.
    /// </summary>
    public DateTime CallDateTime { get; set; }

    /// <summary>
    /// Gets or sets the duration of the call in seconds.
    /// </summary>
    public int Duration { get; set; }

    /// <summary>
    /// Gets or sets the type of the call (e.g. incoming, outgoing, missed).
    /// </summary>
    public string CallType { get; set; }

    // New Field to store the File Name
    public string FileName { get; set; }  // This is the new field
}