using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmallEBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddToolCalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ToolCalls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChatMessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToolName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Arguments = table.Column<string>(type: "TEXT", nullable: true),
                    Result = table.Column<string>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToolCalls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ToolCalls_ChatMessages_ChatMessageId",
                        column: x => x.ChatMessageId,
                        principalTable: "ChatMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ToolCalls_ChatMessageId",
                table: "ToolCalls",
                column: "ChatMessageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ToolCalls");
        }
    }
}
