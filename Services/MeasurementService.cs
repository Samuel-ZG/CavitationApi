// ============================================================
//  MEASUREMENT SERVICE
//  Archivo: Services/MeasurementService.cs
//
//  Responsabilidades:
//  - Registrar mediciones manuales y desde sensores MQTT
//  - Calcular subgrupos y estadísticos para gráfico X̄-R
//  - Calcular desviación de caudal vs objetivo
// ============================================================

using AutoMapper;
using CavitationApi.Data;
using CavitationApi.DTOs;
using CavitationApi.Models;
using Microsoft.EntityFrameworkCore;

namespace CavitationApi.Services;

public class MeasurementService : IMeasurementService
{
    // Constantes para gráfico X̄-R según tamaño de subgrupo n=5 (más común)
    // Fuente: tabla de constantes de control estadístico de procesos
    private static readonly Dictionary<int, (double A2, double D3, double D4)> ControlConstants = new()
    {
        { 2,  (A2: 1.880, D3: 0.000, D4: 3.267) },
        { 3,  (A2: 1.023, D3: 0.000, D4: 2.574) },
        { 4,  (A2: 0.729, D3: 0.000, D4: 2.282) },
        { 5,  (A2: 0.577, D3: 0.000, D4: 2.114) },
        { 6,  (A2: 0.483, D3: 0.000, D4: 2.004) },
        { 7,  (A2: 0.419, D3: 0.076, D4: 1.924) },
        { 8,  (A2: 0.373, D3: 0.136, D4: 1.864) },
        { 9,  (A2: 0.337, D3: 0.184, D4: 1.816) },
        { 10, (A2: 0.308, D3: 0.223, D4: 1.777) },
    };

    private readonly AppDbContext _db;
    private readonly IMapper _mapper;
    private readonly ILogger<MeasurementService> _logger;

    public MeasurementService(
        AppDbContext db,
        IMapper mapper,
        ILogger<MeasurementService> logger)
    {
        _db = db;
        _mapper = mapper;
        _logger = logger;
    }

    // ── Consultas ─────────────────────────────────────────────

    public async Task<IEnumerable<MeasurementDto>> GetByExperimentAsync(int experimentId)
    {
        var measurements = await _db.Measurements
            .Where(m => m.ExperimentId == experimentId)
            .OrderBy(m => m.Timestamp)
            .AsNoTracking()
            .ToListAsync();

        return _mapper.Map<IEnumerable<MeasurementDto>>(measurements);
    }

    // ── Registro manual ───────────────────────────────────────

    public async Task<MeasurementDto> RecordAsync(CreateMeasurementRequest request)
    {
        var experiment = await _db.Experiments
            .Include(x => x.Machine)
            .FirstOrDefaultAsync(x => x.Id == request.ExperimentId);

        if (experiment is null)
            throw new InvalidOperationException($"Experimento {request.ExperimentId} no encontrado.");

        if (experiment.Status != ExperimentStatus.InProgress)
            throw new InvalidOperationException(
                "Solo se pueden registrar mediciones en experimentos en progreso.");

        var measurement = await BuildMeasurementAsync(
            request.ExperimentId,
            request.Temperature,
            request.FlowRate,
            request.Pressure,
            experiment.TargetFlowRate
        );

        _db.Measurements.Add(measurement);
        await _db.SaveChangesAsync();

        return _mapper.Map<MeasurementDto>(measurement);
    }

    // ── Registro desde sensor MQTT ────────────────────────────

    public async Task<MeasurementDto> RecordFromSensorAsync(SensorPayload payload)
    {
        // Buscar el experimento activo en esa máquina
        var experiment = await _db.Experiments
            .Include(x => x.Machine)
            .FirstOrDefaultAsync(x =>
                x.MachineId == payload.MachineId &&
                x.Status == ExperimentStatus.InProgress);

        if (experiment is null)
        {
            _logger.LogDebug(
                "Medición recibida de máquina {MachineId} sin experimento activo", payload.MachineId);
            // Retornar un DTO vacío; la medición no se guarda si no hay experimento activo
            return new MeasurementDto(0, payload.Timestamp, payload.Temperature,
                payload.FlowRate, 0, 0, payload.Pressure, null, null, 0, 0);
        }

        var measurement = await BuildMeasurementAsync(
            experiment.Id,
            payload.Temperature,
            payload.FlowRate,
            payload.Pressure,
            experiment.TargetFlowRate,
            payload.Timestamp
        );

        _db.Measurements.Add(measurement);
        await _db.SaveChangesAsync();

        _logger.LogDebug(
            "Medición guardada — Exp {ExperimentId} | T={Temp}°C | Q={Flow}L/min",
            experiment.Id, payload.Temperature, payload.FlowRate);

        return _mapper.Map<MeasurementDto>(measurement);
    }

    // ── Gráfico de control X̄-R ───────────────────────────────

    public async Task<ControlChartData> GetControlChartDataAsync(int experimentId, int subgroupSize = 5)
    {
        var measurements = await _db.Measurements
            .Where(m => m.ExperimentId == experimentId)
            .OrderBy(m => m.Timestamp)
            .AsNoTracking()
            .ToListAsync();

        if (measurements.Count < subgroupSize)
            return new ControlChartData(0, 0, 0, 0, 0, 0, Enumerable.Empty<SubgroupPoint>());

        // Agrupar mediciones en subgrupos de tamaño n
        var subgroups = measurements
            .Select((m, i) => new { m, index = i / subgroupSize })
            .GroupBy(x => x.index)
            .Where(g => g.Count() == subgroupSize) // Solo subgrupos completos
            .Select(g =>
            {
                var values = g.Select(x => x.m.FlowRate).ToList();
                var mean = values.Average();
                var range = values.Max() - values.Min();
                var timestamp = g.Min(x => x.m.Timestamp);
                return new { Mean = mean, Range = range, Timestamp = timestamp, Number = g.Key + 1 };
            })
            .ToList();

        if (!subgroups.Any())
            return new ControlChartData(0, 0, 0, 0, 0, 0, Enumerable.Empty<SubgroupPoint>());

        var grandMean  = subgroups.Average(s => s.Mean);
        var avgRange   = subgroups.Average(s => s.Range);

        var clampedSize = Math.Clamp(subgroupSize, 2, 10);
        var (a2, d3, d4) = ControlConstants[clampedSize];

        var ucl  = grandMean + a2 * avgRange;
        var lcl  = Math.Max(0, grandMean - a2 * avgRange); // LCL no puede ser negativo para caudal
        var uclR = d4 * avgRange;
        var lclR = d3 * avgRange;

        var points = subgroups.Select(s => new SubgroupPoint(
            SubgroupNumber: s.Number,
            Mean: Math.Round(s.Mean, 4),
            Range: Math.Round(s.Range, 4),
            Timestamp: s.Timestamp
        ));

        return new ControlChartData(
            GrandMean:              Math.Round(grandMean, 4),
            UpperControlLimit:      Math.Round(ucl, 4),
            LowerControlLimit:      Math.Round(lcl, 4),
            AverageRange:           Math.Round(avgRange, 4),
            UpperRangeLimit:        Math.Round(uclR, 4),
            LowerRangeLimit:        Math.Round(lclR, 4),
            Subgroups:              points
        );
    }

    // ── Helpers privados ──────────────────────────────────────

    private async Task<Measurement> BuildMeasurementAsync(
        int experimentId,
        double temperature,
        double flowRate,
        double? pressure,
        double targetFlowRate,
        DateTime? timestamp = null)
    {
        // Calcular desviación de caudal
        var deviation = targetFlowRate > 0
            ? Math.Abs(flowRate - targetFlowRate) / targetFlowRate
            : 0.0;

        // Determinar número de subgrupo actual (contar mediciones previas)
        var count = await _db.Measurements
            .CountAsync(m => m.ExperimentId == experimentId);

        var subgroupSize = 5; // Tamaño de subgrupo predeterminado
        var subgroupNumber = (count / subgroupSize) + 1;

        // Calcular media y rango del subgrupo actual (si el subgrupo está completo)
        double? subgroupMean = null;
        double? subgroupRange = null;

        var currentSubgroupStart = (subgroupNumber - 1) * subgroupSize;
        var subgroupMeasurements = await _db.Measurements
            .Where(m => m.ExperimentId == experimentId)
            .OrderBy(m => m.Timestamp)
            .Skip(currentSubgroupStart)
            .Take(subgroupSize - 1)
            .Select(m => m.FlowRate)
            .ToListAsync();

        // Agregar la medición actual al cálculo
        var allInSubgroup = subgroupMeasurements.Append(flowRate).ToList();

        if (allInSubgroup.Count == subgroupSize)
        {
            subgroupMean  = allInSubgroup.Average();
            subgroupRange = allInSubgroup.Max() - allInSubgroup.Min();
        }

        return new Measurement
        {
            ExperimentId    = experimentId,
            Timestamp       = timestamp ?? DateTime.UtcNow,
            Temperature     = Math.Round(temperature, 2),
            FlowRate        = Math.Round(flowRate, 3),
            FlowRateTarget  = Math.Round(targetFlowRate, 3),
            FlowDeviation   = Math.Round(deviation, 4),
            Pressure        = pressure.HasValue ? Math.Round(pressure.Value, 2) : null,
            SubgroupNumber  = subgroupNumber,
            SubgroupMean    = subgroupMean.HasValue ? Math.Round(subgroupMean.Value, 4) : null,
            SubgroupRange   = subgroupRange.HasValue ? Math.Round(subgroupRange.Value, 4) : null
        };
    }
}
