using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AQUAR.Api.Migrations
{
    /// <inheritdoc />
    public partial class RequirePasswordChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "Staff",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TokenVersion",
                table: "Staff",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "TokenVersion",
                table: "Staff");
        }
    }
}
