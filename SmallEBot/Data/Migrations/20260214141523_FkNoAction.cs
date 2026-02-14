using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmallEBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class FkNoAction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatMessages_ConversationTurns_TurnId",
                table: "ChatMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_ChatMessages_Conversations_ConversationId",
                table: "ChatMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_ConversationTurns_Conversations_ConversationId",
                table: "ConversationTurns");

            migrationBuilder.DropForeignKey(
                name: "FK_ThinkBlocks_ConversationTurns_TurnId",
                table: "ThinkBlocks");

            migrationBuilder.DropForeignKey(
                name: "FK_ThinkBlocks_Conversations_ConversationId",
                table: "ThinkBlocks");

            migrationBuilder.DropForeignKey(
                name: "FK_ToolCalls_ConversationTurns_TurnId",
                table: "ToolCalls");

            migrationBuilder.DropForeignKey(
                name: "FK_ToolCalls_Conversations_ConversationId",
                table: "ToolCalls");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatMessages_ConversationTurns_TurnId",
                table: "ChatMessages",
                column: "TurnId",
                principalTable: "ConversationTurns",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatMessages_Conversations_ConversationId",
                table: "ChatMessages",
                column: "ConversationId",
                principalTable: "Conversations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ConversationTurns_Conversations_ConversationId",
                table: "ConversationTurns",
                column: "ConversationId",
                principalTable: "Conversations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ThinkBlocks_ConversationTurns_TurnId",
                table: "ThinkBlocks",
                column: "TurnId",
                principalTable: "ConversationTurns",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ThinkBlocks_Conversations_ConversationId",
                table: "ThinkBlocks",
                column: "ConversationId",
                principalTable: "Conversations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ToolCalls_ConversationTurns_TurnId",
                table: "ToolCalls",
                column: "TurnId",
                principalTable: "ConversationTurns",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ToolCalls_Conversations_ConversationId",
                table: "ToolCalls",
                column: "ConversationId",
                principalTable: "Conversations",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatMessages_ConversationTurns_TurnId",
                table: "ChatMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_ChatMessages_Conversations_ConversationId",
                table: "ChatMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_ConversationTurns_Conversations_ConversationId",
                table: "ConversationTurns");

            migrationBuilder.DropForeignKey(
                name: "FK_ThinkBlocks_ConversationTurns_TurnId",
                table: "ThinkBlocks");

            migrationBuilder.DropForeignKey(
                name: "FK_ThinkBlocks_Conversations_ConversationId",
                table: "ThinkBlocks");

            migrationBuilder.DropForeignKey(
                name: "FK_ToolCalls_ConversationTurns_TurnId",
                table: "ToolCalls");

            migrationBuilder.DropForeignKey(
                name: "FK_ToolCalls_Conversations_ConversationId",
                table: "ToolCalls");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatMessages_ConversationTurns_TurnId",
                table: "ChatMessages",
                column: "TurnId",
                principalTable: "ConversationTurns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ChatMessages_Conversations_ConversationId",
                table: "ChatMessages",
                column: "ConversationId",
                principalTable: "Conversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ConversationTurns_Conversations_ConversationId",
                table: "ConversationTurns",
                column: "ConversationId",
                principalTable: "Conversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ThinkBlocks_ConversationTurns_TurnId",
                table: "ThinkBlocks",
                column: "TurnId",
                principalTable: "ConversationTurns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ThinkBlocks_Conversations_ConversationId",
                table: "ThinkBlocks",
                column: "ConversationId",
                principalTable: "Conversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ToolCalls_ConversationTurns_TurnId",
                table: "ToolCalls",
                column: "TurnId",
                principalTable: "ConversationTurns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ToolCalls_Conversations_ConversationId",
                table: "ToolCalls",
                column: "ConversationId",
                principalTable: "Conversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
