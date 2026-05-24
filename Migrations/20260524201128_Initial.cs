using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CavitationApi.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Operators",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Operators", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Machines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TemperatureLimitWarning = table.Column<double>(type: "float(6)", precision: 6, scale: 2, nullable: false),
                    TemperatureLimitCritical = table.Column<double>(type: "float(6)", precision: 6, scale: 2, nullable: false),
                    TargetFlowRate = table.Column<double>(type: "float(8)", precision: 8, scale: 3, nullable: false),
                    FlowRateTolerance = table.Column<double>(type: "float(5)", precision: 5, scale: 4, nullable: false),
                    SensorIpAddress = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SensorMqttTopic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsConnected = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AssignedOperatorId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Machines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Machines_Operators_AssignedOperatorId",
                        column: x => x.AssignedOperatorId,
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Token = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OperatorId = table.Column<int>(type: "int", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsRevoked = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Operators_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Experiments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SampleName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SampleDescription = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    InitialTemperature = table.Column<double>(type: "float(6)", precision: 6, scale: 2, nullable: false),
                    MicroscopeImageBeforePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MicroscopeImageAfterPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    HasPrecedent = table.Column<bool>(type: "bit", nullable: false),
                    PrecedentExperimentId = table.Column<int>(type: "int", nullable: true),
                    StartTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PlannedDuration = table.Column<TimeSpan>(type: "time", nullable: false),
                    EndTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AbortReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TargetFlowRate = table.Column<double>(type: "float(8)", precision: 8, scale: 3, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MachineId = table.Column<int>(type: "int", nullable: false),
                    OperatorId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Experiments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Experiments_Experiments_PrecedentExperimentId",
                        column: x => x.PrecedentExperimentId,
                        principalTable: "Experiments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Experiments_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Experiments_Operators_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Alerts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TriggerValue = table.Column<double>(type: "float(8)", precision: 8, scale: 3, nullable: false),
                    ThresholdValue = table.Column<double>(type: "float(8)", precision: 8, scale: 3, nullable: false),
                    AutoShutdown = table.Column<bool>(type: "bit", nullable: false),
                    AcknowledgedByOperator = table.Column<bool>(type: "bit", nullable: false),
                    TriggeredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExperimentId = table.Column<int>(type: "int", nullable: false),
                    MachineId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Alerts_Experiments_ExperimentId",
                        column: x => x.ExperimentId,
                        principalTable: "Experiments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Alerts_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExperimentResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FinalTemperature = table.Column<double>(type: "float(6)", precision: 6, scale: 2, nullable: false),
                    AverageFlowRate = table.Column<double>(type: "float(8)", precision: 8, scale: 3, nullable: false),
                    FlowRateCompliance = table.Column<double>(type: "float(5)", precision: 5, scale: 2, nullable: false),
                    MaxTemperatureReached = table.Column<double>(type: "float", nullable: false),
                    MinFlowRateReached = table.Column<double>(type: "float", nullable: false),
                    MaxFlowRateReached = table.Column<double>(type: "float", nullable: false),
                    FlowRateControlMean = table.Column<double>(type: "float(8)", precision: 8, scale: 4, nullable: false),
                    FlowRateUpperControlLimit = table.Column<double>(type: "float(8)", precision: 8, scale: 4, nullable: false),
                    FlowRateLowerControlLimit = table.Column<double>(type: "float(8)", precision: 8, scale: 4, nullable: false),
                    Observations = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    GeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExperimentId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExperimentResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExperimentResults_Experiments_ExperimentId",
                        column: x => x.ExperimentId,
                        principalTable: "Experiments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Measurements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Temperature = table.Column<double>(type: "float(6)", precision: 6, scale: 2, nullable: false),
                    FlowRate = table.Column<double>(type: "float(8)", precision: 8, scale: 3, nullable: false),
                    FlowRateTarget = table.Column<double>(type: "float(8)", precision: 8, scale: 3, nullable: false),
                    FlowDeviation = table.Column<double>(type: "float(6)", precision: 6, scale: 4, nullable: false),
                    Pressure = table.Column<double>(type: "float(6)", precision: 6, scale: 2, nullable: true),
                    SubgroupMean = table.Column<double>(type: "float(8)", precision: 8, scale: 4, nullable: true),
                    SubgroupRange = table.Column<double>(type: "float(8)", precision: 8, scale: 4, nullable: true),
                    SubgroupNumber = table.Column<int>(type: "int", nullable: false),
                    ExperimentId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Measurements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Measurements_Experiments_ExperimentId",
                        column: x => x.ExperimentId,
                        principalTable: "Experiments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Operators",
                columns: new[] { "Id", "CreatedAt", "Email", "IsActive", "Name", "PasswordHash" },
                values: new object[] { 1, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "operario@demo.com", true, "Operario Demo", "$2a$11$TqUBSVuVFpSXNMQWpuMBEe5QbbUTKDpGNOhPaOPNEJXepiUFvL.Im" });

            migrationBuilder.InsertData(
                table: "Machines",
                columns: new[] { "Id", "AssignedOperatorId", "CreatedAt", "FlowRateTolerance", "IsConnected", "Name", "SensorIpAddress", "SensorMqttTopic", "SerialNumber", "Status", "TargetFlowRate", "TemperatureLimitCritical", "TemperatureLimitWarning", "UpdatedAt" },
                values: new object[,]
                {
                    { 1, 1, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0.050000000000000003, false, "Máquina Cavitación #1", "192.168.1.11", "cavitation/machine/1/sensors", "CAV-0001", 0, 5.0, 85.0, 70.0, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 2, 1, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0.050000000000000003, false, "Máquina Cavitación #2", "192.168.1.12", "cavitation/machine/2/sensors", "CAV-0002", 0, 5.0, 85.0, 70.0, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 3, 1, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0.050000000000000003, false, "Máquina Cavitación #3", "192.168.1.13", "cavitation/machine/3/sensors", "CAV-0003", 0, 5.0, 85.0, 70.0, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 4, 1, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0.050000000000000003, false, "Máquina Cavitación #4", "192.168.1.14", "cavitation/machine/4/sensors", "CAV-0004", 0, 5.0, 85.0, 70.0, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_ExperimentId_TriggeredAt",
                table: "Alerts",
                columns: new[] { "ExperimentId", "TriggeredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_MachineId",
                table: "Alerts",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_ExperimentResults_ExperimentId",
                table: "ExperimentResults",
                column: "ExperimentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Experiments_MachineId",
                table: "Experiments",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_Experiments_OperatorId",
                table: "Experiments",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_Experiments_PrecedentExperimentId",
                table: "Experiments",
                column: "PrecedentExperimentId");

            migrationBuilder.CreateIndex(
                name: "IX_Machines_AssignedOperatorId",
                table: "Machines",
                column: "AssignedOperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_Measurements_ExperimentId_Timestamp",
                table: "Measurements",
                columns: new[] { "ExperimentId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_Operators_Email",
                table: "Operators",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_OperatorId",
                table: "RefreshTokens",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_Token",
                table: "RefreshTokens",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Alerts");

            migrationBuilder.DropTable(
                name: "ExperimentResults");

            migrationBuilder.DropTable(
                name: "Measurements");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "Experiments");

            migrationBuilder.DropTable(
                name: "Machines");

            migrationBuilder.DropTable(
                name: "Operators");
        }
    }
}
