// ============================================================
//  REPORT DATA BUILDER
//  Archivo: Reports/ReportDataBuilder.cs
//
//  Consolida datos de experimento, mediciones, resultados
//  y alertas en un único ReportData listo para renderizar.
// ============================================================

using CavitationApi.Data;
using CavitationApi.Services;
using Microsoft.EntityFrameworkCore;

namespace CavitationApi.Reports;

public class ReportDataBuilder
{
    private readonly AppDbContext _db;
    private readonly IMeasurementService _measurementService;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;

    public ReportDataBuilder(
        AppDbContext db,
        IMeasurementService measurementService,
        IWebHostEnvironment env,
        IConfiguration config)
    {
        _db                 = db;
        _measurementService = measurementService;
        _env                = env;
        _config             = config;
    }

    public async Task<ReportData> BuildAsync(int experimentId)
    {
        // ── Cargar experimento completo ───────────────────────
        var experiment = await _db.Experiments
            .Include(x => x.Machine)
            .Include(x => x.Operator)
            .Include(x => x.PrecedentExperiment)
            .Include(x => x.Result)
            .Include(x => x.Alerts)
                .ThenInclude(a => a.Machine)
            .Include(x => x.Measurements.OrderBy(m => m.Timestamp))
            .FirstOrDefaultAsync(x => x.Id == experimentId)
            ?? throw new KeyNotFoundException($"Experimento {experimentId} no encontrado.");

        if (experiment.Result is null)
            throw new InvalidOperationException(
                "Debe generar los resultados antes de exportar el reporte.");

        // ── Datos de control chart ─────────────────────────────
        var chartData = await _measurementService
            .GetControlChartDataAsync(experimentId, subgroupSize: 5);

        var ucl = chartData.UpperControlLimit;
        var lcl = chartData.LowerControlLimit;

        // ── Construir objeto ──────────────────────────────────
        var data = new ReportData
        {
            // Encabezado
            ExperimentName    = experiment.Name,
            SampleName        = experiment.SampleName,
            SampleDescription = experiment.SampleDescription,
            MachineName       = experiment.Machine.Name,
            OperatorName      = experiment.Operator.Name,
            StartTime         = experiment.StartTime,
            EndTime           = experiment.EndTime,
            PlannedDuration   = experiment.PlannedDuration,
            Status            = experiment.Status.ToString(),
            AbortReason       = experiment.AbortReason,
            GeneratedBy       = experiment.Operator.Name,
            GeneratedAt       = DateTime.Now,

            // Antecedente
            HasPrecedent             = experiment.HasPrecedent,
            PrecedentExperimentName  = experiment.PrecedentExperiment?.Name,

            // Propiedades del agua
            InitialTemperature = experiment.InitialTemperature,
            TargetFlowRate     = experiment.TargetFlowRate,
            FlowRateTolerance  = experiment.Machine.FlowRateTolerance,

            // Imágenes — rutas absolutas en disco para que iText7/DocX las lean
            MicroscopeImageBeforePath = ResolveImagePath(experiment.MicroscopeImageBeforePath),
            MicroscopeImageAfterPath  = ResolveImagePath(experiment.MicroscopeImageAfterPath),

            // Resultados
            FinalTemperature          = experiment.Result.FinalTemperature,
            AverageFlowRate           = experiment.Result.AverageFlowRate,
            FlowRateCompliance        = experiment.Result.FlowRateCompliance,
            MaxTemperatureReached     = experiment.Result.MaxTemperatureReached,
            MinFlowRateReached        = experiment.Result.MinFlowRateReached,
            MaxFlowRateReached        = experiment.Result.MaxFlowRateReached,
            FlowRateControlMean       = experiment.Result.FlowRateControlMean,
            FlowRateUpperControlLimit = experiment.Result.FlowRateUpperControlLimit,
            FlowRateLowerControlLimit = experiment.Result.FlowRateLowerControlLimit,
            Observations              = experiment.Result.Observations,
            ResultGeneratedAt         = experiment.Result.GeneratedAt,

            // Control chart
            ControlChartGrandMean  = chartData.GrandMean,
            ControlChartUCL        = chartData.UpperControlLimit,
            ControlChartLCL        = chartData.LowerControlLimit,
            ControlChartAvgRange   = chartData.AverageRange,
            ControlChartUCLRange   = chartData.UpperRangeLimit,
            ControlChartLCLRange   = chartData.LowerRangeLimit,

            // Mediciones
            Measurements = experiment.Measurements
                .Select((m, i) => new MeasurementRow
                {
                    Index          = i + 1,
                    Timestamp      = m.Timestamp,
                    Temperature    = m.Temperature,
                    FlowRate       = m.FlowRate,
                    FlowRateTarget = m.FlowRateTarget,
                    FlowDeviation  = m.FlowDeviation,
                    Pressure       = m.Pressure
                }).ToList(),

            // Subgrupos X̄-R
            Subgroups = chartData.Subgroups.Select(s => new SubgroupRow
            {
                SubgroupNumber = s.SubgroupNumber,
                Mean           = s.Mean,
                Range          = s.Range,
                Timestamp      = s.Timestamp,
                AboveUCL       = s.Mean > ucl,
                BelowLCL       = s.Mean < lcl
            }).ToList(),

            // Alertas
            Alerts = experiment.Alerts
                .OrderBy(a => a.TriggeredAt)
                .Select(a => new AlertRow
                {
                    Type         = a.Type.ToString(),
                    Message      = a.Message,
                    TriggerValue = a.TriggerValue,
                    AutoShutdown = a.AutoShutdown,
                    TriggeredAt  = a.TriggeredAt
                }).ToList()
        };

        return data;
    }

    // ── Helper: ruta absoluta en disco ────────────────────────

    private string? ResolveImagePath(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return null;

        // TODO NUBE: Con Blob/S3 descargar la imagen a un archivo temporal
        // antes de pasarla al generador de reportes
        var basePath = _config["FileStorage:BasePath"] ?? "uploads";
        var fullPath = Path.Combine(_env.ContentRootPath, basePath, relativePath);
        return File.Exists(fullPath) ? fullPath : null;
    }
}
