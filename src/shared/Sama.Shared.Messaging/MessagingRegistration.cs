using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sama.Shared.Infrastructure.Persistence;
using Sama.Shared.Messaging.Consuming;
using Sama.Shared.Messaging.Contracts;
using Sama.Shared.Messaging.Outbox;
using Sama.Shared.Messaging.Publishing;

namespace Sama.Shared.Messaging;

public static class MessagingRegistration
{
    /// <summary>Registers the Pub/Sub publisher. Required by any service with an outbox.</summary>
    public static IServiceCollection AddSamaMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<PubSubOptions>()
            .Bind(configuration.GetSection(PubSubOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton<IEventPublisher, PubSubEventPublisher>();
        services.TryAddSingleton<EventSubscriptionRegistry>();

        return services;
    }

    /// <summary>
    /// Starts the outbox dispatcher for a service's context. Call once per DbContext that
    /// raises domain events.
    /// </summary>
    public static IServiceCollection AddOutboxDispatcher<TContext>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TContext : SamaDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<OutboxOptions>()
            .Bind(configuration.GetSection(OutboxOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHostedService<OutboxDispatcher<TContext>>();

        return services;
    }

    /// <summary>Binds an event type to its handler and adds it to the dispatch table.</summary>
    public static IServiceCollection AddIntegrationEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<EventSubscriptionRegistry>();
        services.AddScoped<IIntegrationEventHandler<TEvent>, THandler>();

        // The registry is a singleton built during registration, so resolving it here to
        // record the mapping is safe and keeps registration to a single call site.
        var registry = services
            .Where(descriptor => descriptor.ServiceType == typeof(EventSubscriptionRegistry))
            .Select(descriptor => descriptor.ImplementationInstance as EventSubscriptionRegistry)
            .FirstOrDefault(instance => instance is not null);

        if (registry is null)
        {
            registry = new EventSubscriptionRegistry();
            services.RemoveAll<EventSubscriptionRegistry>();
            services.AddSingleton(registry);
        }

        registry.Register<TEvent, THandler>();

        return services;
    }

    /// <summary>Starts the subscriber loop. Call after every handler has been registered.</summary>
    public static IServiceCollection AddSamaSubscriber(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<SubscriberOptions>()
            .Bind(configuration.GetSection(SubscriberOptions.SectionName))
            .ValidateOnStart();

        services.AddHostedService<PubSubSubscriberService>();

        return services;
    }
}
