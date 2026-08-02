namespace Cloudstrap.Extensions
{
    /// <summary>
    /// Blob storage settings, bound from the <c>Cloudstrap:Storage</c> configuration section. They describe
    /// the one container an application works against.
    /// </summary>
    public sealed class StorageOptions
    {
        /// <summary>
        /// The configuration section these options are bound from.
        /// </summary>
        public const string SectionName = "Cloudstrap:Storage";

        /// <summary>
        /// The connection string name honored when no connection string is configured in this section, so
        /// platform tooling can supply one the usual way.
        /// </summary>
        public const string ConnectionStringName = "CloudstrapStorage";

        /// <summary>
        /// Gets or sets the blob service the container lives in. Required unless a connection string is
        /// supplied.
        /// </summary>
        /// <value>
        /// The blob service URI, for example <c>https://contosostore.blob.core.windows.net/</c>, or
        /// <see langword="null"/> when a connection string is used instead.
        /// </value>
        public Uri? BlobServiceUri
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the container the application works against.
        /// </summary>
        /// <value>
        /// The container name, or <see langword="null"/> to use the application's system name in lower case.
        /// </value>
        public string? ContainerName
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the connection string used instead of <see cref="BlobServiceUri"/> and a credential.
        /// </summary>
        /// <value>
        /// The connection string — <c>UseDevelopmentStorage=true</c> targets a local emulator — or
        /// <see langword="null"/> to authenticate against <see cref="BlobServiceUri"/> with a credential.
        /// </value>
        /// <remarks>
        /// When this is not set, the standard <c>ConnectionStrings:CloudstrapStorage</c> entry is honored.
        /// </remarks>
        public string? ConnectionString
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the container is created if it does not exist.
        /// </summary>
        /// <value>
        /// <see langword="true"/> to create the container on startup. Defaults to <see langword="false"/>,
        /// because creating storage is usually the deployment's job and needs rights the application should
        /// not hold.
        /// </value>
        public bool CreateContainerIfNotExists
        {
            get; set;
        }
    }
}
