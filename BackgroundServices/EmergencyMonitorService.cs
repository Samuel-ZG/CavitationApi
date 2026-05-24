// ============================================================
//  EMERGENCY MONITOR SERVICE
//  Archivo: BackgroundServices/EmergencyMonitorService.cs
//
//  Lee del canal Channel<SensorPayload> que llena el
//  MqttBackgroundService y evalúa cada medición contra
//  los límites configurados por máquina.
//
//  Lógica de alertas:
//    Temperatura >= TemperatureLimitWarning
//      → AlertType.Warning  → SignalR notifica al operario
//    Temperatura >= TemperatureLimitCritical
//      → AlertType.Critical → MQTT publica emergency_stop
//                           → Máquina pasa a EmergencyOff
//                           → Experimento pasa a Aborted
//                           → SignalR notifica a todos
//    Desviación de caudal > FlowRateTolerance durante N mediciones
//      → AlertType.FlowDeviation → SignalR notifica al operario
// ============================================================

using System.Text.Json;
using CavitationApi.DTOs;
using CavitationApi.Hubs;
using CavitationApi.Models;
using CavitationApi.Services;
using Microsoft.AspNetCore.SignalR;

namespace CavitationApi.BackgroundServices;

public class EmergencyMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<CavitationHub> _hub;
    private readonly IMqttClientService _mqtt;
    private readonly ILogger<EmergencyMonitorService> _logger;

    // Rastrear alertas de Warning ya emitidas para no duplicarlas por máquina
    private readonly Dictionary<int, DateTime> _lastWarningEmitted = new();

    // Contador de mediciones consecutivas con desviación de caudal por experimento
    private readonly Dictionary<int, int> _flowDeviationCount = new();

    // Umbral: N mediciones consecutivas con desviación para disparar alerta de caudal
    private const int FlowDeviationThreshold = 5;

    // Cooldown entre alertas de Warning de temperatura (no enviar cada segundo)
    private static readonly TimeSpan WarningCooldown = TimeSpan.FromSeconds(30);

    public EmergencyMonitorService(
        IServiceScopeFactory scopeFactory,
        IHubContext<CavitationHub> hub,
        IMqttClientService mqtt,
        ILogger<EmergencyMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _hub          = hub;
        _mqtt         = mqtt;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EmergencyMonitorService iniciado.");

        // Leer del canal que llena MqttBackgroundService
        await foreach (var payload in MqttBackgroundService.EmergencyQueue.Reader
            .ReadAllAsync(stoppingToken))
        {
            try
            {
                await EvaluatePayloadAsync(payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error en evaluación de emergencia para máquina {MachineId}",
                    payload.MachineId);
            }
        }

        _logger.LogInformation("EmergencyMonitorService detenido.");
    }

    // ── Evaluación principal ──────────────────────────────────

    private async Task EvaluatePayloadAsync(SensorPayload payload)
    {
        using var scope = _scopeFactory.CreateScope();
        var machineService    = scope.ServiceProvider.GetRequiredService<IMachineService>();
        var experimentService = scope.ServiceProvider.GetRequiredService<IExperimentService>();
        var alertService      = scope.ServiceProvider.GetRequiredService<IAlertService>();

        var machine = await machineService.GetByIdAsync(payload.MachineId);
        if (machine is null) return;

        // No evaluar si la máquina ya está apagada por emergencia
        if (machine.Status == MachineStatus.EmergencyOff) return;

        // Buscar el experimento activo en esa máquina
        // (la búsqueda se hace en ResultService/ExperimentService internamente)
        // Aquí necesitamos el experimentId para crear la alerta
        var experiments = await experimentService.GetByMachineAsync(payload.MachineId);
        var activeExperiment = experiments
            .FirstOrDefault(e => e.Status == ExperimentStatus.InProgress);

        if (activeExperiment is null) return;

        // ── Evaluación de temperatura ─────────────────────────

        if (payload.Temperature >= machine.TemperatureLimitCritical)
        {
            await HandleCriticalTemperatureAsync(
                payload, machine, activeExperiment.Id,
                machineService, experimentService, alertService);
            return; // Si ya es crítico, no evaluar warning
        }

        if (payload.Temperature >= machine.TemperatureLimitWarning)
        {
            await HandleWarningTemperatureAsync(
                payload, machine, activeExperiment.Id, alertService);
        }
        else
        {
            // Temperatura normal: limpiar cooldown de warning
            _lastWarningEmitted.Remove(payload.MachineId);
        }

        // ── Evaluación de desviación de caudal ────────────────

        await EvaluateFlowDeviationAsync(
            payload, machine, activeExperiment.Id, alertService);
    }

    // ── Temperatura crítica — apagado automático ──────────────

    private async Task HandleCriticalTemperatureAsync(
        SensorPayload payload,
        MachineDto machine,
        int experimentId,
        IMachineService machineService,
        IExperimentService experimentService,
        IAlertService alertService)
    {
        _logger.LogCritical(
            "TEMPERATURA CRÍTICA en máquina {MachineId}: {Temp}°C (límite={Limit}°C) — APAGADO AUTOMÁTICO",
            payload.MachineId, payload.Temperature, machine.TemperatureLimitCritical);

        // 1. Crear alerta crítica en BD
        var alert = await alertService.CreateAsync(
            experimentId:  experimentId,
            machineId:     payload.MachineId,
            type:          AlertType.Critical,
            message:       $"Temperatura crítica: {payload.Temperature:F1}°C superó el límite de {machine.TemperatureLimitCritical:F1}°C. Apagado automático activado.",
            triggerValue:  payload.Temperature,
            thresholdValue: machine.TemperatureLimitCritical,
            autoShutdown:  true
        );

        // 2. Enviar comando emergency_stop al actuador vía MQTT
        await machineService.SendCommandAsync(payload.MachineId, "emergency_stop");

        // 3. Abortar el experimento activo
        await experimentService.UpdateStatusAsync(experimentId, new UpdateExperimentStatusRequest(
            Status:      ExperimentStatus.Aborted,
            AbortReason: $"Apagado automático por temperatura crítica: {payload.Temperature:F1}°C"
        ));

        // 4. Notificar a todos los clientes del grupo de la máquina
        var emergencyEvent = new
        {
            MachineId    = payload.MachineId,
            ExperimentId = experimentId,
            Temperature  = payload.Temperature,
            Limit        = machine.TemperatureLimitCritical,
            Message      = alert.Message,
            Timestamp    = DateTime.UtcNow,
            AutoShutdown = true
        };

        await _hub.Clients
            .Group(CavitationHub.MachineGroup(payload.MachineId))
            .SendAsync("ReceiveAlert", alert);

        await _hub.Clients
            .Group(CavitationHub.MachineGroup(payload.MachineId))
            .SendAsync("MachineStatusChanged", payload.MachineId, "EmergencyOff");

        await _hub.Clients
            .Group(CavitationHub.MachineGroup(payload.MachineId))
            .SendAsync("ExperimentStatusChanged", experimentId, "Aborted");

        // Notificar también al grupo del operario (si está en otra pantalla)
        if (machine.AssignedOperatorId.HasValue)
        {
            await _hub.Clients
                .Group(CavitationHub.OperatorGroup(machine.AssignedOperatorId.Value))
                .SendAsync("ReceiveAlert", alert);
        }

        _lastWarningEmitted.Remove(payload.MachineId);
    }

    // ── Temperatura de advertencia ────────────────────────────

    private async Task HandleWarningTemperatureAsync(
        SensorPayload payload,
        MachineDto machine,
        int experimentId,
        IAlertService alertService)
    {
        // Cooldown: no emitir la misma alerta cada segundo
        if (_lastWarningEmitted.TryGetValue(payload.MachineId, out var lastEmitted)
            && DateTime.UtcNow - lastEmitted < WarningCooldown)
            return;

        _logger.LogWarning(
            "Temperatura de advertencia en máquina {MachineId}: {Temp}°C (límite={Limit}°C)",
            payload.MachineId, payload.Temperature, machine.TemperatureLimitWarning);

        var alert = await alertService.CreateAsync(
            experimentId:   experimentId,
            machineId:      payload.MachineId,
            type:           AlertType.Warning,
            message:        $"Advertencia: temperatura {payload.Temperature:F1}°C se acerca al límite crítico de {machine.TemperatureLimitCritical:F1}°C.",
            triggerValue:   payload.Temperature,
            thresholdValue: machine.TemperatureLimitWarning,
            autoShutdown:   false
        );

        // Emitir alerta al grupo de la máquina
        await _hub.Clients
            .Group(CavitationHub.MachineGroup(payload.MachineId))
            .SendAsync("ReceiveAlert", alert);

        if (machine.AssignedOperatorId.HasValue)
        {
            await _hub.Clients
                .Group(CavitationHub.OperatorGroup(machine.AssignedOperatorId.Value))
                .SendAsync("ReceiveAlert", alert);
        }

        _lastWarningEmitted[payload.MachineId] = DateTime.UtcNow;
    }

    // ── Desviación de caudal ──────────────────────────────────

    private async Task EvaluateFlowDeviationAsync(
        SensorPayload payload,
        MachineDto machine,
        int experimentId,
        IAlertService alertService)
    {
        // Calcular desviación porcentual vs caudal objetivo de la máquina
        if (machine.TargetFlowRate <= 0) return;

        var deviation = Math.Abs(payload.FlowRate - machine.TargetFlowRate)
                        / machine.TargetFlowRate;

        if (deviation > machine.FlowRateTolerance)
        {
            _flowDeviationCount[experimentId] =
                _flowDeviationCount.GetValueOrDefault(experimentId, 0) + 1;

            // Disparar alerta solo al alcanzar el umbral de mediciones consecutivas
            if (_flowDeviationCount[experimentId] == FlowDeviationThreshold)
            {
                _logger.LogWarning(
                    "Desviación de caudal en experimento {ExperimentId}: " +
                    "{Flow}L/min vs objetivo {Target}L/min ({Dev:P1} desviación)",
                    experimentId, payload.FlowRate, machine.TargetFlowRate, deviation);

                var alert = await alertService.CreateAsync(
                    experimentId:   experimentId,
                    machineId:      payload.MachineId,
                    type:           AlertType.FlowDeviation,
                    message:        $"Caudal fuera de tolerancia: {payload.FlowRate:F2}L/min " +
                                    $"(objetivo {machine.TargetFlowRate:F2}L/min, " +
                                    $"desviación {deviation:P1}).",
                    triggerValue:   payload.FlowRate,
                    thresholdValue: machine.TargetFlowRate,
                    autoShutdown:   false
                );

                await _hub.Clients
                    .Group(CavitationHub.MachineGroup(payload.MachineId))
                    .SendAsync("ReceiveAlert", alert);

                if (machine.AssignedOperatorId.HasValue)
                {
                    await _hub.Clients
                        .Group(CavitationHub.OperatorGroup(machine.AssignedOperatorId.Value))
                        .SendAsync("ReceiveAlert", alert);
                }
            }
        }
        else
        {
            // Caudal volvió a tolerancia: reiniciar contador
            _flowDeviationCount[experimentId] = 0;
        }
    }
}
