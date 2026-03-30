namespace bridge_service;

public interface IMqttSubscriber
{
    event Func<string, string, Task>? MessageReceived;

    Task InitializeAsync(CancellationToken cancellationToken);
    Task ConnectAsync(CancellationToken cancellationToken);
    Task PublishAsync(string topic, string payload, CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
    Task ReconnectAsync(CancellationToken cancellationToken);
}
