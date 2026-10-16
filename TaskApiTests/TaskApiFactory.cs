using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaskApi.Data;
using TaskApi.RabbitMQ;

namespace TaskApiTests;

public class TaskApiFactory : WebApplicationFactory<Program>
{
    private const string TestConnectionString = "Host=localhost;Port=5432;Database=TestDataBase;Username=postgres;Password=postgres";

    public FakeRabbitMqPublisher FakePublisher { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<TaskDbContext>>();
            services.RemoveAll<TaskDbContext>();

            services.AddDbContext<TaskDbContext>(options => options.UseNpgsql(TestConnectionString));

            services.RemoveAll<IRabbitMqPublisher>();
            services.AddSingleton<IRabbitMqPublisher>(FakePublisher);
        });
    }
}