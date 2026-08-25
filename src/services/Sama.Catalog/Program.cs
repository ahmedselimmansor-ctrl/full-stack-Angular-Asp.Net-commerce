using FluentValidation;
using Sama.Catalog.Features.Categories;
using Sama.Catalog.Features.Products;
using Sama.Catalog.Persistence;
using Sama.Shared.Infrastructure;
using Sama.Shared.Infrastructure.Persistence;
using Sama.Shared.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.AddSamaServiceDefaults("sama-catalog");

builder.Services.AddSamaDbContext<CatalogDbContext>(
    builder.Configuration, builder.Environment, CatalogDbContext.SchemaName);

builder.Services.AddValidatorsFromAssemblyContaining<CreateProductRequestValidator>(ServiceLifetime.Singleton);

builder.Services.AddSamaMessaging(builder.Configuration);
builder.Services.AddOutboxDispatcher<CatalogDbContext>(builder.Configuration);

var app = builder.Build();

app.UseSamaServiceDefaults();

app.MapProductEndpoints();
app.MapCategoryEndpoints();
app.MapSamaDefaultEndpoints();

await app.RunAsync();

public partial class Program;
