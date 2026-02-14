using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmallEBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationTurns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TurnId",
                table: "ToolCalls",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TurnId",
                table: "ThinkBlocks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TurnId",
                table: "ChatMessages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConversationTurns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsThinkingMode = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationTurns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversationTurns_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ToolCalls_TurnId",
                table: "ToolCalls",
                column: "TurnId");

            migrationBuilder.CreateIndex(
                name: "IX_ThinkBlocks_TurnId",
                table: "ThinkBlocks",
                column: "TurnId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_TurnId",
                table: "ChatMessages",
                column: "TurnId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTurns_ConversationId_CreatedAt",
                table: "ConversationTurns",
                columns: new[] { "ConversationId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_ChatMessages_ConversationTurns_TurnId",
                table: "ChatMessages",
                column: "TurnId",
                principalTable: "ConversationTurns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ThinkBlocks_ConversationTurns_TurnId",
                table: "ThinkBlocks",
                column: "TurnId",
                principalTable: "ConversationTurns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ToolCalls_ConversationTurns_TurnId",
                table: "ToolCalls",
                column: "TurnId",
                principalTable: "ConversationTurns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatMessages_ConversationTurns_TurnId",
                table: "ChatMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_ThinkBlocks_ConversationTurns_TurnId",
                table: "ThinkBlocks");

            migrationBuilder.DropForeignKey(
                name: "FK_ToolCalls_ConversationTurns_TurnId",
                table: "ToolCalls");

            migrationBuilder.DropTable(
                name: "ConversationTurns");

            migrationBuilder.DropIndex(
                name: "IX_ToolCalls_TurnId",
                table: "ToolCalls");

            migrationBuilder.DropIndex(
                name: "IX_ThinkBlocks_TurnId",
                table: "ThinkBlocks");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_TurnId",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "TurnId",
                table: "ToolCalls");

            migrationBuilder.DropColumn(
                name: "TurnId",
                table: "ThinkBlocks");

            migrationBuilder.DropColumn(
                name: "TurnId",
                table: "ChatMessages");
        }
    }
}
