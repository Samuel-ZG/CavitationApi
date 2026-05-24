// ============================================================
//  EXPERIMENT SERVICE
//  Archivo: Services/ExperimentService.cs
// ============================================================

using AutoMapper;
using CavitationApi.Data;
using CavitationApi.DTOs;
using CavitationApi.Models;
using Microsoft.EntityFrameworkCore;

namespace CavitationApi.Services;

public class ExperimentService : IExperimentService
{
    private readonly AppDbContext _db;
    private readonly IMapper _mapper;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<ExperimentService> _logger;

    public ExperimentService(
        AppDbContext db,
        IMapper mapper,
        IFileStorageService fileStorage,
        ILogger<ExperimentService> logger)
    {
        _db = db;
        _mapper = mapper;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    // ── Consultas ─────────────────────────────────────────────

    public async Task<PagedResult<ExperimentSummaryDto>> GetPagedAsync(
        int page, int pageSize, int? operatorId = null, int? machineId = null)
    {
        var query = _db.Experiments
            .Include(x => x.Machine)
            .Include(x => x.Operator)
            .AsNoTracking()
            .AsQueryable();

        if (operatorId.HasValue)
            query = query.Where(x => x.OperatorId == operatorId.Value);

        if (machineId.HasValue)
            query = query.Where(x => x.MachineId == machineId.Value);

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<ExperimentSummaryDto>(
            Items: _mapper.Map<IEnumerable<ExperimentSummaryDto>>(items),
            TotalCount: total,
            Page: page,
            PageSize: pageSize
        );
    }

    public async Task<ExperimentDto?> GetByIdAsync(int id)
    {
        var experiment = await _db.Experiments
            .Include(x => x.Machine)
            .Include(x => x.Operator)
            .Include(x => x.PrecedentExperiment)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);

        return experiment is null ? null : _mapper.Map<ExperimentDto>(experiment);
    }

    public async Task<IEnumerable<ExperimentSummaryDto>> GetByMachineAsync(int machineId)
    {
        var experiments = await _db.Experiments
            .Include(x => x.Machine)
            .Include(x => x.Operator)
            .Where(x => x.MachineId == machineId)
            .OrderByDescending(x => x.CreatedAt)
            .AsNoTracking()
            .ToListAsync();

        return _mapper.Map<IEnumerable<ExperimentSummaryDto>>(experiments);
    }

    // ── Crear ─────────────────────────────────────────────────

    public async Task<ExperimentDto> CreateAsync(CreateExperimentRequest request, int operatorId)
    {
        // Verificar que la máquina existe y está disponible
        var machine = await _db.Machines.FindAsync(request.MachineId);
        if (machine is null)
            throw new InvalidOperationException($"Máquina {request.MachineId} no encontrada.");

        if (machine.Status == MachineStatus.Error || machine.Status == MachineStatus.EmergencyOff)
            throw new InvalidOperationException(
                $"La máquina está en estado {machine.Status} y no puede usarse.");

        // Verificar que el operario tiene la máquina asignada
        if (machine.AssignedOperatorId != operatorId)
            throw new UnauthorizedAccessException(
                "No tienes permiso para usar esta máquina.");

        // Verificar antecedente si aplica
        if (request.HasPrecedent && request.PrecedentExperimentId.HasValue)
        {
            var precedent = await _db.Experiments
                .FirstOrDefaultAsync(x => x.Id == request.PrecedentExperimentId.Value
                                       && x.Status == ExperimentStatus.Completed);
            if (precedent is null)
                throw new InvalidOperationException(
                    "El experimento antecedente no existe o no está completado.");
        }

        var experiment = _mapper.Map<Experiment>(request);
        experiment.OperatorId = operatorId;
        experiment.CreatedAt = DateTime.UtcNow;

        _db.Experiments.Add(experiment);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Experimento '{Name}' creado por operario {OperatorId} en máquina {MachineId}",
            experiment.Name, operatorId, request.MachineId);

        await _db.Entry(experiment).Reference(x => x.Machine).LoadAsync();
        await _db.Entry(experiment).Reference(x => x.Operator).LoadAsync();

        return _mapper.Map<ExperimentDto>(experiment);
    }

    // ── Actualizar ────────────────────────────────────────────

    public async Task<ExperimentDto?> UpdateAsync(int id, UpdateExperimentRequest request)
    {
        var experiment = await _db.Experiments
            .Include(x => x.Machine)
            .Include(x => x.Operator)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (experiment is null) return null;

        // Solo se puede editar si está Pending
        if (experiment.Status != ExperimentStatus.Pending)
            throw new InvalidOperationException(
                "Solo se puede editar un experimento en estado Pending.");

        experiment.Name = request.Name;
        experiment.SampleDescription = request.SampleDescription;
        experiment.TargetFlowRate = request.TargetFlowRate;
        experiment.StartTime = request.StartTime;
        experiment.PlannedDuration = request.PlannedDuration;

        await _db.SaveChangesAsync();
        return _mapper.Map<ExperimentDto>(experiment);
    }

    // ── Estado ────────────────────────────────────────────────

    public async Task<bool> UpdateStatusAsync(int id, UpdateExperimentStatusRequest request)
    {
        var experiment = await _db.Experiments
            .Include(x => x.Machine)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (experiment is null) return false;

        // Validar transiciones de estado permitidas
        var allowed = (experiment.Status, request.Status) switch
        {
            (ExperimentStatus.Pending, ExperimentStatus.InProgress)   => true,
            (ExperimentStatus.InProgress, ExperimentStatus.Paused)    => true,
            (ExperimentStatus.Paused, ExperimentStatus.InProgress)    => true,
            (ExperimentStatus.InProgress, ExperimentStatus.Completed) => true,
            (ExperimentStatus.InProgress, ExperimentStatus.Aborted)   => true,
            (ExperimentStatus.Paused, ExperimentStatus.Aborted)       => true,
            _ => false
        };

        if (!allowed)
            throw new InvalidOperationException(
                $"Transición de estado no permitida: {experiment.Status} → {request.Status}");

        experiment.Status = request.Status;

        if (request.Status == ExperimentStatus.InProgress && experiment.StartTime == default)
            experiment.StartTime = DateTime.UtcNow;

        if (request.Status is ExperimentStatus.Completed or ExperimentStatus.Aborted)
        {
            experiment.EndTime = DateTime.UtcNow;
            experiment.AbortReason = request.AbortReason;

            // Liberar la máquina si el experimento termina
            if (experiment.Machine.Status == MachineStatus.InUse)
            {
                experiment.Machine.Status = request.Status == ExperimentStatus.Aborted
                    ? MachineStatus.EmergencyOff
                    : MachineStatus.Available;
                experiment.Machine.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Experimento {Id} cambió estado a {Status}", id, request.Status);

        return true;
    }

    // ── Imágenes de microscopio ───────────────────────────────

    public async Task<bool> SetMicroscopeImageAsync(int id, string imagePath, bool isBefore)
    {
        var experiment = await _db.Experiments.FindAsync(id);
        if (experiment is null) return false;

        if (isBefore)
        {
            // Eliminar imagen anterior si existe
            if (!string.IsNullOrEmpty(experiment.MicroscopeImageBeforePath))
                await _fileStorage.DeleteAsync(experiment.MicroscopeImageBeforePath);

            experiment.MicroscopeImageBeforePath = imagePath;
        }
        else
        {
            if (!string.IsNullOrEmpty(experiment.MicroscopeImageAfterPath))
                await _fileStorage.DeleteAsync(experiment.MicroscopeImageAfterPath);

            experiment.MicroscopeImageAfterPath = imagePath;
        }

        await _db.SaveChangesAsync();
        return true;
    }

    // ── Eliminar ──────────────────────────────────────────────

    public async Task<bool> DeleteAsync(int id)
    {
        var experiment = await _db.Experiments.FindAsync(id);
        if (experiment is null) return false;

        if (experiment.Status == ExperimentStatus.InProgress)
            throw new InvalidOperationException(
                "No se puede eliminar un experimento en progreso.");

        // Limpiar imágenes del disco
        if (!string.IsNullOrEmpty(experiment.MicroscopeImageBeforePath))
            await _fileStorage.DeleteAsync(experiment.MicroscopeImageBeforePath);

        if (!string.IsNullOrEmpty(experiment.MicroscopeImageAfterPath))
            await _fileStorage.DeleteAsync(experiment.MicroscopeImageAfterPath);

        _db.Experiments.Remove(experiment);
        await _db.SaveChangesAsync();
        return true;
    }
}
