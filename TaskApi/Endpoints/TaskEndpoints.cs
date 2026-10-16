using TaskApi.Data;
using TaskApi.RabbitMQ;
using Microsoft.EntityFrameworkCore;

namespace TaskApi.Endpoints;

public static class TaskEndpoints
{
    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/tasks").WithTags("Tasks");

        group.MapPost("/", CreateTask)
            .WithName("CreateTask")
            .Produces<TaskItem>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapGet("/", GetAllTasks)
            .WithName("GetAllTasks")
            .Produces<List<TaskItem>>();

        group.MapPut("/{id:guid}/complete", CompleteTask)
            .WithName("CompleteTask")
            .Produces<TaskItem>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", DeleteTask)
            .WithName("DeleteTask")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateTask(CreateTaskRequest request, TaskDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["title"] = ["Title не может быть пустым"]
            });
        }

        if (request.Title.Length > 200)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["title"] = ["Title не может превышать 200 символов"]
            });
        }

        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            IsCompleted = false,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = null,
            Priority = request.Priority ?? Priority.Medium
        };

        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/tasks/{task.Id}", task);
    }

    private static async Task<IResult> GetAllTasks(TaskDbContext db, CancellationToken ct)
    {
        var tasks = await db.Tasks
            .AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        return Results.Ok(tasks);
    }

    private static async Task<IResult> CompleteTask(Guid id, TaskDbContext db, IRabbitMqPublisher publisher, ILogger<Program> logger,
        CancellationToken ct)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct);

        if (task is null)
            return Results.NotFound();

        if (task.IsCompleted)
            return Results.Conflict(new { error = "Задача уже завершена" });

        task.IsCompleted = true;
        task.CompletedAt = DateTimeOffset.UtcNow;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogWarning("Конкурентное завершение задачи {TaskId}", id);
            return Results.Conflict(new { error = "Задача уже завершена (конкурентное изменение)" });
        }

        var @event = new TaskCompletedEvent(
            task.Id,
            task.Title,
            task.CompletedAt.Value,
            task.Priority);

        await publisher.PublishTaskCompletedAsync(@event, ct);

        return Results.Ok(task);
    }

    private static async Task<IResult> DeleteTask(Guid id, TaskDbContext db, CancellationToken ct)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct);

        if (task is null)
            return Results.NotFound();

        db.Tasks.Remove(task);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}