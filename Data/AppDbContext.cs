using Microsoft.EntityFrameworkCore;
using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<Person> Persons { get; set; }
        public DbSet<Registration> Registrations { get; set; }
        public DbSet<Hotel> Hotels { get; set; }

        public DbSet<Room> Rooms { get; set; }
        public DbSet<AppSetting> AppSettings { get; set; }
        public DbSet<RoomAssignment> RoomAssignments { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite(@"Data Source=C:\TurizmDB\turizm.db");
        }
    }
}