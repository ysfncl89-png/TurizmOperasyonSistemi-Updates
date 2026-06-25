using TurizmOperasyonSistemi.Models;
using System.Text.Json;

namespace TurizmOperasyonSistemi.Services
{
    public class CurrentUser
    {
        private static CurrentUser? _instance;
        private User? _user;
        private HashSet<string> _permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public User? User => _user;

        private CurrentUser()
        {
        }

        public static CurrentUser Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new CurrentUser();
                }
                return _instance;
            }
        }

        public void SetUser(User user)
        {
            _user = user;
            _permissions.Clear();
            try
            {
                if (!string.IsNullOrWhiteSpace(user.PermissionsJson))
                {
                    var list = JsonSerializer.Deserialize<List<string>>(user.PermissionsJson) ?? new List<string>();
                    foreach (var p in list) if (!string.IsNullOrWhiteSpace(p)) _permissions.Add(p);
                }
            }
            catch
            {
                // fallback: use role defaults
                try
                {
                    var defaults = RolePermissions.GetDefaultPermissions(user.Role ?? "user");
                    foreach (var p in defaults) _permissions.Add(p);
                }
                catch { }
            }

            // ensure admin always has wildcard 'all'
            if (string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase)) _permissions.Add(PermissionKeys.All);
        }

        public void Clear()
        {
            _user = null;
            _permissions.Clear();
        }

        public bool IsLoggedIn => _user != null;

        public bool IsAdmin => _user != null && string.Equals(_user.Role, "admin", StringComparison.OrdinalIgnoreCase);

        public bool HasPermission(string key)
        {
            if (!IsLoggedIn) return false;
            if (string.IsNullOrWhiteSpace(key)) return false;

            // Kargo rolündeki kullanıcılar yalnızca kargo modülüne erişebilsin -- veritabanındaki PermissionsJson'da ekstra izinler olsa bile
            if (string.Equals(_user?.Role ?? string.Empty, "kargo", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(key, PermissionKeys.Cargo, StringComparison.OrdinalIgnoreCase);
            }

            // If user has global wildcard, allow all except explicitly restricted cases
            if (_permissions.Contains(PermissionKeys.All))
            {
                // Moderatör should not have user management even if 'all' present as default
                if (string.Equals(_user?.Role ?? string.Empty, "moderator", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(_user?.Role ?? string.Empty, "moderatör", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(key, PermissionKeys.UserManagement, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                return true;
            }

            return _permissions.Contains(key);
        }

        public IEnumerable<string> Permissions => _permissions.ToList();
    }
}
