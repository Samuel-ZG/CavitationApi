// ============================================================
//  DTOs — Data Transfer Objects
//  Archivo: DTOs/AllDTOs.cs
// ============================================================

using CavitationApi.Models;

namespace CavitationApi.DTOs;

// ── Auth ──────────────────────────────────────────────────────

public record LoginRequest(string Email, string Password);

public record LoginResponse(
    string Token,
    string RefreshToken,
    DateTime ExpiresAt,
    OperatorDto Operator
);

public record RefreshTokenRequest(string RefreshToken);

// ── Operator ──────────────────────────────────────────────────

public record OperatorDto(
    int Id,
    string Name,
    string Email,
    bool IsActive,
    DateTime CreatedAt
);

public record CreateOperatorRequest(
    string Name,
    string Email,
    string Password
);

public record UpdateOperatorRequest(
    string Name,
    string Email,
    bool IsActive
);

// ── Machine ───────────────────────────────────────────────────

public record MachineDto(
    int Id,
    string Name,
    string SerialNumber,
    MachineStatus Status,
    double TemperatureLimitWarning,
    double TemperatureLimitCritical,
    double TargetFlowRate,
    double FlowRateTolerance,
    string SensorIpAddress,
    string SensorMqttTopic,
    bool IsConnected,
    int? AssignedOperatorId,
    string? AssignedOperatorName,
    DateTime UpdatedAt
);

public record CreateMachineRequest(
    string Name,
    string SerialNumber,
    double TemperatureLimitWarning,
    double TemperatureLimitCritical,
    double TargetFlowRate,
    double FlowRateTolerance,
    string SensorIpAddress,
    string SensorMqttTopic,
    int? AssignedOperatorId
);

public record UpdateMachineRequest(
    string Name,
    double TemperatureLimitWarning,
    double TemperatureLimitCritical,
    double TargetFlowRate,
    double FlowRateTolerance,
    string SensorIpAddress,
    string SensorMqttTopic,
    int? AssignedOperatorId
);

public record MachineStatusUpdateRequest(MachineStatus Status);

public record MachineCommandRequest(
    string Command  // "start" | "stop" | "emergency_stop"
);

// ── Experiment ────────────────────────────────────────────────

public record ExperimentDto(
    int Id,
    string Name,
    string SampleName,
    string SampleDescription,
    double InitialTemperature,
    string? MicroscopeImageBeforePath,
    string? MicroscopeImageAfterPath,
    bool HasPrecedent,
    int? PrecedentExperimentId,
    string? PrecedentExperimentName,
    DateTime StartTime,
    TimeSpan PlannedDuration,
    DateTime? EndTime,
    ExperimentStatus Status,
    string? AbortReason,
    double TargetFlowRate,
    int MachineId,
    string MachineName,
    int OperatorId,
    string OperatorName,
    DateTime CreatedAt
);

public record ExperimentSummaryDto(
    int Id,
    string Name,
    string SampleName,
    ExperimentStatus Status,
    DateTime StartTime,
    TimeSpan PlannedDuration,
    int MachineId,
    string MachineName,
    int OperatorId,
    string OperatorName
);

public record CreateExperimentRequest(
    string Name,
    string SampleName,
    string SampleDescription,
    double InitialTemperature,
    bool HasPrecedent,
    int? PrecedentExperimentId,
    DateTime StartTime,
    TimeSpan PlannedDuration,
    double TargetFlowRate,
    int MachineId
    // Las imágenes se suben por endpoint separado (multipart)
);

public record UpdateExperimentRequest(
    string Name,
    string SampleDescription,
    double TargetFlowRate,
    DateTime StartTime,
    TimeSpan PlannedDuration
);

public record UpdateExperimentStatusRequest(
    ExperimentStatus Status,
    string? AbortReason
);

// ── Measurement ───────────────────────────────────────────────

public record MeasurementDto(
    int Id,
    DateTime Timestamp,
    double Temperature,
    double FlowRate,
    double FlowRateTarget,
    double FlowDeviation,
    double? Pressure,
    double? SubgroupMean,
    double? SubgroupRange,
    int SubgroupNumber,
    int ExperimentId
);

public record CreateMeasurementRequest(
    int ExperimentId,
    double Temperature,
    double FlowRate,
    double? Pressure
    // FlowDeviation, SubgroupMean, SubgroupRange los calcula el servicio
);

// Payload que llega desde el sensor vía MQTT
public record SensorPayload(
    int MachineId,
    double Temperature,
    double FlowRate,
    double? Pressure,
    DateTime Timestamp
);

// Payload que se emite por SignalR en tiempo real
public record RealtimeMeasurementEvent(
    int MachineId,
    int ExperimentId,
    double Temperature,
    double FlowRate,
    double FlowDeviation,
    double? Pressure,
    DateTime Timestamp,
    bool TemperatureWarning,
    bool TemperatureCritical,
    bool FlowDeviated
);

// ── Result ────────────────────────────────────────────────────

public record ExperimentResultDto(
    int Id,
    int ExperimentId,
    string ExperimentName,
    double FinalTemperature,
    double AverageFlowRate,
    double FlowRateCompliance,
    double MaxTemperatureReached,
    double MinFlowRateReached,
    double MaxFlowRateReached,
    double FlowRateControlMean,
    double FlowRateUpperControlLimit,
    double FlowRateLowerControlLimit,
    string? Observations,
    string? MicroscopeImageBeforePath,
    string? MicroscopeImageAfterPath,
    DateTime GeneratedAt
);

public record CreateResultRequest(
    int ExperimentId,
    double FinalTemperature,
    string? Observations
    // El resto de métricas las calcula el servicio desde las mediciones
);

// ── Alert ─────────────────────────────────────────────────────

public record AlertDto(
    int Id,
    AlertType Type,
    string Message,
    double TriggerValue,
    double ThresholdValue,
    bool AutoShutdown,
    bool AcknowledgedByOperator,
    DateTime TriggeredAt,
    int ExperimentId,
    int MachineId,
    string MachineName
);

public record AcknowledgeAlertRequest(int AlertId);

// ── Paginación ────────────────────────────────────────────────

public record PagedResult<T>(
    IEnumerable<T> Items,
    int TotalCount,
    int Page,
    int PageSize
);
