public class UserCallSmsStatistics
{
    public string? PhoneNumber { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? FatherName { get; set; }
    public string? BirthDate { get; set; }
    public string? Address { get; set; }
    public string? FileNames { get; set; }
    public string? UserSourceFiles { get; set; } // Added UserSourceFiles for source file names
    public int TotalCalls { get; set; } = 0; // Default to 0 if no calls found
    public int TotalSMS { get; set; } = 0; // Default to 0 if no SMS found
    public int TotalCallDuration { get; set; } = 0; // Default to 0 if no call duration found
    public int UserRepetitionCount { get; set; } // New field for Users table
    public int CallHistoryRepetitionCount { get; set; } // New field for CallHistories
    // Add fields for frequent partners
    public string? FrequentPartners1 { get; set; }
    public string? FrequentPartners2 { get; set; }
    public string? FrequentPartners3 { get; set; }
    public string? FrequentPartners4 { get; set; }

    // Method to set defaults for properties if they are null or empty
    public void SetDefaultsIfNeeded()
    {
        PhoneNumber ??= "Not Available";
        FirstName ??= "Not Available";
        LastName ??= "Not Available";
        FatherName ??= "Not Available";
        BirthDate ??= "Not Available";
        Address ??= "Not Available";
        FileNames ??= "Not Available";
        UserSourceFiles ??= "Not Available"; // Set default for UserSourceFiles
        FrequentPartners1 ??= "Not Available"; // Set default for FrequentPartners1
        FrequentPartners2 ??= "Not Available"; // Set default for FrequentPartners2
        FrequentPartners3 ??= "Not Available"; // Set default for FrequentPartners3
        FrequentPartners4 ??= "Not Available"; // Set default for FrequentPartners4
    }
}