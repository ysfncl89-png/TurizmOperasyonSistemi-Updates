namespace TurizmOperasyonSistemi.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string PasswordHash { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string Role { get; set; } = "user";
        // JSON array of permission keys e.g. ["module.payments","module.lists.view"]
        public string PermissionsJson { get; set; } = "[]";
    }
}
