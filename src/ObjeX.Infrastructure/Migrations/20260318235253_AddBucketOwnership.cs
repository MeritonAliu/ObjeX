using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObjeX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBucketOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "owner_id",
                table: "buckets",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // Backfill: assign existing buckets to the seeded admin user
            migrationBuilder.Sql(@"
                UPDATE buckets SET owner_id = (
                    SELECT id FROM ""AspNetUsers""
                    WHERE user_name = 'admin' LIMIT 1
                ) WHERE owner_id IS NULL OR owner_id = '';
            ");

            migrationBuilder.CreateIndex(
                name: "ix_buckets_owner_id",
                table: "buckets",
                column: "owner_id");

            migrationBuilder.AddForeignKey(
                name: "fk_buckets_users_owner_id",
                table: "buckets",
                column: "owner_id",
                principalTable: "AspNetUsers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_buckets_users_owner_id",
                table: "buckets");

            migrationBuilder.DropIndex(
                name: "ix_buckets_owner_id",
                table: "buckets");

            migrationBuilder.DropColumn(
                name: "owner_id",
                table: "buckets");
        }
    }
}
