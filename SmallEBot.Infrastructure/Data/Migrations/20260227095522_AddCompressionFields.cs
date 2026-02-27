using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmallEBot.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompressionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompressedAt",
                table: "Conversations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompressedContext",
                table: "Conversations",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompressedAt",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "CompressedContext",
                table: "Conversations");
        }
    }
}
