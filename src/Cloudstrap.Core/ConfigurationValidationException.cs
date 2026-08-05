namespace Cloudstrap.Core
{
    /// <summary>
    /// Thrown when the <c>Cloudstrap</c> configuration section is missing or fails validation on the eager,
    /// pre-host read performed by <see cref="ConfigurationExtensions.GetCloudstrapOptions"/>.
    /// </summary>
    /// <remarks>
    /// The dependency-injection path reports the same rule violations through the framework's
    /// <see cref="Microsoft.Extensions.Options.OptionsValidationException"/> at host startup instead.
    /// </remarks>
    public sealed class ConfigurationValidationException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigurationValidationException"/> class.
        /// </summary>
        public ConfigurationValidationException()
        {
            Failures = [];
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigurationValidationException"/> class
        /// with a message and no individual failures.
        /// </summary>
        /// <param name="message">The message describing the configuration problem.</param>
        public ConfigurationValidationException(string message)
            : base(message)
        {
            Failures = [];
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigurationValidationException"/> class
        /// with a message and the exception that caused it.
        /// </summary>
        /// <param name="message">The message describing the configuration problem.</param>
        /// <param name="innerException">The exception that caused this one.</param>
        public ConfigurationValidationException(string message, Exception innerException)
            : base(message, innerException)
        {
            Failures = [];
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigurationValidationException"/> class
        /// with a message and every individual validation failure. The failures are appended to the
        /// exception message, one per line, and stay individually addressable through <see cref="Failures"/>.
        /// </summary>
        /// <param name="message">The message describing the configuration problem.</param>
        /// <param name="failures">The individual validation failures, each naming the offending setting.</param>
        /// <exception cref="ArgumentNullException"><paramref name="failures"/> is <see langword="null"/>.</exception>
        public ConfigurationValidationException(string message, IEnumerable<string> failures)
            : this(message, Materialize(failures))
        {
        }

        private ConfigurationValidationException(string message, string[] failures)
            : base(BuildMessage(message, failures))
        {
            Failures = failures;
        }

        /// <summary>
        /// Gets the individual validation failures, each naming the offending setting by its configuration path.
        /// </summary>
        /// <value>The validation failures. Empty when the exception carries none.</value>
        public IReadOnlyList<string> Failures
        {
            get;
        }

        private static string[] Materialize(IEnumerable<string> failures)
        {
            ArgumentNullException.ThrowIfNull(failures);

            return [.. failures];
        }

        private static string BuildMessage(string message, string[] failures)
        {
            return failures.Length == 0
                ? message
                : $"{message}{Environment.NewLine}{string.Join(Environment.NewLine, failures)}";
        }
    }
}
