using EFCoreTransaciton;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddEndpointsApiExplorer();
builder.Services.AddDbContext<EfCoreTransactionDbContext>(
        o =>
        {
            var config = builder.Configuration;
            o.UseSqlServer(config.GetConnectionString("Default"));
        })
    ;


builder.Services.AddTransient<Service>()
    .AddTransient<Repository>()
    .AddTransient<InTransaction>();

var app = builder.Build();

app.UseMiddleware<TransactionMiddleware>();

app.MapGet("/", (EfCoreTransactionDbContext db) => db.Models.ToListAsync());


// middleware
app.MapGet(
    "/two",
    (Service service) => service.WriteNoTransaction(new Model() { Name = Guid.NewGuid().ToString() })
).WithMetadata(new TransactionMiddlewareAttribute());

// two transactions
app.MapGet(
    "/twoerr",
    (Service service) => service.Write(new Model() { Name = Guid.NewGuid().ToString() })
).WithMetadata(new TransactionMiddlewareAttribute());

// filter
app.MapGet(
    "/three",
    (Service service) => service.WriteNoTransaction(new Model() { Name = Guid.NewGuid().ToString() })
).AddEndpointFilter<EndpointFilter>();


// wrapped
app.MapGet(
    "/four",
    async (InTransaction inTransaction, Service service) =>
        await inTransaction.Run(() => service.WriteNoTransaction(new Model() { Name = Guid.NewGuid().ToString() }))
);


app.UseHttpsRedirection();


app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

public class Service(Repository repo, EfCoreTransactionDbContext db)
{
    public async Task<Model> Write(Model model)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            var result = await repo.Write(model);

            await transaction.CommitAsync();
            return result;
        }
        catch (Exception e)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<Model> WriteNoTransaction(Model model)
    {
        return await repo.Write(model);
    }
}


public class Repository(EfCoreTransactionDbContext db)
{
    public async Task<Model> Write(Model model)
    {
        db.Models.Add(model);
        await db.SaveChangesAsync();
        return model;
    }
}

public class TransactionMiddleware
{
    private readonly RequestDelegate _next;

    public TransactionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, EfCoreTransactionDbContext database)
    {
        var endpoint = context.GetEndpoint();
        var transactionAttribute = endpoint?.Metadata.GetMetadata<TransactionMiddlewareAttribute>();

        if (transactionAttribute == null)
        {
            await _next(context);
            return;
        }

        await using var transaction = await database.Database.BeginTransactionAsync();

        try
        {
            await _next(context);
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}

public class TransactionMiddlewareAttribute : Attribute
{
}

public class EndpointFilter(EfCoreTransactionDbContext db) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var result = await next(context);
            await transaction.CommitAsync();
            return result;
        }
        catch (Exception e)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}

public class InTransaction(EfCoreTransactionDbContext db)
{
    public async Task<T> Run<T>(Func<Task<T>> action)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            var result = await action();
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
