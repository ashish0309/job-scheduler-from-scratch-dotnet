using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobSchedulerPrototype.Jobs.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobAcknowledgement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AcknowledgedAt",
                table: "Jobs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcknowledgedBy",
                table: "Jobs",
                type: "TEXT",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcknowledgedAt",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "AcknowledgedBy",
                table: "Jobs");
        }
    }
}
