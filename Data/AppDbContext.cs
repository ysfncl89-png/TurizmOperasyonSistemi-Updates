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

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite(@"Data Source=C:\TurizmDB\turizm.db");
        }

        public void EnsureRegistrationSchema()
        {
            Database.OpenConnection();

            try
            {
                using var createCommand = Database.GetDbConnection().CreateCommand();
                createCommand.CommandText = @"
CREATE TABLE IF NOT EXISTS ""Registrations"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Registrations"" PRIMARY KEY AUTOINCREMENT,
    ""PersonId"" INTEGER NOT NULL,
    ""TourType"" TEXT NOT NULL,
    ""Period"" TEXT NOT NULL,
    ""GroupType"" TEXT NOT NULL,
    ""DayCount"" TEXT NOT NULL,
    ""VisaStatus"" TEXT,
    ""TicketStatus"" TEXT,
    ""CargoStatus"" TEXT,
    ""RoomType"" TEXT,
    ""JoinCity"" TEXT,
    ""TransferStatus"" TEXT,
    ""TransferCity"" TEXT,
    ""District"" TEXT,
    ""TourPrice"" TEXT NOT NULL,
    ""CurrencyType"" TEXT,
    ""PaidAmount"" TEXT NOT NULL,
    ""RemainingDebt"" TEXT NOT NULL,
    ""Reference"" TEXT,
    ""RegistrationDate"" TEXT NOT NULL,
    ""IsActive"" INTEGER NOT NULL
);";
                createCommand.ExecuteNonQuery();

                var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using var pragmaCommand = Database.GetDbConnection().CreateCommand();
                pragmaCommand.CommandText = @"PRAGMA table_info('Registrations')";

                using (var reader = pragmaCommand.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        existingColumns.Add(reader.GetString(1));
                    }
                }

                EnsureColumn(Database.GetDbConnection(), existingColumns, "TransferCity", "TEXT");
                EnsureColumn(Database.GetDbConnection(), existingColumns, "District", "TEXT");
                EnsureColumn(Database.GetDbConnection(), existingColumns, "Reference", "TEXT");
            }
            finally
            {
                Database.CloseConnection();
            }
        }

        private static void EnsureColumn(DbConnection connection, HashSet<string> existingColumns, string columnName, string sqlType)
        {
            if (existingColumns.Contains(columnName))
            {
                return;
            }

            using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = $@"ALTER TABLE ""Registrations"" ADD COLUMN ""{columnName}"" {sqlType}";
            alterCommand.ExecuteNonQuery();
            existingColumns.Add(columnName);
        }
    }
}
