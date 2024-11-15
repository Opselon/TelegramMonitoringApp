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
                name: "CallHistories",
                columns: table => new
                {
                    CallId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourcePhoneNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DestinationPhoneNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CallDateTime = table.Column<string>(type: "nvarchar(max)", maxLength: 20, nullable: false),
                    Duration = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CallType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallHistories", x => x.CallId);
                });

            migrationBuilder.CreateTable(
                name: "CallHistoryWithUserNames",
                columns: table => new
                {
                    CallId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourcePhoneNumber = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    DestinationPhoneNumber = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    CallDateTime = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Duration = table.Column<int>(type: "int", nullable: false),
                    CallType = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CallerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReceiverName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallHistoryWithUserNames", x => x.CallId);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserNameProfile = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UserNumberFile = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UserNameFile = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    UserFamilyFile = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    UserFatherNameFile = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UserBirthDayFile = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    UserAddressFile = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    UserDescriptionFile = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UserSourceFile = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UserTelegramID = table.Column<long>(type: "bigint", nullable: true)
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
                        name: "FK_UserPermissions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CallHistories_DestinationPhoneNumber",
                table: "CallHistories",
                column: "DestinationPhoneNumber");

            migrationBuilder.CreateIndex(
                name: "IX_CallHistories_SourcePhoneNumber",
                table: "CallHistories",
                column: "SourcePhoneNumber");

            migrationBuilder.CreateIndex(
                name: "IX_CallHistoryWithUserNames_DestinationPhoneNumber",
                table: "CallHistoryWithUserNames",
                column: "DestinationPhoneNumber");

            migrationBuilder.CreateIndex(
                name: "IX_CallHistoryWithUserNames_SourcePhoneNumber",
                table: "CallHistoryWithUserNames",
                column: "SourcePhoneNumber");

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissions_UserId",
                table: "UserPermissions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserNumberFile",
                table: "Users",
                column: "UserNumberFile");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CallHistories");

            migrationBuilder.DropTable(
                name: "CallHistoryWithUserNames");

            migrationBuilder.DropTable(
                name: "UserPermissions");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
