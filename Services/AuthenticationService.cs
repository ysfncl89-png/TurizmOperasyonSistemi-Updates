using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;
using Microsoft.EntityFrameworkCore;

namespace TurizmOperasyonSistemi.Services
{
    public class AuthenticationService
    {
        private readonly AppDbContext _context;

        public AuthenticationService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<(bool Success, string Message, User? User)> LoginAsync(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return (false, "Kullanıcı adı ve şifre boş olamaz.", null);
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);

            if (user == null)
            {
                return (false, "Kullanıcı adı veya şifre hatalı.", null);
            }

            if (PasswordHasher.VerifyPassword(password, user.PasswordHash))
            {
                return (true, "Giriş başarılı.", user);
            }

            return (false, "Kullanıcı adı veya şifre hatalı.", null);
        }

        public async Task<(bool Success, string Message, User? User)> RegisterAsync(string username, string password, string confirmPassword, string role = "user")
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return (false, "Kullanıcı adı ve şifre boş olamaz.", null);
            }

            if (username.Length < 3)
            {
                return (false, "Kullanıcı adı en az 3 karakter olmalıdır.", null);
            }

            if (password.Length < 6)
            {
                return (false, "Şifre en az 6 karakter olmalıdır.", null);
            }

            if (password != confirmPassword)
            {
                return (false, "Şifreler eşleşmiyor.", null);
            }

            var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);

            if (existingUser != null)
            {
                return (false, "Bu kullanıcı adı zaten kullanılıyor.", null);
            }

            var perms = RolePermissions.GetDefaultPermissions(role ?? "user");
            var newUser = new User
            {
                Username = username,
                PasswordHash = PasswordHasher.HashPassword(password),
                CreatedAt = DateTime.Now,
                Role = role,
                PermissionsJson = System.Text.Json.JsonSerializer.Serialize(perms)
            };

            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            return (true, "Kayıt başarılı.", newUser);
        }

        public async Task<bool> HasUsersAsync()
        {
            return await _context.Users.AnyAsync();
        }

        public async Task<int> GetUserCountAsync()
        {
            return await _context.Users.CountAsync();
        }

        public async Task<(bool Success, string Message)> UpdateUserRoleAsync(int userId, string newRole)
        {
            if (string.IsNullOrWhiteSpace(newRole))
            {
                return (false, "Rol boş olamaz.");
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return (false, "Kullanıcı bulunamadı.");
            }

            try
            {
                user.Role = newRole.Trim();
                var perms = RolePermissions.GetDefaultPermissions(newRole);
                user.PermissionsJson = System.Text.Json.JsonSerializer.Serialize(perms);
                await _context.SaveChangesAsync();
                return (true, "Rol başarıyla güncellendi.");
            }
            catch (System.Exception ex)
            {
                return (false, $"Rol güncellenirken hata: {ex.Message}");
            }
        }

        public async Task<(bool Success, string Message)> UpdateUserPasswordAsync(int userId, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            {
                return (false, "Şifre en az 6 karakter olmalıdır.");
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return (false, "Kullanıcı bulunamadı.");
            }

            try
            {
                user.PasswordHash = PasswordHasher.HashPassword(newPassword);
                await _context.SaveChangesAsync();
                return (true, "Şifre başarıyla güncellendi.");
            }
            catch (System.Exception ex)
            {
                return (false, $"Şifre güncellenirken hata: {ex.Message}");
            }
        }

        public async Task<(bool Success, string Message)> DeleteUserAsync(int userId)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return (false, "Kullanıcı bulunamadı.");
            }

            try
            {
                _context.Users.Remove(user);
                await _context.SaveChangesAsync();
                return (true, "Kullanıcı başarıyla silindi.");
            }
            catch (System.Exception ex)
            {
                return (false, $"Kullanıcı silinirken hata: {ex.Message}");
            }
        }
    }
}
