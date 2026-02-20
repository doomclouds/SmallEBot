using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmallEBot.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageEditSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsEdited",
                table: "ChatMessages",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacedByMessageId",
                table: "ChatMessages",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsEdited",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "ReplacedByMessageId",
                table: "ChatMessages");
        }
    }
}
