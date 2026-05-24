// ============================================================
//  RESULT SERVICE
//  Archivo: Services/ResultService.cs
//
//  Calcula automáticamente desde las mediciones guardadas:
//  - Temperatura y caudal finales
//  - % de compliance del caudal
//  - Límites de control X̄-R para el reporte
// ============================================================

using AutoMapper;
using CavitationApi.Data;
using CavitationApi.DTOs;
using CavitationApi.Models;
using Microsoft.EntityFrameworkCore;

namespace CavitationApi.Services;

public class ResultService : IResultService
{
    private readonly AppDbContext _db;
    private readonly IMapper _mapper;
    private readonly IMeasurementService _measurementService;
    private readonly ILogger<ResultService> _logger;

    public ResultService(
        AppDbContext db,
        IMapper mapper,
        IMeasurementService measurementService,
        ILogger<ResultService> logger)
    {
        _db = db;
        _mapper = mapper;
        _measurementService = measurementService;
        _logger = logger;
    }

    // ── Consulta ──────────────────────────────────────────────

    public async Task<ExperimentResultDto?> GetByExperimentAsync(int experimentId)
    {
        var result = await _db.ExperimentResults
            .Include(r => r.Experiment)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ExperimentId == experimentId);

        return result is null ? null : _mapper.Map<ExperimentResultDto>(result);
    }

    // ── Generar resultado final ───────────────────────────────

    public async Task<ExperimentResultDto> GenerateAsync(int experimentId, string? observations)
    {
        var experiment = await _db.Experiments
            .Include(x => x.Machine)
            .FirstOrDefaultAsync(x => x.Id == experimentId);

        if (experiment is null)
            throw new InvalidOperationException($"Experimento {experimentId} no encontrado.");

        if (experiment.Status != ExperimentStatus.Completed
            && experiment.Status != ExperimentStatus.Aborted)
            throw new InvalidOperationException(
                "Solo se pueden generar resultados de experimentos completados o abortados.");

        // Eliminar resultado previo si existe (regeneración)
        var existing = await _db.ExperimentResults
            .FirstOrDefaultAsync(r => r.ExperimentId == experimentId);
        if (existing is not null)
        {
            _db.ExperimentResults.Remove(existing);
            await _db.SaveChangesAsync();
        }

        // Cargar todas las mediciones
        var measurements = await _db.Measurements
            .Where(m => m.ExperimentId == experimentId)
            .OrderBy(m => m.Timestamp)
            .AsNoTracking()
            .ToListAsync();

        if (!measurements.Any())
            throw new InvalidOperationException(
                "No hay mediciones registradas para generar resultados.");

        // ── Calcular métricas ─────────────────────────────────

        var flowRates    = measurements.Select(m => m.FlowRate).ToList();
        var temperatures = measurements.Select(m => m.Temperature).ToList();

        var avgFlow         = flowRates.Average();
        var maxTemp         = temperatures.Max();
        var finalTemp       = temperatures.Last();
        var minFlow         = flowRates.Min();
        var maxFlow         = flowRates.Max();

        // % del tiempo dentro de tolerancia del caudal (±5% por defecto, o lo configurado en máquina)
        var tolerance  = experiment.Machine.FlowRateTolerance;
        var target     = experiment.TargetFlowRate;
        var inTolerance = measurements.Count(m =>
            Math.Abs(m.FlowRate - target) / target <= tolerance);
        var compliance = measurements.Count > 0
            ? (double)inTolerance / measurements.Count * 100.0
            : 0.0;

        // Datos para gráfico X̄-R
        var chartData = await _measurementService.GetControlChartDataAsync(experimentId, subgroupSize: 5);

        var result = new ExperimentResult
        {
            ExperimentId               = experimentId,
            FinalTemperature           = Math.Round(finalTemp, 2),
            AverageFlowRate            = Math.Round(avgFlow, 3),
            FlowRateCompliance         = Math.Round(compliance, 2),
            MaxTemperatureReached      = Math.Round(maxTemp, 2),
            MinFlowRateReached         = Math.Round(minFlow, 3),
            MaxFlowRateReached         = Math.Round(maxFlow, 3),
            FlowRateControlMean        = chartData.GrandMean,
            FlowRateUpperControlLimit  = chartData.UpperControlLimit,
            FlowRateLowerControlLimit  = chartData.LowerControlLimit,
            Observations               = observations,
            GeneratedAt                = DateTime.UtcNow
        };

        _db.ExperimentResults.Add(result);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Resultado generado para experimento {Id} — Compliance: {Compliance}%",
            experimentId, compliance);

        await _db.Entry(result).Reference(r => r.Experiment).LoadAsync();
        return _mapper.Map<ExperimentResultDto>(result);
    }
}
