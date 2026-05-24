// ============================================================
//  MQTT CLIENT SERVICE — Singleton
//  Archivo: Services/MqttClientService.cs
//
//  Maneja la conexión al broker MQTT local (Mosquitto).
//  Se registra como Singleton para que los BackgroundServices
//  y el MachineService compartan la misma conexión.
//
//  TODO NUBE: Para AWS IoT Core cambiar el puerto a 8883,
//  habilitar TLS y usar certificados X.509 del dispositivo.
//  Para Azure IoT Hub usar el adaptador MQTT con SAS token.
// ============================================================

using CavitationApi.Services;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace CavitationApi.Services;

public class MqttClientService : IMqttClientService, IAsyncDisposable
{
    private readonly IConfiguration _config;
    private readonly ILogger<MqttClientService> _logger;

    private IMqttClient? _client;
    private MqttClientOptions? _options;

    // Evento que dispara el BackgroundService al recibir un mensaje
    public event Func<string, string, Task>? MessageReceived;

    public bool IsConnected => _client?.IsConnected ?? false;

    public MqttClientService(IConfiguration config, ILogger<MqttClientService> logger)
    {
        _config = config;
        _logger = logger;
    }

    // ── Arranque ──────────────────────────────────────────────

    public async Task StartAsync()
    {
        var factory = new MqttFactory();
        _client = factory.CreateMqttClient();

        // TODO NUBE: Para AWS IoT Core / Azure IoT Hub:
        //   - Cambiar puerto a 8883
        //   - Agregar .WithTls() con certificado del dispositivo
        //   - Cambiar ClientId por el device ID registrado en el Hub
        var host     = _config["Mqtt:Host"]     ?? "localhost";
        var port     = int.Parse(_config["Mqtt:Port"] ?? "1883");
        var clientId = _config["Mqtt:ClientId"] ?? "CavitationApiServer";
        var username = _config["Mqtt:Username"] ?? string.Empty;
        var password = _config["Mqtt:Password"] ?? string.Empty;

        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(host, port)
            .WithClientId(clientId)
            .WithCleanSession()
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30));

        if (!string.IsNullOrEmpty(username))
            builder = builder.WithCredentials(username, password);

        _options = builder.Build();

        // Callback al recibir mensaje
        _client.ApplicationMessageReceivedAsync += async e =>
        {
            var topic   = e.ApplicationMessage.Topic;
            var payload = System.Text.Encoding.UTF8.GetString(
                e.ApplicationMessage.PayloadSegment);

            _logger.LogDebug("MQTT recibido — Topic: {Topic}", topic);

            if (MessageReceived is not null)
                await MessageReceived.Invoke(topic, payload);
        };

        // Reconexión automática al desconectarse
        _client.DisconnectedAsync += async e =>
        {
            _logger.LogWarning("MQTT desconectado. Reintentando en 5s...");
            await Task.Delay(TimeSpan.FromSeconds(5));

            try
            {
                await _client.ConnectAsync(_options, CancellationToken.None);
                await SubscribeToRootTopicAsync();
                _logger.LogInformation("MQTT reconectado.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al reconectar al broker MQTT.");
            }
        };

        try
        {
            await _client.ConnectAsync(_options, CancellationToken.None);
            await SubscribeToRootTopicAsync();
            _logger.LogInformation("MQTT conectado a {Host}:{Port}", host, port);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo conectar al broker MQTT en {Host}:{Port}. " +
                "Verifique que Mosquitto está corriendo localmente.", host, port);
            // No lanzar excepción: el sistema arranca aunque el broker no esté disponible
        }
    }

    // ── Parada ────────────────────────────────────────────────

    public async Task StopAsync()
    {
        if (_client?.IsConnected == true)
        {
            await _client.DisconnectAsync();
            _logger.LogInformation("MQTT desconectado limpiamente.");
        }
    }

    // ── Publicar ──────────────────────────────────────────────

    public async Task PublishAsync(string topic, string payload)
    {
        if (_client is null || !_client.IsConnected)
        {
            _logger.LogWarning("Intento de publicar sin conexión MQTT — Topic: {Topic}", topic);
            return;
        }

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(false)
            .Build();

        await _client.PublishAsync(message, CancellationToken.None);
        _logger.LogDebug("MQTT publicado — Topic: {Topic}", topic);
    }

    // ── Suscripción ───────────────────────────────────────────

    private async Task SubscribeToRootTopicAsync()
    {
        if (_client is null) return;

        // TODO NUBE: El tópico raíz cambiará según el broker de nube
        // AWS IoT Core usa: $aws/things/{thingName}/shadow/update
        // Azure IoT Hub usa: devices/{deviceId}/messages/events/#
        var rootTopic = _config["Mqtt:RootTopic"] ?? "cavitation/#";

        var topicFilter = new MqttTopicFilterBuilder()
            .WithTopic(rootTopic)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        await _client.SubscribeAsync(topicFilter);
        _logger.LogInformation("MQTT suscrito a: {Topic}", rootTopic);
    }

    // ── Dispose ───────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _client?.Dispose();
    }
}
