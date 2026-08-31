using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgenticHrmApi.Migrations
{
    /// <inheritdoc />
    public partial class AddFaceAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Pin",
                table: "Users",
                newName: "PasswordHash");

            migrationBuilder.AddColumn<DateTime>(
                name: "FaceEnrolledAt",
                table: "Users",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "FaceEnrolledAt", "PasswordHash" },
                values: new object[] { null, "AQAAAAIAAYagAAAAEC4Jbn8Nd/Dwn8HU7gqnsbJo/byp6OQ2g4i6SwQZdtIfAwvYVSz5LlGh+mkM4WJT7Q==" });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "FaceEnrolledAt", "PasswordHash" },
                values: new object[] { null, "AQAAAAIAAYagAAAAEHryAZ/oXThb6eVrA4LX5uttM6Mlvd1VYQtcNYxmpMbpoYnBnmDe3bHKxca34IK/fQ==" });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "FaceEnrolledAt", "PasswordHash" },
                values: new object[] { null, "AQAAAAIAAYagAAAAECBMuMglaf8y63JHfSr3OhKNyialFduBoAcwclv1amqOBOiSBbJw2HLgbSoKcyxJuw==" });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "FaceEnrolledAt", "PasswordHash" },
                values: new object[] { null, "AQAAAAIAAYagAAAAEHtzta7ccdXQ2LvmrACEW5/lTO7FfpmIZHzsOQ1xoX91jzSYw+C35ZOdxCuu3KrE0w==" });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "FaceEnrolledAt", "PasswordHash" },
                values: new object[] { null, "AQAAAAIAAYagAAAAED1wHbm9Ta0l9qVL7LgXcq0/pwpz7DR4X90VSRLs9eDVW7f+vRYBAf+jr6bOWlD/Sw==" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FaceEnrolledAt",
                table: "Users");

            migrationBuilder.RenameColumn(
                name: "PasswordHash",
                table: "Users",
                newName: "Pin");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "Pin",
                value: "1234");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 2,
                column: "Pin",
                value: "9999");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 3,
                column: "Pin",
                value: "1001");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 4,
                column: "Pin",
                value: "1002");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 5,
                column: "Pin",
                value: "1003");
        }
    }
}
