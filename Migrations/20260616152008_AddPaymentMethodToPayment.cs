using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurizmOperasyonSistemi.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentMethodToPayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Note: other schema alterations removed to avoid duplicate-column errors on existing databases.
            // This migration focuses on creating the Payments table only.

            // Create Payments table only if it does not already exist (some deployments create it at runtime)
            migrationBuilder.Sql(@"CREATE TABLE IF NOT EXISTS Payments (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    RegistrationId INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    OriginalAmount TEXT NOT NULL,
                    OriginalCurrency TEXT NOT NULL,
                    AmountInRegistrationCurrency TEXT NOT NULL,
                    Rate TEXT,
                    Note TEXT NOT NULL,
                    UploadedDocumentPath TEXT NOT NULL,
                    PaymentMethod TEXT NOT NULL,
                    FOREIGN KEY (RegistrationId) REFERENCES Registrations(Id) ON DELETE CASCADE
                );");

            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS IX_Payments_RegistrationId ON Payments(RegistrationId);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Payments");
        }
    }
}
