// ============================================================
//  INTERFACES DE SERVICIOS
//  Archivo: Services/IServices.cs
// ============================================================

using CavitationApi.DTOs;
using CavitationApi.Models;

namespace CavitationApi.Services;

// ── Auth ──────────────────────────────────────────────────────

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request);
    Task<LoginResponse?> RefreshTokenAsync(string refreshToken);
    Task RevokeRefreshTokenAsync(string refreshToken);
    Task<OperatorDto?> GetCurrentOperatorAsync(int operatorId);
}

// ── Machine ───────────────────────────────────────────────────

public interface IMachineService
{
    Task<IEnumerable<MachineDto>> GetAllAsync();
    Task<IEnumerable<MachineDto>> GetByOperatorAsync(int operatorId);
    Task<MachineDto?> GetByIdAsync(int id);
    Task<MachineDto> CreateAsync(CreateMachineRequest request);
    Task<MachineDto?> UpdateAsync(int id, UpdateMachineRequest request);
    Task<bool> UpdateStatusAsync(int id, MachineStatus status);
    Task<bool> SendCommandAsync(int id, string command);
    Task<bool> AssignOperatorAsync(int machineId, int operatorId);
    Task<bool> DeleteAsync(int id);
}

// ── Experiment ────────────────────────────────────────────────

public interface IExperimentService
{
    Task<PagedResult<ExperimentSummaryDto>> GetPagedAsync(int page, int pageSize, int? operatorId = null, int? machineId = null);
    Task<ExperimentDto?> GetByIdAsync(int id);
    Task<ExperimentDto> CreateAsync(CreateExperimentRequest request, int operatorId);
    Task<ExperimentDto?> UpdateAsync(int id, UpdateExperimentRequest request);
    Task<bool> UpdateStatusAsync(int id, UpdateExperimentStatusRequest request);
    Task<bool> SetMicroscopeImageAsync(int id, string imagePath, bool isBefore);
    Task<IEnumerable<ExperimentSummaryDto>> GetByMachineAsync(int machineId);
    Task<bool> DeleteAsync(int id);
}

// ── Measurement ───────────────────────────────────────────────

public interface IMeasurementService
{
    Task<IEnumerable<MeasurementDto>> GetByExperimentAsync(int experimentId);
    
    Task<MeasurementDto> RecordAsync(CreateMeasurementRequest request);
    Task<MeasurementDto> RecordFromSensorAsync(SensorPayload payload);
    // Calcula los límites de control X̄-R para el gráfico
    Task<ControlChartData> GetControlChartDataAsync(int experimentId, int subgroupSize = 5);
    
    // Carta de individuales para un solo experimento
    Task<IndividualChartData> GetIndividualChartDataAsync(int experimentId);

// Carta entre experimentos (por máquina o todas)
    Task<CrossExperimentChartData> GetCrossExperimentChartDataAsync(
        int? machineId = null,
        int? operatorId = null,
        int maxExperiments = 25);
}

// ── Result ────────────────────────────────────────────────────

public interface IResultService
{
    Task<ExperimentResultDto?> GetByExperimentAsync(int experimentId);
    Task<ExperimentResultDto> GenerateAsync(int experimentId, string? observations);
}

// ── Alert ─────────────────────────────────────────────────────

public interface IAlertService
{
    Task<IEnumerable<AlertDto>> GetByExperimentAsync(int experimentId);
    Task<IEnumerable<AlertDto>> GetActiveByOperatorAsync(int operatorId);
    Task<AlertDto> CreateAsync(int experimentId, int machineId, AlertType type, string message,
        double triggerValue, double thresholdValue, bool autoShutdown);
    Task<bool> AcknowledgeAsync(int alertId, int operatorId);
}

// ── Report ────────────────────────────────────────────────────

public interface IReportService
{
    Task<string> GeneratePdfAsync(int experimentId);  // retorna ruta del archivo
    Task<string> GenerateWordAsync(int experimentId); // retorna ruta del archivo
}

// ── File Storage ──────────────────────────────────────────────

public interface IFileStorageService
{
    Task<string> SaveImageAsync(Stream imageStream, string fileName, string folder);
    Task<bool> DeleteAsync(string filePath);
    string GetPublicUrl(string filePath);
}

// ── MQTT Client ───────────────────────────────────────────────

public interface IMqttClientService
{
    Task StartAsync();
    Task StopAsync();
    Task PublishAsync(string topic, string payload);
    bool IsConnected { get; }
    event Func<string, string, Task>? MessageReceived;
}

// ── DTO auxiliar para gráfico de control ─────────────────────

public record ControlChartData(
    double GrandMean,           // X̄ general
    double UpperControlLimit,   // UCL = X̄ + A2 * R̄
    double LowerControlLimit,   // LCL = X̄ - A2 * R̄
    double AverageRange,        // R̄
    double UpperRangeLimit,     // UCL del gráfico R = D4 * R̄
    double LowerRangeLimit,     // LCL del gráfico R = D3 * R̄
    IEnumerable<SubgroupPoint> Subgroups
);

public record SubgroupPoint(
    int SubgroupNumber,
    double Mean,
    double Range,
    DateTime Timestamp
);

// ── Carta de individuales (X-MR) ─────────────────────────────
// Para un experimento con cualquier cantidad de mediciones

public record IndividualChartData(
    double GrandMean,               // X̄ general
    double UpperControlLimit,       // UCL = X̄ + 3×(MR̄/d2)
    double LowerControlLimit,       // LCL = X̄ - 3×(MR̄/d2)
    double AverageMovingRange,      // MR̄
    double UpperRangeLimitMR,       // UCL_MR = D4 × MR̄  (D4=3.267 para n=2)
    IEnumerable<IndividualPoint> Points
);

public record IndividualPoint(
    int    Index,
    double Value,
    double? MovingRange,
    DateTime Timestamp,
    bool   AboveUCL,
    bool   BelowLCL,
    bool   MRAboveUCL
);

// ── Carta entre experimentos ──────────────────────────────────
// Cada experimento es una observación (su caudal promedio)

public record CrossExperimentChartData(
    double GrandMean,
    double UpperControlLimit,
    double LowerControlLimit,
    double AverageMovingRange,
    double UpperRangeLimitMR,
    IEnumerable<CrossExperimentPoint> Points
);

public record CrossExperimentPoint(
    int      ExperimentId,
    string   ExperimentName,
    DateTime StartTime,
    double   AverageFlowRate,
    double?  MovingRange,
    bool     AboveUCL,
    bool     BelowLCL
);