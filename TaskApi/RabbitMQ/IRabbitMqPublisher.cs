namespace TaskApi.RabbitMQ;

public interface IRabbitMqPublisher
{
    Task PublishTaskCompletedAsync(TaskCompletedEvent @event, CancellationToken ct = default);
}