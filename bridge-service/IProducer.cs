namespace bridge_service;

public interface IProducer
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task ConnectAsync(CancellationToken cancellationToken);
    Task PublishAsync(string key, string payload, CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
    Task ReconnectAsync(CancellationToken cancellationToken);
}
