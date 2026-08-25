using Sama.Inventory.Features;
using Sama.Inventory.Integration;
using Sama.Inventory.Persistence;
using Sama.Shared.Infrastructure;
using Sama.Shared.Infrastructure.Persistence;
using Sama.Shared.Messaging;
using Sama.Shared.Messaging.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.AddSamaServiceDefaults("sama-inventory");

builder.Services.AddSamaDbContext<InventoryDbContext>(
    builder.Configuration, builder.Environment, InventoryDbContext.SchemaName);

builder.Services.AddSamaMessaging(builder.Configuration);
builder.Services.AddOutboxDispatcher<InventoryDbContext>(builder.Configuration);

// Saga participation: hold stock when an order is placed, give it back if the order dies.
builder.Services.AddIntegrationEventHandler<OrderPlaced, ReserveStockOnOrderPlaced>();
builder.Services.AddIntegrationEventHandler<OrderCancelled, ReleaseStockOnOrderCancelled>();
builder.Services.AddSamaSubscriber(builder.Configuration);

var app = builder.Build();

app.UseSamaServiceDefaults();

app.MapStockEndpoints();
app.MapSamaDefaultEndpoints();

await app.RunAsync();

public partial class Program;
