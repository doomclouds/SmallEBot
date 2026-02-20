using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmallEBot.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttachedPathsJson",
                table: "ChatMessages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestedSkillIdsJson",
                table: "ChatMessages",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttachedPathsJson",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "RequestedSkillIdsJson",
                table: "ChatMessages");
        }
    }
}
