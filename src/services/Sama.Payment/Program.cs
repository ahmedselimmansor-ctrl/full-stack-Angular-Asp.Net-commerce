using Sama.Payment.Features;
using Sama.Payment.Integration;
using Sama.Payment.Persistence;
using Sama.Shared.Infrastructure;
using Sama.Shared.Infrastructure.Persistence;
using Sama.Shared.Messaging;
using Sama.Shared.Messaging.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.AddSamaServiceDefaults("sama-payment");

builder.Services.AddSamaDbContext<PaymentDbContext>(
    builder.Configuration, builder.Environment, PaymentDbContext.SchemaName);

builder.Services.AddOptions<SimulatedProviderOptions>()
    .Bind(builder.Configuration.GetSection(SimulatedProviderOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Swap this registration for a real acquirer's client; nothing else in the saga changes.
builder.Services.AddSingleton<IPaymentProvider, SimulatedPaymentProvider>();

builder.Services.AddSamaMessaging(builder.Configuration);
builder.Services.AddOutboxDispatcher<PaymentDbContext>(builder.Configuration);

builder.Services.AddIntegrationEventHandler<PaymentRequested, ChargeOnPaymentRequested>();
builder.Services.AddSamaSubscriber(builder.Configuration);

var app = builder.Build();

app.UseSamaServiceDefaults();

app.MapPaymentEndpoints();
app.MapSamaDefaultEndpoints();

await app.RunAsync();

public partial class Program;
