using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerMonitoringApp.Migrations
{
    /// <inheritdoc />
    public partial class newdb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop foreign key constraints that reference the Users table
            migrationBuilder.Sql(@"
        DECLARE @sql NVARCHAR(MAX) = N'';
        SELECT @sql += 'ALTER TABLE ' + t.name + ' DROP CONSTRAINT ' + fk.name + ';'
        FROM sys.foreign_keys fk
        INNER JOIN sys.tables t ON fk.parent_object_id = t.object_id
        WHERE fk.referenced_object_id = OBJECT_ID('dbo.Users');
        EXEC sp_executesql @sql;
    ");
            // Drop the 'UserPermissions' table if it exists
            migrationBuilder.Sql("IF OBJECT_ID('dbo.UserPermissions', 'U') IS NOT NULL DROP TABLE dbo.UserPermissions;");

            // Drop the 'Users' table if it exists
            migrationBuilder.Sql("IF OBJECT_ID('dbo.Users', 'U') IS NOT NULL DROP TABLE dbo.Users;");

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
                    UserTelegramID = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "CallHistories",
                columns: table => new
                {
                    CallId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourcePhoneNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DestinationPhoneNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CallDateTime = table.Column<DateTime>(type: "datetime", nullable: false),
                    Duration = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CallType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CallerUserId = table.Column<int>(type: "int", nullable: true),
                    RecipientUserId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallHistories", x => x.CallId);
                    table.ForeignKey(
                        name: "FK_CallHistories_Users_CallerUserId",
                        column: x => x.CallerUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CallHistories_Users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CallHistories_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
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
                name: "IX_CallHistories_CallerUserId",
                table: "CallHistories",
                column: "CallerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CallHistories_RecipientUserId",
                table: "CallHistories",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CallHistories_UserId",
                table: "CallHistories",
                column: "UserId");

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
                name: "CallHistories");

            migrationBuilder.DropTable(
                name: "UserPermissions");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
