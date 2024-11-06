using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerMonitoringApp.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserNameProfile = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UserNumberFile = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    UserNameFile = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UserFamilyFile = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    UserFatherNameFile = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    UserBirthDayFile = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UserAddressFile = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UserDescriptionFile = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    UserSourceFile = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UserTelegramID = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "UserPermissions",
                columns: table => new
                {
                    PermissionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    UserTelegramID = table.Column<int>(type: "int", nullable: false),
                    PermissionType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PermissionDescription = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPermissions", x => x.PermissionId);
                    table.ForeignKey(
                        name: "FK_UserPermissions_Users_UserTelegramID",
                        column: x => x.UserTelegramID,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissions_UserTelegramID",
                table: "UserPermissions",
                column: "UserTelegramID");

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserTelegramID",
                table: "Users",
                column: "UserTelegramID",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserPermissions");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
