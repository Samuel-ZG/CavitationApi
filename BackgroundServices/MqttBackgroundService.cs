// ============================================================
//  MQTT BACKGROUND SERVICE
//  Archivo: BackgroundServices/MqttBackgroundService.cs
//
//  Flujo:
//  1. Se suscribe al evento MessageReceived del MqttClientService
//  2. Al llegar un payload del sensor, lo deserializa como SensorPayload
//  3. Llama a MeasurementService.RecordFromSensorAsync()
//  4. Emite el evento RealtimeMeasurementEvent a los clientes Flutter
//     vía SignalR en el grupo machine-{machineId}
//  5. Delega la evaluación de límites al EmergencyMonitorService
//     a través del canal Channel<SensorPayload>
// ============================================================

using System.Text.Json;
using System.Threading.Channels;
using CavitationApi.DTOs;
using CavitationApi.Hubs;
using CavitationApi.Services;
using Microsoft.AspNetCore.SignalR;

namespace CavitationApi.BackgroundServices;

public class MqttBackgroundService : BackgroundService
{
    private readonly IMqttClientService _mqtt;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<CavitationHub> _hub;
    private readonly ILogger<MqttBackgroundService> _logger;

    // Canal para pasar payloads al EmergencyMonitorService sin bloquear
    public static readonly Channel<SensorPayload> EmergencyQueue =
        Channel.CreateUnbounded<SensorPayload>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public MqttBackgroundService(
        IMqttClientService mqtt,
        IServiceScopeFactory scopeFactory,
        IHubContext<CavitationHub> hub,
        ILogger<MqttBackgroundService> logger)
    {
        _mqtt          = mqtt;
        _scopeFactory  = scopeFactory;
        _hub           = hub;
        _logger        = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Suscribirse al evento de mensajes MQTT
        _mqtt.MessageReceived += OnMessageReceivedAsync;

        // Mantener el servicio vivo hasta que se cancele
        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { });

        _mqtt.MessageReceived -= OnMessageReceivedAsync;
        _logger.LogInformation("MqttBackgroundService detenido.");
    }

    // ── Handler principal ─────────────────────────────────────

    private async Task OnMessageReceivedAsync(string topic, string payloadJson)
    {
        // Solo procesar tópicos de sensores: cavitation/machine/{id}/sensors
        if (!topic.Contains("/sensors")) return;

        SensorPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<SensorPayload>(payloadJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Payload MQTT inválido en tópico {Topic}: {Payload}",
                topic, payloadJson);
            return;
        }

        if (payload is null) return;

        // ── 1. Guardar medición en BD ─────────────────────────
        MeasurementDto? savedMeasurement = null;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var measurementService = scope.ServiceProvider
                .GetRequiredService<IMeasurementService>();

            savedMeasurement = await measurementService.RecordFromSensorAsync(payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error guardando medición de máquina {MachineId}", payload.MachineId);
            return;
        }

        // Si no hay experimento activo no hay nada que emitir
        if (savedMeasurement.ExperimentId == 0) return;

        // ── 2. Determinar advertencias para el evento ─────────
        double tempWarning  = 0;
        double tempCritical = 0;
        double flowTolerance = 0;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var machineService = scope.ServiceProvider.GetRequiredService<IMachineService>();
            var machine = await machineService.GetByIdAsync(payload.MachineId);
            if (machine is not null)
            {
                tempWarning  = machine.TemperatureLimitWarning;
                tempCritical = machine.TemperatureLimitCritical;
                flowTolerance = machine.FlowRateTolerance;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudieron obtener límites de máquina {MachineId}",
                payload.MachineId);
        }

        var tempWarn     = payload.Temperature >= tempWarning  && tempWarning  > 0;
        var tempCrit     = payload.Temperature >= tempCritical && tempCritical > 0;
        var flowDeviated = savedMeasurement.FlowDeviation > flowTolerance && flowTolerance > 0;

        // ── 3. Emitir a Flutter vía SignalR ───────────────────
        var realtimeEvent = new RealtimeMeasurementEvent(
            MachineId:           payload.MachineId,
            ExperimentId:        savedMeasurement.ExperimentId,
            Temperature:         payload.Temperature,
            FlowRate:            payload.FlowRate,
            FlowDeviation:       savedMeasurement.FlowDeviation,
            Pressure:            payload.Pressure,
            Timestamp:           payload.Timestamp,
            TemperatureWarning:  tempWarn,
            TemperatureCritical: tempCrit,
            FlowDeviated:        flowDeviated
        );

        await _hub.Clients
            .Group(CavitationHub.MachineGroup(payload.MachineId))
            .SendAsync("ReceiveMeasurement", realtimeEvent);

        // ── 4. Encolar para evaluación de emergencia ──────────
        await EmergencyQueue.Writer.WriteAsync(payload);

        _logger.LogDebug(
            "Medición procesada — Máquina {MachineId} | T={Temp}°C | Q={Flow}L/min",
            payload.MachineId, payload.Temperature, payload.FlowRate);
    }
}
