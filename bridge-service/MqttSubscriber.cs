using System.Buffers;
using System.Text;
using Microsoft.Extensions.Options;
using MQTTnet;

namespace bridge_service;

public class MqttSubscriber : IMqttSubscriber
{
    private readonly ILogger<MqttSubscriber> _logger;
    private readonly MqttSettings _settings;
    private IMqttClient? _client;
    private MqttClientOptions? _options;
    private readonly SemaphoreSlim _reconnectLock = new(1, 1);
    private int _reconnectAttempts;
    private bool _isManualDisconnect;

    public event Func<string, string, Task>? MessageReceived;

    public MqttSubscriber(ILogger<MqttSubscriber> logger, IOptions<MqttSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        var factory = new MqttClientFactory();
        _client = factory.CreateMqttClient();

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithTcpServer(_settings.BrokerHost, _settings.BrokerPort)
            .WithClientId(_settings.ClientId);

        if (!string.IsNullOrWhiteSpace(_settings.Username))
        {
            optionsBuilder.WithCredentials(_settings.Username, _settings.Password);
        }

        _options = optionsBuilder.Build();
        RegisterHandlers(cancellationToken);

        _logger.LogInformation("MQTT subscriber initialized. Broker: {Host}:{Port}", _settings.BrokerHost, _settings.BrokerPort);
        return Task.CompletedTask;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_client is null || _options is null)
        {
            throw new InvalidOperationException("MQTT subscriber is not initialized.");
        }

        if (_client.IsConnected)
        {
            _logger.LogDebug("MQTT client already connected. Skip connect.");
            return;
        }

        _isManualDisconnect = false;

        var connectResult = await _client.ConnectAsync(_options, cancellationToken);
        if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
        {
            throw new InvalidOperationException($"MQTT connect failed: {connectResult.ResultCode}");
        }

        await SubscribeAsync(cancellationToken);
        _logger.LogInformation("MQTT subscriber connected and subscribed to {TopicPattern}", _settings.TopicPattern);
    }

    public async Task PublishAsync(string topic, string payload, CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            throw new InvalidOperationException("MQTT subscriber is not initialized.");
        }

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .Build();

        await _client.PublishAsync(message, cancellationToken);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        _isManualDisconnect = true;

        if (_client is null)
        {
            return;
        }

        if (_client.IsConnected)
        {
            var disconnectOptions = new MqttClientDisconnectOptionsBuilder()
                .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                .Build();

            await _client.DisconnectAsync(disconnectOptions, cancellationToken);
        }

        _logger.LogInformation("MQTT subscriber disconnected.");
    }

    public async Task ReconnectAsync(CancellationToken cancellationToken)
    {
        if (!_settings.AutoReconnect)
        {
            return;
        }

        if (_client is null)
        {
            _logger.LogWarning("MQTT client is null. Skip reconnect.");
            return;
        }

        if (_client.IsConnected)
        {
            _logger.LogDebug("MQTT client already connected. Skip reconnect.");
            return;
        }

        await _reconnectLock.WaitAsync(cancellationToken);
        try
        {
            // 避免多個斷線事件排隊進來後重複重連
            if (_client.IsConnected)
            {
                _logger.LogDebug("MQTT client already connected after acquiring reconnect lock. Skip reconnect.");
                return;
            }

            while (!cancellationToken.IsCancellationRequested && !_isManualDisconnect)
            {
                if (_client.IsConnected)
                {
                    _logger.LogInformation("MQTT client reconnected. Stop reconnect loop.");
                    _reconnectAttempts = 0;
                    return;
                }

                _reconnectAttempts++;

                if (_settings.MaxReconnectAttempts > 0 && _reconnectAttempts > _settings.MaxReconnectAttempts)
                {
                    _logger.LogError("MQTT max reconnect attempts reached: {MaxAttempts}", _settings.MaxReconnectAttempts);
                    return;
                }

                try
                {
                    _logger.LogWarning("MQTT reconnect attempt {Attempt}", _reconnectAttempts);
                    await Task.Delay(TimeSpan.FromSeconds(_settings.ReconnectDelaySeconds), cancellationToken);
                    await ConnectAsync(cancellationToken);
                    _reconnectAttempts = 0;
                    _logger.LogInformation("MQTT reconnect succeeded.");
                    return;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("MQTT reconnect cancelled.");
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MQTT reconnect attempt {Attempt} failed", _reconnectAttempts);
                }
            }
        }
        finally
        {
            _reconnectLock.Release();
        }
    }

    private void RegisterHandlers(CancellationToken serviceToken)
    {
        if (_client is null)
        {
            return;
        }

        _client.ApplicationMessageReceivedAsync += async e =>
        {
            try
            {
                var topic = e.ApplicationMessage.Topic;
                var payloadSequence = e.ApplicationMessage.Payload;
                var bytes = new byte[payloadSequence.Length];
                payloadSequence.CopyTo(bytes);
                var payload = Encoding.UTF8.GetString(bytes);

                if (MessageReceived is not null)
                {
                    await MessageReceived.Invoke(topic, payload);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling MQTT message.");
            }
        };

        _client.DisconnectedAsync += async _ =>
        {
            if (_isManualDisconnect || serviceToken.IsCancellationRequested)
            {
                return;
            }

            _logger.LogWarning("MQTT disconnected unexpectedly. Starting reconnect flow.");
            await ReconnectAsync(serviceToken);
        };

        _client.ConnectedAsync += _ =>
        {
            _logger.LogInformation("MQTT connected.");
            return Task.CompletedTask;
        };
    }

    private async Task SubscribeAsync(CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            throw new InvalidOperationException("MQTT subscriber is not initialized.");
        }

        var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(_settings.TopicPattern)
            .Build();

        await _client.SubscribeAsync(subscribeOptions, cancellationToken);
    }
}
