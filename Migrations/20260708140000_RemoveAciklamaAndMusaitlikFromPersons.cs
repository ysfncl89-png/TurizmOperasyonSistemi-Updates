using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurizmOperasyonSistemi.Migrations
{
    public partial class RemoveAciklamaAndMusaitlikFromPersons : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop deprecated columns from Persons
            migrationBuilder.DropColumn(
                name: "Aciklama",
                table: "Persons");

            migrationBuilder.DropColumn(
                name: "Musaitlik",
                table: "Persons");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recreate columns in case of rollback
            migrationBuilder.AddColumn<string>(
                name: "Aciklama",
                table: "Persons",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Musaitlik",
                table: "Persons",
                type: "TEXT",
                nullable: true);
        }
    }
}
