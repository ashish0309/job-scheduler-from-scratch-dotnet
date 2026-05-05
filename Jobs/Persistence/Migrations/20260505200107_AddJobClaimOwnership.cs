using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobSchedulerPrototype.Jobs.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobClaimOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ClaimedAt",
                table: "Jobs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClaimedBy",
                table: "Jobs",
                type: "TEXT",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClaimedAt",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ClaimedBy",
                table: "Jobs");
        }
    }
}
