using System.Collections.Generic;

namespace TurizmOperasyonSistemi.Services
{
    public static class PermissionKeys
    {
        public const string All = "all";
        public const string UserManagement = "users.manage";
        public const string Payments = "module.payments";
        public const string Cargo = "module.cargo";
        public const string ListsView = "module.lists.view";
        public const string ListsEdit = "module.lists.edit";
        public const string ListsKaroEdit = "module.lists.karo.edit";
        public const string Export = "module.export";
    }

    public static class RolePermissions
    {
        public static List<string> GetDefaultPermissions(string role)
        {
            if (string.IsNullOrWhiteSpace(role)) role = "user";
            role = role.Trim().ToLowerInvariant();

            switch (role)
            {
                case "admin":
                    return new List<string> { PermissionKeys.All };
                default:
                    break;
            }

            // normalize common names for moderator
            if (role == "moderator" || role == "moderatör") role = "moderator";

            if (role == "moderator")
            {
                // all module access except user management
                return new List<string> {
                    PermissionKeys.Payments,
                    PermissionKeys.Cargo,
                    PermissionKeys.ListsView,
                    PermissionKeys.ListsEdit,
                    PermissionKeys.ListsKaroEdit,
                    PermissionKeys.Export
                };
            }

            if (role == "muhasebe")
            {
                return new List<string> { PermissionKeys.Payments, PermissionKeys.Export };
            }

            if (role == "kargo")
            {
                // kargo rolü yalnızca kargo modülüne erişsin
                return new List<string> { PermissionKeys.Cargo };
            }

            // default: kullanıcı
            if (role == "kullanıcı" || role == "kullanici" || role == "user")
            {
                return new List<string> { PermissionKeys.ListsView, PermissionKeys.Export };
            }

            // fallback minimal
            return new List<string> { PermissionKeys.ListsView };
        }
    }
}
