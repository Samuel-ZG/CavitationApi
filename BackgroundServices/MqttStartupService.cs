// ============================================================
//  MQTT STARTUP SERVICE
//  Archivo: BackgroundServices/MqttStartupService.cs
//
//  Servicio de hosted que arranca el MqttClientService
//  al iniciarse el servidor ASP.NET Core.
//  Separado del MqttBackgroundService para que la conexión
//  esté lista antes de que lleguen los primeros mensajes.
//
//  Registrar en Program.cs:
//  builder.Services.AddHostedService<MqttStartupService>();
// ============================================================

using CavitationApi.Services;

namespace CavitationApi.BackgroundServices;

public class MqttStartupService : IHostedService
{
    private readonly IMqttClientService _mqttClientService;
    private readonly ILogger<MqttStartupService> _logger;

    public MqttStartupService(
        IMqttClientService mqttClientService,
        ILogger<MqttStartupService> logger)
    {
        _mqttClientService = mqttClientService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Iniciando conexión MQTT...");
        await _mqttClientService.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cerrando conexión MQTT...");
        await _mqttClientService.StopAsync();
    }
}
