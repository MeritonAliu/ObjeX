using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObjeX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMultipartUpload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "multipart_uploads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    bucket_name = table.Column<string>(type: "TEXT", nullable: false),
                    key = table.Column<string>(type: "TEXT", nullable: false),
                    content_type = table.Column<string>(type: "TEXT", nullable: false),
                    initiated_by_user_id = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_multipart_uploads", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "multipart_upload_parts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    upload_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    part_number = table.Column<int>(type: "INTEGER", nullable: false),
                    e_tag = table.Column<string>(type: "TEXT", nullable: false),
                    size = table.Column<long>(type: "INTEGER", nullable: false),
                    storage_path = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_multipart_upload_parts", x => x.id);
                    table.ForeignKey(
                        name: "fk_multipart_upload_parts_multipart_uploads_upload_id",
                        column: x => x.upload_id,
                        principalTable: "multipart_uploads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_multipart_upload_parts_upload_id_part_number",
                table: "multipart_upload_parts",
                columns: new[] { "upload_id", "part_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "multipart_upload_parts");

            migrationBuilder.DropTable(
                name: "multipart_uploads");
        }
    }
}
