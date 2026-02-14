using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmallEBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class ToolCallToConversation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConversationId",
                table: "ToolCalls",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "ToolCalls",
                type: "TEXT",
                nullable: true);

            // Copy ConversationId and CreatedAt from parent ChatMessage
            migrationBuilder.Sql(
                "UPDATE ToolCalls SET ConversationId = (SELECT ConversationId FROM ChatMessages WHERE ChatMessages.Id = ToolCalls.ChatMessageId), CreatedAt = (SELECT CreatedAt FROM ChatMessages WHERE ChatMessages.Id = ToolCalls.ChatMessageId)");

            migrationBuilder.AlterColumn<Guid>(
                name: "ConversationId",
                table: "ToolCalls",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "ToolCalls",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.DropForeignKey(
                name: "FK_ToolCalls_ChatMessages_ChatMessageId",
                table: "ToolCalls");

            migrationBuilder.DropIndex(
                name: "IX_ToolCalls_ChatMessageId",
                table: "ToolCalls");

            migrationBuilder.DropColumn(
                name: "ChatMessageId",
                table: "ToolCalls");

            migrationBuilder.CreateIndex(
                name: "IX_ToolCalls_ConversationId_CreatedAt",
                table: "ToolCalls",
                columns: new[] { "ConversationId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_ToolCalls_Conversations_ConversationId",
                table: "ToolCalls",
                column: "ConversationId",
                principalTable: "Conversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ToolCalls_Conversations_ConversationId",
                table: "ToolCalls");

            migrationBuilder.DropIndex(
                name: "IX_ToolCalls_ConversationId_CreatedAt",
                table: "ToolCalls");

            migrationBuilder.AddColumn<Guid>(
                name: "ChatMessageId",
                table: "ToolCalls",
                type: "TEXT",
                nullable: true);

            // Restore ChatMessageId from first assistant message in same conversation (best-effort)
            migrationBuilder.Sql(
                "UPDATE ToolCalls SET ChatMessageId = (SELECT Id FROM ChatMessages WHERE ChatMessages.ConversationId = ToolCalls.ConversationId AND ChatMessages.Role = 'assistant' ORDER BY CreatedAt LIMIT 1)");

            migrationBuilder.AlterColumn<Guid>(
                name: "ChatMessageId",
                table: "ToolCalls",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "ConversationId",
                table: "ToolCalls");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "ToolCalls");

            migrationBuilder.CreateIndex(
                name: "IX_ToolCalls_ChatMessageId",
                table: "ToolCalls",
                column: "ChatMessageId");

            migrationBuilder.AddForeignKey(
                name: "FK_ToolCalls_ChatMessages_ChatMessageId",
                table: "ToolCalls",
                column: "ChatMessageId",
                principalTable: "ChatMessages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
