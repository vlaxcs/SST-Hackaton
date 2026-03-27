using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SST_Hackaton.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNoteFoldersAndBulkSelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FolderName",
                table: "Notes",
                type: "TEXT",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FolderName",
                table: "Notes");
        }
    }
}
