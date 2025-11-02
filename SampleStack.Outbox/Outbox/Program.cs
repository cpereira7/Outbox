using Microsoft.EntityFrameworkCore;
using Outbox.Infrastructure.PackageQueue;
using Outbox.Infrastructure.Persistence;
using Outbox.Infrastructure.Processor;
using Outbox.Infrastructure.Repository;
using Outbox.Service;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        // To preserve the default behavior, capture the original delegate to call later.
        var builtInFactory = options.InvalidModelStateResponseFactory;

        options.InvalidModelStateResponseFactory = context =>
        {
            var logger = context.HttpContext.RequestServices
                                .GetRequiredService<ILogger<Program>>();

            logger.LogWarning("Model state invalid: {Errors}", context.ModelState);

            // Invoke the default behavior, which produces a ValidationProblemDetails
            // response.
            // To produce a custom response, return a different implementation of 
            // IActionResult instead.
            return builtInFactory(context);
        };
    });

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<PackageDbContext>(options =>
    options.UseSqlite("Data Source=testDb.sqlite"));

// Core services
builder.Services.AddScoped<IPackageService, PackageService>();
builder.Services.AddScoped<IPackageRepository, PackageRepository>();
builder.Services.AddScoped<IPackageEventQueue, PackageEventQueue>();
builder.Services.AddScoped<INotificationService, ConsoleNotificationService>();

// Background processor
builder.Services.AddHostedService<PackageEventQueueProcessor>();

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.SuppressConsumesConstraintForFormFileParameters = true;
        options.SuppressInferBindingSourcesForParameters = true;
        options.SuppressModelStateInvalidFilter = true;
        options.SuppressMapClientErrors = true;
        options.ClientErrorMapping[StatusCodes.Status404NotFound].Link =
            "https://httpstatuses.com/404";
    });


builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.UseHttpsRedirection();

app.Run();
