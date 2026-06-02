using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SST_Hackaton.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveNoteTypeFromNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notes_NoteTypes_NoteTypeId",
                table: "Notes");

            migrationBuilder.DropTable(
                name: "NoteTypes");

            migrationBuilder.DropIndex(
                name: "IX_Notes_NoteTypeId",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "NoteTypeId",
                table: "Notes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NoteTypeId",
                table: "Notes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "NoteTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoteTypes", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "NoteTypes",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "Text" },
                    { 2, "Checkbox" },
                    { 3, "Audio" },
                    { 4, "Video" },
                    { 5, "Photo" },
                    { 6, "Drawing" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notes_NoteTypeId",
                table: "Notes",
                column: "NoteTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Notes_NoteTypes_NoteTypeId",
                table: "Notes",
                column: "NoteTypeId",
                principalTable: "NoteTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
