namespace CustomerMonitoringApp.Application.DTOs
{
    public class UserDto
    {
        public long UserTelegramID { get; set; }
        public string UserNameProfile { get; set; }
        public string UserNumberFile { get; set; }
        public string UserNameFile { get; set; }
        public string UserFamilyFile { get; set; }
        public string UserFatherNameFile { get; set; }
        public DateTime UserBirthDayFile { get; set; }
        public string UserAddressFile { get; set; }
        public string UserDescriptionFile { get; set; }
        public string UserSourceFile { get; set; }
    }
}