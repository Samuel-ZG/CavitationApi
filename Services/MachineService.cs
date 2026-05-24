// ============================================================
//  MACHINE SERVICE
//  Archivo: Services/MachineService.cs
// ============================================================

using AutoMapper;
using CavitationApi.Data;
using CavitationApi.DTOs;
using CavitationApi.Models;
using Microsoft.EntityFrameworkCore;

namespace CavitationApi.Services;

public class MachineService : IMachineService
{
    private readonly AppDbContext _db;
    private readonly IMapper _mapper;
    private readonly IMqttClientService _mqtt;
    private readonly IConfiguration _config;
    private readonly ILogger<MachineService> _logger;

    public MachineService(
        AppDbContext db,
        IMapper mapper,
        IMqttClientService mqtt,
        IConfiguration config,
        ILogger<MachineService> logger)
    {
        _db = db;
        _mapper = mapper;
        _mqtt = mqtt;
        _config = config;
        _logger = logger;
    }

    // ── Consultas ─────────────────────────────────────────────

    public async Task<IEnumerable<MachineDto>> GetAllAsync()
    {
        var machines = await _db.Machines
            .Include(m => m.AssignedOperator)
            .AsNoTracking()
            .ToListAsync();

        return _mapper.Map<IEnumerable<MachineDto>>(machines);
    }

    public async Task<IEnumerable<MachineDto>> GetByOperatorAsync(int operatorId)
    {
        var machines = await _db.Machines
            .Include(m => m.AssignedOperator)
            .Where(m => m.AssignedOperatorId == operatorId)
            .AsNoTracking()
            .ToListAsync();

        return _mapper.Map<IEnumerable<MachineDto>>(machines);
    }

    public async Task<MachineDto?> GetByIdAsync(int id)
    {
        var machine = await _db.Machines
            .Include(m => m.AssignedOperator)
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id);

        return machine is null ? null : _mapper.Map<MachineDto>(machine);
    }

    // ── Crear / Actualizar ────────────────────────────────────

    public async Task<MachineDto> CreateAsync(CreateMachineRequest request)
    {
        var machine = _mapper.Map<Machine>(request);
        _db.Machines.Add(machine);
        await _db.SaveChangesAsync();

        await _db.Entry(machine).Reference(m => m.AssignedOperator).LoadAsync();
        return _mapper.Map<MachineDto>(machine);
    }

    public async Task<MachineDto?> UpdateAsync(int id, UpdateMachineRequest request)
    {
        var machine = await _db.Machines
            .Include(m => m.AssignedOperator)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (machine is null) return null;

        _mapper.Map(request, machine);
        machine.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return _mapper.Map<MachineDto>(machine);
    }

    // ── Estado ────────────────────────────────────────────────

    public async Task<bool> UpdateStatusAsync(int id, MachineStatus status)
    {
        var machine = await _db.Machines.FindAsync(id);
        if (machine is null) return false;

        machine.Status = status;
        machine.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Máquina {Id} cambió estado a {Status}", id, status);
        return true;
    }

    // ── Comandos hacia el actuador vía MQTT ───────────────────

    public async Task<bool> SendCommandAsync(int id, string command)
    {
        var machine = await _db.Machines.FindAsync(id);
        if (machine is null) return false;

        // Tópico de comandos: cavitation/machine/{id}/commands
        // TODO NUBE: El tópico cambiará al formato del broker de nube (ej. AWS IoT Shadow)
        var topic = $"cavitation/machine/{id}/commands";

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            command,
            machineId = id,
            timestamp = DateTime.UtcNow
        });

        if (!_mqtt.IsConnected)
        {
            _logger.LogWarning("MQTT no conectado al enviar comando {Command} a máquina {Id}", command, id);
            return false;
        }

        await _mqtt.PublishAsync(topic, payload);

        // Actualizar estado local según el comando
        machine.Status = command switch
        {
            "start"          => MachineStatus.InUse,
            "stop"           => MachineStatus.Available,
            "emergency_stop" => MachineStatus.EmergencyOff,
            _                => machine.Status
        };
        machine.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Comando '{Command}' enviado a máquina {Id}", command, id);
        return true;
    }

    // ── Asignación de operario ────────────────────────────────

    public async Task<bool> AssignOperatorAsync(int machineId, int operatorId)
    {
        var machine = await _db.Machines.FindAsync(machineId);
        if (machine is null) return false;

        var operatorExists = await _db.Operators.AnyAsync(o => o.Id == operatorId && o.IsActive);
        if (!operatorExists) return false;

        machine.AssignedOperatorId = operatorId;
        machine.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    // ── Eliminar ──────────────────────────────────────────────

    public async Task<bool> DeleteAsync(int id)
    {
        var machine = await _db.Machines.FindAsync(id);
        if (machine is null) return false;

        // No eliminar si tiene experimentos activos
        var hasActive = await _db.Experiments
            .AnyAsync(e => e.MachineId == id &&
                          (e.Status == ExperimentStatus.InProgress ||
                           e.Status == ExperimentStatus.Pending));

        if (hasActive)
        {
            _logger.LogWarning("Intento de eliminar máquina {Id} con experimentos activos", id);
            return false;
        }

        _db.Machines.Remove(machine);
        await _db.SaveChangesAsync();
        return true;
    }
}
