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
    
    // ── Carta de individuales (X-MR) ─────────────────────────────

    public async Task<IndividualChartData> GetIndividualChartDataAsync(int experimentId)
    {
        var measurements = await _db.Measurements
            .Where(m => m.ExperimentId == experimentId)
            .OrderBy(m => m.Timestamp)
            .AsNoTracking()
            .ToListAsync();

        if (measurements.Count < 2)
            return new IndividualChartData(0, 0, 0, 0, 0,
                Enumerable.Empty<IndividualPoint>());

        var values = measurements.Select(m => m.FlowRate).ToList();

        // Rangos móviles entre observaciones consecutivas
        var movingRanges = new List<double>();
        for (int i = 1; i < values.Count; i++)
            movingRanges.Add(Math.Abs(values[i] - values[i - 1]));

        var grandMean = values.Average();
        var avgMR     = movingRanges.Average();

        // d2 = 1.128 para subgrupos de n=2 (constante estándar SPC)
        const double d2 = 1.128;
        // D4 = 3.267 para n=2
        const double d4MR = 3.267;

        var ucl   = grandMean + 3.0 * (avgMR / d2);
        var lcl   = Math.Max(0, grandMean - 3.0 * (avgMR / d2));
        var uclMR = d4MR * avgMR;

        var points = measurements.Select((m, i) =>
        {
            double? mr = i == 0 ? null : Math.Abs(values[i] - values[i - 1]);
            return new IndividualPoint(
                Index:       i + 1,
                Value:       Math.Round(m.FlowRate, 4),
                MovingRange: mr.HasValue ? Math.Round(mr.Value, 4) : null,
                Timestamp:   m.Timestamp,
                AboveUCL:    m.FlowRate > ucl,
                BelowLCL:    m.FlowRate < lcl,
                MRAboveUCL:  mr.HasValue && mr.Value > uclMR
            );
        });

        return new IndividualChartData(
            GrandMean:            Math.Round(grandMean, 4),
            UpperControlLimit:    Math.Round(ucl, 4),
            LowerControlLimit:    Math.Round(lcl, 4),
            AverageMovingRange:   Math.Round(avgMR, 4),
            UpperRangeLimitMR:    Math.Round(uclMR, 4),
            Points:               points
        );
    }

    // ── Carta entre experimentos ──────────────────────────────────

    public async Task<CrossExperimentChartData> GetCrossExperimentChartDataAsync(
        int? machineId = null,
        int? operatorId = null,
        int maxExperiments = 25)
    {
        // Tomar los últimos N experimentos completados
        var query = _db.Experiments
            .Where(x => x.Status == ExperimentStatus.Completed)
            .AsQueryable();

        if (machineId.HasValue)
            query = query.Where(x => x.MachineId == machineId.Value);

        if (operatorId.HasValue)
            query = query.Where(x => x.OperatorId == operatorId.Value);

        var experiments = await query
            .OrderByDescending(x => x.StartTime)
            .Take(maxExperiments)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.StartTime
            })
            .ToListAsync();

        // Invertir para orden cronológico
        experiments = experiments.AsEnumerable().Reverse().ToList();

        if (experiments.Count < 2)
            return new CrossExperimentChartData(0, 0, 0, 0, 0,
                Enumerable.Empty<CrossExperimentPoint>());

        // Calcular caudal promedio de cada experimento
        var expAverages = new List<(int Id, string Name, DateTime Start, double AvgFlow)>();

        foreach (var exp in experiments)
        {
            var avg = await _db.Measurements
                .Where(m => m.ExperimentId == exp.Id)
                .AverageAsync(m => (double?)m.FlowRate) ?? 0.0;

            expAverages.Add((exp.Id, exp.Name, exp.StartTime, avg));
        }

        var values = expAverages.Select(e => e.AvgFlow).ToList();

        // Rangos móviles entre experimentos consecutivos
        var movingRanges = new List<double>();
        for (int i = 1; i < values.Count; i++)
            movingRanges.Add(Math.Abs(values[i] - values[i - 1]));

        var grandMean = values.Average();
        var avgMR     = movingRanges.Average();

        const double d2   = 1.128;
        const double d4MR = 3.267;

        var ucl   = grandMean + 3.0 * (avgMR / d2);
        var lcl   = Math.Max(0, grandMean - 3.0 * (avgMR / d2));
        var uclMR = d4MR * avgMR;

        var points = expAverages.Select((e, i) =>
        {
            double? mr = i == 0 ? null : Math.Abs(values[i] - values[i - 1]);
            return new CrossExperimentPoint(
                ExperimentId:    e.Id,
                ExperimentName:  e.Name,
                StartTime:       e.Start,
                AverageFlowRate: Math.Round(e.AvgFlow, 4),
                MovingRange:     mr.HasValue ? Math.Round(mr.Value, 4) : null,
                AboveUCL:        e.AvgFlow > ucl,
                BelowLCL:        e.AvgFlow < lcl
            );
        });

        return new CrossExperimentChartData(
            GrandMean:          Math.Round(grandMean, 4),
            UpperControlLimit:  Math.Round(ucl, 4),
            LowerControlLimit:  Math.Round(lcl, 4),
            AverageMovingRange: Math.Round(avgMR, 4),
            UpperRangeLimitMR:  Math.Round(uclMR, 4),
            Points:             points
        );
    }
}
