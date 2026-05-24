// ============================================================
//  SIGNALR HUB — Tiempo real
//  Archivo: Hubs/CavitationHub.cs
//
//  Grupos manejados:
//    machine-{machineId}   → todos los clientes viendo esa máquina
//    operator-{operatorId} → todos los dispositivos de un operario
//
//  Métodos invocables desde el cliente Flutter:
//    JoinMachineGroup(machineId)
//    LeaveMachineGroup(machineId)
//    JoinOperatorGroup()          (usa el operatorId del JWT)
//    AcknowledgeAlert(alertId)
//
//  Eventos emitidos hacia el cliente:
//    ReceiveMeasurement(RealtimeMeasurementEvent)
//    ReceiveAlert(AlertDto)
//    MachineStatusChanged(machineId, status)
//    ExperimentStatusChanged(experimentId, status)
// ============================================================

using CavitationApi.DTOs;
using CavitationApi.Helpers;
using CavitationApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CavitationApi.Hubs;

[Authorize]
public class CavitationHub : Hub
{
    private readonly IAlertService _alertService;
    private readonly IMachineService _machineService;
    private readonly ILogger<CavitationHub> _logger;

    public CavitationHub(
        IAlertService alertService,
        IMachineService machineService,
        ILogger<CavitationHub> logger)
    {
        _alertService   = alertService;
        _machineService = machineService;
        _logger         = logger;
    }

    // ── Ciclo de vida ─────────────────────────────────────────

    public override async Task OnConnectedAsync()
    {
        var operatorId = ClaimsHelper.GetOperatorId(Context.User!);

        // Unirse automáticamente al grupo del operario al conectar
        await Groups.AddToGroupAsync(Context.ConnectionId, OperatorGroup(operatorId));

        // Unirse a los grupos de todas las máquinas asignadas
        var machines = await _machineService.GetByOperatorAsync(operatorId);
        foreach (var machine in machines)
            await Groups.AddToGroupAsync(Context.ConnectionId, MachineGroup(machine.Id));

        _logger.LogInformation(
            "Operario {OperatorId} conectado a SignalR — ConnectionId: {ConnectionId}",
            operatorId, Context.ConnectionId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var operatorId = ClaimsHelper.GetOperatorId(Context.User!);

        _logger.LogInformation(
            "Operario {OperatorId} desconectado de SignalR — ConnectionId: {ConnectionId}",
            operatorId, Context.ConnectionId);

        if (exception is not null)
            _logger.LogWarning(exception, "Desconexión con error del operario {OperatorId}", operatorId);

        await base.OnDisconnectedAsync(exception);
    }

    // ── Métodos invocables desde Flutter ─────────────────────

    /// <summary>Suscribirse a las actualizaciones de una máquina específica.</summary>
    public async Task JoinMachineGroup(int machineId)
    {
        var operatorId = ClaimsHelper.GetOperatorId(Context.User!);

        // Verificar que el operario tiene esa máquina asignada
        var machines = await _machineService.GetByOperatorAsync(operatorId);
        if (!machines.Any(m => m.Id == machineId))
        {
            _logger.LogWarning(
                "Operario {OperatorId} intentó suscribirse a máquina {MachineId} sin permiso",
                operatorId, machineId);
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, MachineGroup(machineId));

        _logger.LogDebug(
            "Operario {OperatorId} suscrito al grupo machine-{MachineId}", operatorId, machineId);
    }

    /// <summary>Desuscribirse de las actualizaciones de una máquina.</summary>
    public async Task LeaveMachineGroup(int machineId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, MachineGroup(machineId));
    }

    /// <summary>Suscribirse al grupo personal del operario (para alertas).</summary>
    public async Task JoinOperatorGroup()
    {
        var operatorId = ClaimsHelper.GetOperatorId(Context.User!);
        await Groups.AddToGroupAsync(Context.ConnectionId, OperatorGroup(operatorId));
    }

    /// <summary>Reconocer una alerta directamente desde SignalR.</summary>
    public async Task AcknowledgeAlert(int alertId)
    {
        var operatorId = ClaimsHelper.GetOperatorId(Context.User!);

        try
        {
            await _alertService.AcknowledgeAsync(alertId, operatorId);

            // Notificar a todos los dispositivos del operario que la alerta fue reconocida
            await Clients
                .Group(OperatorGroup(operatorId))
                .SendAsync("AlertAcknowledged", alertId);

            _logger.LogInformation(
                "Alerta {AlertId} reconocida vía SignalR por operario {OperatorId}",
                alertId, operatorId);
        }
        catch (UnauthorizedAccessException)
        {
            await Clients.Caller.SendAsync("Error",
                "No tienes permiso para reconocer esta alerta.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al reconocer alerta {AlertId}", alertId);
            await Clients.Caller.SendAsync("Error", "Error al reconocer la alerta.");
        }
    }

    // ── Helpers de nombre de grupo ────────────────────────────

    public static string MachineGroup(int machineId)   => $"machine-{machineId}";
    public static string OperatorGroup(int operatorId) => $"operator-{operatorId}";
}
