using System;
using System.Linq;
using TurizmOperasyonSistemi.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace TurizmOperasyonSistemi.Services
{
    public static class UserPermissionSeeder
    {
        // Ensure users have PermissionsJson set according to their role when missing or empty
        public static void EnsureUserPermissionsSeeded()
        {
            try
            {
                using var db = new AppDbContext();
                var users = db.Users.ToList();
                bool changed = false;
                foreach (var u in users)
                {
                    try
                    {
                        var pj = (u.PermissionsJson ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(pj) || pj == "[]")
                        {
                            var perms = RolePermissions.GetDefaultPermissions(u.Role ?? "user");
                            u.PermissionsJson = JsonSerializer.Serialize(perms);
                            changed = true;
                        }
                    }
                    catch { }
                }

                if (changed)
                {
                    db.SaveChanges();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UserPermissionSeeder hata: {ex.Message}");
            }
        }
    }
}
