namespace Cloudstrap.BlazorCommon
{
    using System.Reflection;
    using Microsoft.Extensions.DependencyInjection;

    /// <summary>
    /// Registers a Blazor presentation layer in a dependency injection container by naming
    /// convention.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Scans <typeparamref name="TAssemblyMarker"/>'s assembly (plus any
        /// <see cref="BlazorCommonOptions.AdditionalAssemblies"/>) and registers every public
        /// concrete class whose name ends in a configured convention suffix — <c>ViewModel</c> and
        /// <c>Service</c> by default — as all of its implemented interfaces.
        /// </summary>
        /// <typeparam name="TAssemblyMarker">Any type in the assembly to scan.</typeparam>
        /// <param name="services">The service collection to register into.</param>
        /// <param name="configure">Optional override of the scan conventions.</param>
        /// <returns>The same <paramref name="services"/> instance, so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// <see cref="BlazorCommonOptions.ConventionSuffixes"/> contains a <see langword="null"/>,
        /// empty or whitespace entry.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Registration is interfaces-only: a matching class with no interfaces registers nothing,
        /// and the concrete type itself is never registered. Every registration uses
        /// <see cref="BlazorCommonOptions.Lifetime"/> (<see cref="ServiceLifetime.Transient"/> by
        /// default). Suffix matching is ordinal and runs once per configured suffix. An emptied
        /// suffix list is a legal no-op.
        /// </para>
        /// <para>
        /// Calling this method again appends registrations (standard
        /// <see cref="IServiceCollection"/> semantics) — call it once per scanned assembly set. The
        /// options are consumed eagerly at this call and never registered or bound to
        /// configuration.
        /// </para>
        /// <para>
        /// For anything beyond these knobs — self-registration, decorators, predicate filters —
        /// call Scrutor directly: it is a normal public dependency of this package, and
        /// <c>services.Scan(...)</c> composes freely with this method.
        /// </para>
        /// </remarks>
        public static IServiceCollection AddCloudstrapBlazorCommon<TAssemblyMarker>(
            this IServiceCollection services,
            Action<BlazorCommonOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            var options = new BlazorCommonOptions();
            configure?.Invoke(options);

            foreach (string suffix in options.ConventionSuffixes)
            {
                if (string.IsNullOrWhiteSpace(suffix))
                {
                    throw new ArgumentException(
                        $"{nameof(BlazorCommonOptions.ConventionSuffixes)} contains a null, empty or " +
                        $"whitespace entry ('{suffix}'). Remove the entry, or clear the list for a " +
                        "deliberate no-op.",
                        nameof(configure));
                }
            }

            if (options.ConventionSuffixes.Count == 0)
            {
                return services;
            }

            Assembly[] assemblies =
                [typeof(TAssemblyMarker).Assembly, .. options.AdditionalAssemblies];

            services.Scan(scan =>
            {
                var selector = scan.FromAssemblies(assemblies);
                foreach (string suffix in options.ConventionSuffixes)
                {
                    selector
                        .AddClasses(
                            classes => classes.Where(type =>
                                type.Name.EndsWith(suffix, StringComparison.Ordinal)),
                            publicOnly: true)
                        .AsImplementedInterfaces()
                        .WithLifetime(options.Lifetime);
                }
            });

            return services;
        }
    }
}
