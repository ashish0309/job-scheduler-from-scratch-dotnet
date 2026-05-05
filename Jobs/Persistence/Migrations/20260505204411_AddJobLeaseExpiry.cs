using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobSchedulerPrototype.Jobs.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobLeaseExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LeaseExpiresAt",
                table: "Jobs",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "Jobs");
        }
    }
}
