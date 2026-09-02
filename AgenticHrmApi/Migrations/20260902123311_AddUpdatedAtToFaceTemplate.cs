using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgenticHrmApi.Migrations
{
    /// <inheritdoc />
    public partial class AddUpdatedAtToFaceTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "FaceTemplates",
                type: "timestamp without time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "FaceTemplates");
        }
    }
}
