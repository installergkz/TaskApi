using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace TaskApi.RabbitMQ;

public sealed class RabbitMqPublisher : IRabbitMqPublisher, IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqPublisher> _logger;

    private IConnection? _connection;
    private IChannel? _channel;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private volatile bool _initialized;

    public RabbitMqPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
            };

            _connection = await factory.CreateConnectionAsync(ct);
            _channel = await _connection.CreateChannelAsync(cancellationToken: ct);

            await _channel.ExchangeDeclareAsync(
                exchange: _options.ExchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: ct);

            _initialized = true;
            _logger.LogInformation("RabbitMQ publisher инициализирован, exchange={Exchange}", _options.ExchangeName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось инициализировать RabbitMQ publisher");
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task PublishTaskCompletedAsync(TaskCompletedEvent @event, CancellationToken ct = default)
    {
        try
        {
            await EnsureInitializedAsync(ct);

            var body = JsonSerializer.SerializeToUtf8Bytes(@event, JsonSerializerOptions.Default);

            var props = new BasicProperties
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                MessageId = Guid.NewGuid().ToString("N"),
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            };

            await _channel!.BasicPublishAsync(
                exchange: _options.ExchangeName,
                routingKey: _options.RoutingKey,
                mandatory: false,
                basicProperties: props,
                body: body,
                cancellationToken: ct);

            _logger.LogInformation("Опубликовано событие TaskCompleted: taskId={TaskId}, routingKey={RoutingKey}", @event.TaskId, _options.RoutingKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Не удалось опубликовать событие TaskCompleted для taskId={TaskId}. " +
                "Задача завершена, но событие потеряно.",
                @event.TaskId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_channel is not null)
            {
                await _channel.CloseAsync();
                await _channel.DisposeAsync();
            }

            if (_connection is not null)
            {
                await _connection.CloseAsync();
                await _connection.DisposeAsync();
            }

            _logger.LogInformation("RabbitMQ publisher корректно закрыт");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка при закрытии RabbitMQ publisher");
        }
        finally
        {
            _initLock.Dispose();
        }
    }
}