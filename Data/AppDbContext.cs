using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data.Common;
using TurizmOperasyonSistemi.Models;


namespace TurizmOperasyonSistemi.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<Person> Persons { get; set; }
        public DbSet<AppSetting> AppSettings { get; set; }
        public DbSet<Registration> Registrations { get; set; }
        public DbSet<Hotel> Hotels { get; set; }
        public DbSet<RoomAllocation> RoomAllocations { get; set; }
        public DbSet<RoomOccupant> RoomOccupants { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite(@"Data Source=C:\TurizmDB\turizm.db");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // RoomAllocation ve RoomOccupant arasındaki ilişkiyi tanımlayalım
            modelBuilder.Entity<RoomOccupant>()
                .HasOne(r => r.RoomAllocation)      // Bir yolcunun bir odası vardır
                .WithMany(a => a.Occupants)         // Bir odanın birçok yolcusu olabilir
                .HasForeignKey(r => r.RoomAllocationId) // Bağlantı anahtarı bu sütundur
                .OnDelete(DeleteBehavior.Cascade);  // Oda silinirse, o odaya atanan yolcular da silinsin
        }
    }
}
