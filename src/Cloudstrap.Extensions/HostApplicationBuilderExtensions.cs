namespace Cloudstrap.Extensions
{
    using Azure.Core;
    using Azure.Storage.Blobs;
    using Cloudstrap.Core;
    using Microsoft.AspNetCore.DataProtection;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Adds the Azure-backed Cloudstrap features to a host builder: KeyVault-backed configuration, blob
    /// storage and data protection.
    /// </summary>
    public static class HostApplicationBuilderExtensions
    {
        private const string _keyVaultMarker = "Cloudstrap.Extensions.KeyVault";

        /// <summary>
        /// Adds Azure KeyVault as a configuration source when <c>Cloudstrap:KeyVault:Enabled</c> is set,
        /// loading the secrets whose names carry this workload's prefix and mapping
        /// <c>{prefix}-Section--Key</c> onto configuration key <c>Section:Key</c>.
        /// </summary>
        /// <param name="builder">The host application builder to add the configuration source to.</param>
        /// <param name="configure">
        /// An optional hook for the credential and reload interval, which cannot be expressed in
        /// configuration.
        /// </param>
        /// <returns>The same <paramref name="builder"/> instance, so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
        /// <exception cref="ConfigurationValidationException">
        /// The section is enabled but <c>Cloudstrap:KeyVault:VaultUri</c> is missing or relative.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Leave this call in unconditionally: with the section absent or disabled it does nothing at all,
        /// and nothing Azure-related is even constructed. Which environments read a vault is a
        /// configuration decision, not a code one.
        /// </para>
        /// <para>
        /// Call it first, before <c>GetCloudstrapOptions</c> and the other Cloudstrap registrations, so
        /// secrets take part in options binding. The source is added last, so vault values win over
        /// <c>appsettings.json</c> — the standard configuration layering.
        /// </para>
        /// <para>
        /// Failures are deliberately loud. An enabled-but-unnamed vault throws here, before the host exists,
        /// and an unreachable vault fails the configuration build — an application must not start believing
        /// its secrets are absent.
        /// </para>
        /// <para>
        /// Use this or Aspire's KeyVault configuration, never both. Cloudstrap's addition is the secret
        /// prefix filter, which lets several workloads share one vault.
        /// </para>
        /// </remarks>
        public static IHostApplicationBuilder AddCloudstrapKeyVault(
            this IHostApplicationBuilder builder,
            Action<KeyVaultConnectionSettings>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);

            if (builder.Properties.ContainsKey(_keyVaultMarker))
            {
                return builder;
            }

            builder.Properties[_keyVaultMarker] = true;

            KeyVaultOptions options = builder.Configuration
                .GetSection(KeyVaultOptions.SectionName)
                .Get<KeyVaultOptions>() ?? new KeyVaultOptions();

            ValidateOptionsResult result = new KeyVaultOptionsValidator().Validate(name: null, options);

            if (result.Failed)
            {
                throw new ConfigurationValidationException(
                    "The Cloudstrap KeyVault configuration is invalid.",
                    result.Failures ?? []);
            }

            if (!options.Enabled)
            {
                return builder;
            }

            ApplicationOptions application = builder.Configuration
                .GetSection(ApplicationOptions.SectionName)
                .Get<ApplicationOptions>() ?? new ApplicationOptions();

            KeyVaultConnectionSettings settings = new();
            configure?.Invoke(settings);

            KeyVaultSource source = KeyVaultRegistration.Compose(options, application, settings);

            builder.Configuration.AddAzureKeyVault(source.VaultUri, source.Credential, source.Options);

            return builder;
        }

        /// <summary>
        /// Registers the <see cref="BlobContainerClient"/> for the container described by
        /// <c>Cloudstrap:Storage</c> as a singleton, so the application injects the container it works
        /// against rather than building clients itself.
        /// </summary>
        /// <param name="builder">The host application builder to register into.</param>
        /// <param name="configure">An optional hook supplying the credential.</param>
        /// <returns>The same <paramref name="builder"/> instance, so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <para>
        /// The container name defaults to the application's system name in lower case, and is overridable
        /// like every other convention. Authentication is by credential against
        /// <c>Cloudstrap:Storage:BlobServiceUri</c>; setting a connection string switches to that instead,
        /// which is how a local emulator is targeted — explicitly, never by guessing the environment.
        /// A connection string may also come from the standard
        /// <c>ConnectionStrings:CloudstrapStorage</c> entry.
        /// </para>
        /// <para>
        /// The container is created only when <c>CreateContainerIfNotExists</c> asks for it: creating
        /// storage usually belongs to deployment, and needs rights the application should not hold. When it
        /// is asked for, a failure surfaces untouched.
        /// </para>
        /// <para>
        /// Configuration is validated at host startup, naming the offending key.
        /// </para>
        /// </remarks>
        public static IHostApplicationBuilder AddCloudstrapBlobStorage(
            this IHostApplicationBuilder builder,
            Action<AzureCredentialSettings>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.Services.AddOptions<StorageOptions>()
                .BindConfiguration(StorageOptions.SectionName)
                .ValidateOnStart();
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<StorageOptions>, StorageOptionsValidator>());

            builder.Services.AddOptions<ApplicationOptions>()
                .BindConfiguration(ApplicationOptions.SectionName);

            AzureCredentialSettings settings = new();
            configure?.Invoke(settings);

            builder.Services.TryAddSingleton(serviceProvider => BlobStorageRegistration.CreateClient(
                serviceProvider.GetRequiredService<IOptions<StorageOptions>>().Value,
                serviceProvider.GetRequiredService<IOptions<ApplicationOptions>>().Value,
                settings,
                serviceProvider.GetRequiredService<IConfiguration>()));

            return builder;
        }

        /// <summary>
        /// Persists the data protection key ring to the blob named by <c>Cloudstrap:DataProtection</c> and
        /// encrypts it with the configured KeyVault key, so cookies and antiforgery tokens issued by one
        /// instance are readable by every other and survive a restart.
        /// </summary>
        /// <param name="builder">The host application builder to register into.</param>
        /// <param name="configure">An optional hook supplying the credential.</param>
        /// <returns>The same <paramref name="builder"/> instance, so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <para>
        /// Disabled, the call does nothing and the framework's own key storage stays in place — which is the
        /// right choice for a single-instance development run.
        /// </para>
        /// <para>
        /// Enabled, both the key blob and the KeyVault key are required, and a missing one fails startup
        /// naming the key. Encryption is not optional by design: keys written unencrypted because a setting
        /// was forgotten is the failure this prevents. An application that genuinely wants blob persistence
        /// without envelope encryption configures the framework's <c>AddDataProtection()</c> chain itself.
        /// </para>
        /// <para>
        /// Payloads are isolated by application name, defaulting to the workload name, so applications
        /// sharing one storage account cannot read each other's data. The identity needs data-plane rights:
        /// blob read/write on the key container, and wrap and unwrap on the KeyVault key.
        /// </para>
        /// </remarks>
        public static IHostApplicationBuilder AddCloudstrapDataProtection(
            this IHostApplicationBuilder builder,
            Action<AzureCredentialSettings>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.Services.AddOptions<DataProtectionOptions>()
                .BindConfiguration(DataProtectionOptions.SectionName)
                .ValidateOnStart();
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<DataProtectionOptions>,
                    DataProtectionOptionsValidator>());

            DataProtectionOptions options = builder.Configuration
                .GetSection(DataProtectionOptions.SectionName)
                .Get<DataProtectionOptions>() ?? new DataProtectionOptions();

            if (!options.Enabled
                || new DataProtectionOptionsValidator().Validate(name: null, options).Failed)
            {
                return builder;
            }

            ApplicationOptions application = builder.Configuration
                .GetSection(ApplicationOptions.SectionName)
                .Get<ApplicationOptions>() ?? new ApplicationOptions();

            AzureCredentialSettings settings = new();
            configure?.Invoke(settings);
            TokenCredential credential = BlobStorageRegistration.SelectCredential(settings);

            builder.Services.AddDataProtection()
                .SetApplicationName(options.ApplicationName ?? application.WorkloadName)
                .PersistKeysToAzureBlobStorage(options.KeysBlobUri!, credential)
                .ProtectKeysWithAzureKeyVault(options.KeyVaultKeyId!, credential);

            return builder;
        }
    }
}
