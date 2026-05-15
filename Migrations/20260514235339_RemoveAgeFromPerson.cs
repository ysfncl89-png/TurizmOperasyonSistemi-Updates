using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurizmOperasyonSistemi.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAgeFromPerson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Age",
                table: "Persons");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Age",
                table: "Persons",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }
    }
}
