using FluentValidation;
using Sama.Basket.Features;
using Sama.Basket.Integration;
using Sama.Basket.Storage;
using Sama.Shared.Infrastructure;
using Sama.Shared.Infrastructure.Http;
using Sama.Shared.Messaging;
using Sama.Shared.Messaging.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.AddSamaServiceDefaults("sama-basket");

builder.Services.AddOptions<BasketOptions>()
    .Bind(builder.Configuration.GetSection(BasketOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<BasketStore>();
builder.Services.AddValidatorsFromAssemblyContaining<AddItemRequestValidator>(ServiceLifetime.Singleton);

builder.Services.AddTransient<BearerForwardingHandler>();

builder.Services.AddHttpClient<ICatalogClient, CatalogClient>(client =>
    {
        client.BaseAddress = new Uri(
            builder.Configuration["Services:Catalog"]
            ?? throw new InvalidOperationException("Services:Catalog is not configured."));

        // Well under the shopper's patience and under the gateway's own timeout, so a slow
        // catalogue surfaces as a clean 503 rather than a hung request.
        client.Timeout = TimeSpan.FromSeconds(5);
    })
    .AddHttpMessageHandler<BearerForwardingHandler>()

    // Retries transient failures, sheds load with a circuit breaker when the catalogue is
    // unhealthy, and bounds each attempt. Without the breaker, every basket request would
    // queue behind a dead dependency and take this service down with it.
    .AddStandardResilienceHandler();

builder.Services.AddSamaMessaging(builder.Configuration);
builder.Services.AddIntegrationEventHandler<OrderConfirmed, ClearBasketOnOrderConfirmed>();
builder.Services.AddSamaSubscriber(builder.Configuration);

var app = builder.Build();

app.UseSamaServiceDefaults();

app.MapBasketEndpoints();
app.MapSamaDefaultEndpoints();

await app.RunAsync();

public partial class Program;
