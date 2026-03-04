using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acme.TaskHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUpdatedAtAndRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "TodoItems",
                type: "BLOB",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UpdatedAtUtc",
                table: "TodoItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "TodoItems");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "TodoItems");
        }
    }
}
