using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurizmOperasyonSistemi.Migrations
{
    /// <inheritdoc />
    public partial class AddDatabaseVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create DatabaseVersions table to track database schema versions
            migrationBuilder.Sql(@"CREATE TABLE IF NOT EXISTS DatabaseVersions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Version TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                Description TEXT
            );");

            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS IX_DatabaseVersions_CreatedAt ON DatabaseVersions(CreatedAt);");

            // Create MigrationLogs table to track migration execution history
            migrationBuilder.Sql(@"CREATE TABLE IF NOT EXISTS MigrationLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                MigrationName TEXT NOT NULL,
                AppliedAt TEXT NOT NULL,
                Success INTEGER NOT NULL DEFAULT 1,
                ErrorMessage TEXT
            );");

            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS IX_MigrationLogs_AppliedAt ON MigrationLogs(AppliedAt);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "MigrationLogs");
            migrationBuilder.DropTable(name: "DatabaseVersions");
        }
    }
}
