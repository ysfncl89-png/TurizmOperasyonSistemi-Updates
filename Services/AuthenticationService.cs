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

        public async Task<(bool Success, string Message, User? User)> RegisterAsync(string username, string password, string confirmPassword)
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

            var newUser = new User
            {
                Username = username,
                PasswordHash = PasswordHasher.HashPassword(password),
                CreatedAt = DateTime.Now
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
    }
}
