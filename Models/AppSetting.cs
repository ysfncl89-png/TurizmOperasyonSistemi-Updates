namespace TurizmOperasyonSistemi.Models
{
    public class AppSetting
    {
        public int Id { get; set; }

        public string Type { get; set; }

        public string Value { get; set; }

        public bool IsActive { get; set; } = true;
    }
}