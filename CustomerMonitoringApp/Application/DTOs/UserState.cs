namespace CustomerMonitoringApp.Application.DTOs
{
    public class UserState
    {
        public long ChatId { get; set; }
        public string CurrentCommand { get; set; }
        public Dictionary<string, object> Data { get; set; } = new Dictionary<string, object>();

        // Add more fields as needed to track the user's session state
    }
}
