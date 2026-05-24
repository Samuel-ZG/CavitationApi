// ============================================================
//  MODELOS DE DOMINIO — Sistema de cavitación
//  Archivo: Models/Domain.cs
// ============================================================

namespace CavitationApi.Models;

// ── Enumeraciones ─────────────────────────────────────────────

public enum MachineStatus
{
    Available,   // Disponible
    InUse,       // En uso
    Maintenance, // Mantenimiento
    Error,       // Error / falla
    EmergencyOff // Apagado por emergencia
}

public enum ExperimentStatus
{
    Pending,    // Pendiente de iniciar
    InProgress, // En proceso
    Paused,     // Pausado
    Completed,  // Completado
    Aborted     // Abortado (emergencia)
}

public enum AlertType
{
    Warning,      // Advertencia — temperatura cerca del límite
    Critical,     // Crítica — temperatura superó el límite; se apaga la máquina
    FlowDeviation // Desviación de caudal fuera del rango aceptable
}

// ── Operario ──────────────────────────────────────────────────

public class Operator
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    // Navegación
    public ICollection<Machine> AssignedMachines { get; set; } = new List<Machine>();
    public ICollection<Experiment> Experiments { get; set; } = new List<Experiment>();
    public ICollection<RefreshTokenEntry> RefreshTokens { get; set; } = new List<RefreshTokenEntry>();
}

// ── Máquina ───────────────────────────────────────────────────

public class Machine
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public MachineStatus Status { get; set; } = MachineStatus.Available;

    // Configuración de límites de seguridad
    public double TemperatureLimitWarning { get; set; }  // °C — alerta al operario
    public double TemperatureLimitCritical { get; set; } // °C — apagado automático

    // Caudal predefinido para el experimento (L/min)
    public double TargetFlowRate { get; set; }
    public double FlowRateTolerance { get; set; } = 0.05; // 5% de tolerancia por defecto

    // Conectividad del sensor
    public string SensorIpAddress { get; set; } = string.Empty;
    public string SensorMqttTopic { get; set; } = string.Empty;
    public bool IsConnected { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navegación
    public int? AssignedOperatorId { get; set; }
    public Operator? AssignedOperator { get; set; }
    public ICollection<Experiment> Experiments { get; set; } = new List<Experiment>();
    public ICollection<Alert> Alerts { get; set; } = new List<Alert>();
}

// ── Experimento ───────────────────────────────────────────────

public class Experiment
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Muestra de agua
    public string SampleName { get; set; } = string.Empty;
    public string SampleDescription { get; set; } = string.Empty;
    public double InitialTemperature { get; set; }
    public string? MicroscopeImageBeforePath { get; set; }
    public string? MicroscopeImageAfterPath { get; set; }

    // Antecedente
    public bool HasPrecedent { get; set; } = false;
    public int? PrecedentExperimentId { get; set; }
    public Experiment? PrecedentExperiment { get; set; }

    // Tiempos
    public DateTime StartTime { get; set; }
    public TimeSpan PlannedDuration { get; set; }
    public DateTime? EndTime { get; set; }

    // Estado
    public ExperimentStatus Status { get; set; } = ExperimentStatus.Pending;
    public string? AbortReason { get; set; }

    // Caudal objetivo
    public double TargetFlowRate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navegación
    public int MachineId { get; set; }
    public Machine Machine { get; set; } = null!;
    public int OperatorId { get; set; }
    public Operator Operator { get; set; } = null!;
    public ICollection<Measurement> Measurements { get; set; } = new List<Measurement>();
    public ExperimentResult? Result { get; set; }
    public ICollection<Alert> Alerts { get; set; } = new List<Alert>();
}

// ── Medición ──────────────────────────────────────────────────

public class Measurement
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public double Temperature { get; set; }
    public double FlowRate { get; set; }
    public double FlowRateTarget { get; set; }
    public double FlowDeviation { get; set; }
    public double? Pressure { get; set; }

    public double? SubgroupMean { get; set; }
    public double? SubgroupRange { get; set; }
    public int SubgroupNumber { get; set; }

    // Navegación
    public int ExperimentId { get; set; }
    public Experiment Experiment { get; set; } = null!;
}

// ── Resultado final ───────────────────────────────────────────

public class ExperimentResult
{
    public int Id { get; set; }

    public double FinalTemperature { get; set; }
    public double AverageFlowRate { get; set; }
    public double FlowRateCompliance { get; set; }
    public double MaxTemperatureReached { get; set; }
    public double MinFlowRateReached { get; set; }
    public double MaxFlowRateReached { get; set; }

    public double FlowRateControlMean { get; set; }
    public double FlowRateUpperControlLimit { get; set; }
    public double FlowRateLowerControlLimit { get; set; }

    public string? Observations { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    // Navegación
    public int ExperimentId { get; set; }
    public Experiment Experiment { get; set; } = null!;
}

// ── Alerta ────────────────────────────────────────────────────

public class Alert
{
    public int Id { get; set; }
    public AlertType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public double TriggerValue { get; set; }
    public double ThresholdValue { get; set; }
    public bool AutoShutdown { get; set; }
    public bool AcknowledgedByOperator { get; set; } = false;
    public DateTime TriggeredAt { get; set; } = DateTime.UtcNow;

    // Navegación
    public int ExperimentId { get; set; }
    public Experiment Experiment { get; set; } = null!;
    public int MachineId { get; set; }
    public Machine Machine { get; set; } = null!;
}

// ── Refresh Token ─────────────────────────────────────────────

public class RefreshTokenEntry
{
    public int Id { get; set; }
    public string Token { get; set; } = string.Empty;
    public int OperatorId { get; set; }
    public Operator Operator { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime CreatedAt { get; set; }
}