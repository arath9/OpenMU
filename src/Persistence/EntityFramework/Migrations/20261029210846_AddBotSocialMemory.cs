using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MUnique.OpenMU.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddBotSocialMemory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BotEventLogs",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uuid", nullable: false),
                    BotName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    Context = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BotEventLogs", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "BotReputationRecords",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uuid", nullable: false),
                    BotName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TargetName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CurrentReputation = table.Column<int>(type: "integer", nullable: false),
                    TotalInteractions = table.Column<int>(type: "integer", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BotReputationRecords", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "BotSocialInteractions",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uuid", nullable: false),
                    BotName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TargetName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    InteractionType = table.Column<int>(type: "integer", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BotSocialInteractions", x => x.ID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BotEventLogs_BotName_Timestamp",
                table: "BotEventLogs",
                columns: new[] { "BotName", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_BotReputationRecords_BotName_TargetName",
                table: "BotReputationRecords",
                columns: new[] { "BotName", "TargetName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BotSocialInteractions_BotName_Timestamp",
                table: "BotSocialInteractions",
                columns: new[] { "BotName", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BotEventLogs");

            migrationBuilder.DropTable(
                name: "BotReputationRecords");

            migrationBuilder.DropTable(
                name: "BotSocialInteractions");
        }
    }
}
