using FluentValidation;
using Sama.Ordering.Features;
using Sama.Ordering.Integration;
using Sama.Ordering.Persistence;
using Sama.Shared.Infrastructure;
using Sama.Shared.Infrastructure.Http;
using Sama.Shared.Infrastructure.Persistence;
using Sama.Shared.Messaging;
using Sama.Shared.Messaging.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.AddSamaServiceDefaults("sama-ordering");

builder.Services.AddSamaDbContext<OrderingDbContext>(
    builder.Configuration, builder.Environment, OrderingDbContext.SchemaName);

builder.Services.AddValidatorsFromAssemblyContaining<CheckoutRequestValidator>(ServiceLifetime.Singleton);

builder.Services.AddTransient<BearerForwardingHandler>();

builder.Services.AddHttpClient<IBasketClient, BasketClient>(client =>
    {
        client.BaseAddress = new Uri(
            builder.Configuration["Services:Basket"]
            ?? throw new InvalidOperationException("Services:Basket is not configured."));
        client.Timeout = TimeSpan.FromSeconds(5);
    })
    .AddHttpMessageHandler<BearerForwardingHandler>()
    .AddStandardResilienceHandler();

builder.Services.AddHttpClient<ICatalogClient, CatalogClient>(client =>
    {
        client.BaseAddress = new Uri(
            builder.Configuration["Services:Catalog"]
            ?? throw new InvalidOperationException("Services:Catalog is not configured."));
        client.Timeout = TimeSpan.FromSeconds(5);
    })
    .AddHttpMessageHandler<BearerForwardingHandler>()
    .AddStandardResilienceHandler();

builder.Services.AddSamaMessaging(builder.Configuration);
builder.Services.AddOutboxDispatcher<OrderingDbContext>(builder.Configuration);

// Ordering owns the checkout saga: it starts it, and every other participant reports back here.
builder.Services.AddIntegrationEventHandler<StockReserved, AdvanceOnStockReserved>();
builder.Services.AddIntegrationEventHandler<StockReservationRejected, CancelOnStockRejected>();
builder.Services.AddIntegrationEventHandler<PaymentSucceeded, ConfirmOnPaymentSucceeded>();
builder.Services.AddIntegrationEventHandler<PaymentFailed, CancelOnPaymentFailed>();
builder.Services.AddSamaSubscriber(builder.Configuration);

var app = builder.Build();

app.UseSamaServiceDefaults();

app.MapOrderEndpoints();
app.MapSamaDefaultEndpoints();

await app.RunAsync();

public partial class Program;
