using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurizmOperasyonSistemi.Migrations
{
    public partial class AddMusaitlikToPersons : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Musaitlik",
                table: "Persons",
                type: "TEXT",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Musaitlik",
                table: "Persons");
        }
    }
}
