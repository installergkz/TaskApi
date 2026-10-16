using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TaskApi;

namespace TaskApiTests;

public class CompleteTaskTests : IClassFixture<TaskApiFactory>
{
    private readonly TaskApiFactory _factory;
    private readonly HttpClient _client;

    public CompleteTaskTests(TaskApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CompleteTask_PublishesTaskCompletedEvent()
    {
        // Arrange
        _factory.FakePublisher.Published.Clear();

        // Act 1 — создать задачу
        var createResponse = await _client.PostAsJsonAsync("/tasks", new
        {
            title = "test task",
            priority = 2
        });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var taskId = created.GetProperty("id").GetGuid();

        // Act 2 — завершить задачу
        var completeResponse = await _client.PutAsync($"/tasks/{taskId}/complete", null);

        completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert 1 — задача завершена
        var completed = await completeResponse.Content.ReadFromJsonAsync<JsonElement>();
        completed.GetProperty("isCompleted").GetBoolean().Should().BeTrue();
        completed.GetProperty("completedAt").ValueKind.Should().NotBe(JsonValueKind.Null);

        // Assert 2 — событие опубликовано
        _factory.FakePublisher.Published.Should().HaveCount(1);

        var published = _factory.FakePublisher.Published.Single();
        published.TaskId.Should().Be(taskId);
        published.Title.Should().Be("test task");
        published.Priority.Should().Be(Priority.High);
        published.CompletedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CompleteTask_SecondTime_Returns409()
    {
        // Arrange
        _factory.FakePublisher.Published.Clear();

        var createResponse = await _client.PostAsJsonAsync("/tasks", new
        {
            title = "test task",
            priority = 1
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var taskId = created.GetProperty("id").GetGuid();

        // Act
        var first = await _client.PutAsync($"/tasks/{taskId}/complete", null);
        var second = await _client.PutAsync($"/tasks/{taskId}/complete", null);

        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        _factory.FakePublisher.Published.Should().HaveCount(1);
    }
}