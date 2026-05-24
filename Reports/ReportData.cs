// ============================================================
//  REPORT DATA MODEL — Datos consolidados para el reporte
//  Archivo: Reports/ReportData.cs
//
//  Agrega toda la información del experimento, mediciones,
//  resultados y alertas en un único objeto que usan tanto
//  el generador PDF como el generador Word.
// ============================================================

namespace CavitationApi.Reports;

public class ReportData
{
    // ── Encabezado ────────────────────────────────────────────
    public string ExperimentName    { get; set; } = string.Empty;
    public string SampleName        { get; set; } = string.Empty;
    public string SampleDescription { get; set; } = string.Empty;
    public string MachineName       { get; set; } = string.Empty;
    public string OperatorName      { get; set; } = string.Empty;
    public DateTime StartTime       { get; set; }
    public DateTime? EndTime        { get; set; }
    public TimeSpan PlannedDuration { get; set; }
    public string Status            { get; set; } = string.Empty;
    public string? AbortReason      { get; set; }

    // ── Antecedente ───────────────────────────────────────────
    public bool HasPrecedent               { get; set; }
    public string? PrecedentExperimentName { get; set; }

    // ── Propiedades del agua ──────────────────────────────────
    public double InitialTemperature { get; set; }
    public double TargetFlowRate     { get; set; }
    public double FlowRateTolerance  { get; set; }

    // ── Imágenes de microscopio ───────────────────────────────
    public string? MicroscopeImageBeforePath { get; set; }
    public string? MicroscopeImageAfterPath  { get; set; }

    // ── Resultados finales ────────────────────────────────────
    public double FinalTemperature          { get; set; }
    public double AverageFlowRate           { get; set; }
    public double FlowRateCompliance        { get; set; }
    public double MaxTemperatureReached     { get; set; }
    public double MinFlowRateReached        { get; set; }
    public double MaxFlowRateReached        { get; set; }
    public double FlowRateControlMean       { get; set; }
    public double FlowRateUpperControlLimit { get; set; }
    public double FlowRateLowerControlLimit { get; set; }
    public string? Observations             { get; set; }
    public DateTime ResultGeneratedAt       { get; set; }

    // ── Mediciones (series de tiempo) ─────────────────────────
    public List<MeasurementRow> Measurements { get; set; } = new();

    // ── Subgrupos X̄-R ─────────────────────────────────────────
    public List<SubgroupRow> Subgroups { get; set; } = new();
    public double ControlChartGrandMean  { get; set; }
    public double ControlChartUCL        { get; set; }
    public double ControlChartLCL        { get; set; }
    public double ControlChartAvgRange   { get; set; }
    public double ControlChartUCLRange   { get; set; }
    public double ControlChartLCLRange   { get; set; }

    // ── Alertas ───────────────────────────────────────────────
    public List<AlertRow> Alerts { get; set; } = new();

    // ── Metadata del reporte ──────────────────────────────────
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public string GeneratedBy   { get; set; } = string.Empty;
}

// ── Filas para tablas ─────────────────────────────────────────

public class MeasurementRow
{
    public int      Index          { get; set; }
    public DateTime Timestamp      { get; set; }
    public double   Temperature    { get; set; }
    public double   FlowRate       { get; set; }
    public double   FlowRateTarget { get; set; }
    public double   FlowDeviation  { get; set; }
    public double?  Pressure       { get; set; }
}

public class SubgroupRow
{
    public int      SubgroupNumber { get; set; }
    public double   Mean           { get; set; }
    public double   Range          { get; set; }
    public DateTime Timestamp      { get; set; }
    public bool     AboveUCL       { get; set; }
    public bool     BelowLCL       { get; set; }
}

public class AlertRow
{
    public string   Type         { get; set; } = string.Empty;
    public string   Message      { get; set; } = string.Empty;
    public double   TriggerValue { get; set; }
    public bool     AutoShutdown { get; set; }
    public DateTime TriggeredAt  { get; set; }
}
