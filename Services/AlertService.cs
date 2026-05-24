// ============================================================
//  ALERT SERVICE
//  Archivo: Services/AlertService.cs
// ============================================================

using AutoMapper;
using CavitationApi.Data;
using CavitationApi.DTOs;
using CavitationApi.Models;
using Microsoft.EntityFrameworkCore;

namespace CavitationApi.Services;

public class AlertService : IAlertService
{
    private readonly AppDbContext _db;
    private readonly IMapper _mapper;
    private readonly ILogger<AlertService> _logger;

    public AlertService(
        AppDbContext db,
        IMapper mapper,
        ILogger<AlertService> logger)
    {
        _db = db;
        _mapper = mapper;
        _logger = logger;
    }

    // ── Consultas ─────────────────────────────────────────────

    public async Task<IEnumerable<AlertDto>> GetByExperimentAsync(int experimentId)
    {
        var alerts = await _db.Alerts
            .Include(a => a.Machine)
            .Where(a => a.ExperimentId == experimentId)
            .OrderByDescending(a => a.TriggeredAt)
            .AsNoTracking()
            .ToListAsync();

        return _mapper.Map<IEnumerable<AlertDto>>(alerts);
    }

    // Alertas activas (no reconocidas) de todas las máquinas del operario
    public async Task<IEnumerable<AlertDto>> GetActiveByOperatorAsync(int operatorId)
    {
        var alerts = await _db.Alerts
            .Include(a => a.Machine)
            .Include(a => a.Experiment)
            .Where(a =>
                a.Machine.AssignedOperatorId == operatorId &&
                !a.AcknowledgedByOperator)
            .OrderByDescending(a => a.TriggeredAt)
            .AsNoTracking()
            .ToListAsync();

        return _mapper.Map<IEnumerable<AlertDto>>(alerts);
    }

    // ── Crear alerta ──────────────────────────────────────────

    public async Task<AlertDto> CreateAsync(
        int experimentId,
        int machineId,
        AlertType type,
        string message,
        double triggerValue,
        double thresholdValue,
        bool autoShutdown)
    {
        var alert = new Alert
        {
            ExperimentId           = experimentId,
            MachineId              = machineId,
            Type                   = type,
            Message                = message,
            TriggerValue           = triggerValue,
            ThresholdValue         = thresholdValue,
            AutoShutdown           = autoShutdown,
            AcknowledgedByOperator = false,
            TriggeredAt            = DateTime.UtcNow
        };

        _db.Alerts.Add(alert);
        await _db.SaveChangesAsync();

        _logger.LogWarning(
            "Alerta {Type} — Máquina {MachineId}: {Message} (valor={TriggerValue}, umbral={Threshold})",
            type, machineId, message, triggerValue, thresholdValue);

        await _db.Entry(alert).Reference(a => a.Machine).LoadAsync();
        return _mapper.Map<AlertDto>(alert);
    }

    // ── Reconocer alerta ──────────────────────────────────────

    public async Task<bool> AcknowledgeAsync(int alertId, int operatorId)
    {
        var alert = await _db.Alerts
            .Include(a => a.Machine)
            .FirstOrDefaultAsync(a => a.Id == alertId);

        if (alert is null) return false;

        // Verificar que la alerta pertenece a una máquina del operario
        if (alert.Machine.AssignedOperatorId != operatorId)
            throw new UnauthorizedAccessException(
                "No tienes permiso para reconocer esta alerta.");

        alert.AcknowledgedByOperator = true;
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Alerta {AlertId} reconocida por operario {OperatorId}", alertId, operatorId);

        return true;
    }
}
