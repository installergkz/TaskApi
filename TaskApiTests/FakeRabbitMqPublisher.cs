using System.Collections.Concurrent;
using TaskApi;
using TaskApi.RabbitMQ;

namespace TaskApiTests;

public class FakeRabbitMqPublisher : IRabbitMqPublisher
{
    public ConcurrentBag<TaskCompletedEvent> Published { get; } = new();

    public Task PublishTaskCompletedAsync(TaskCompletedEvent @event, CancellationToken ct = default)
    {
        Published.Add(@event);
        return Task.CompletedTask;
    }
}