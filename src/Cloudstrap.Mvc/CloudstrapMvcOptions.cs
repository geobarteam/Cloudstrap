namespace Cloudstrap.Mvc
{
    /// <summary>
    /// The MVC bootstrap settings, bound from <c>Cloudstrap:Mvc</c> and validated at host startup.
    /// </summary>
    public sealed class CloudstrapMvcOptions
    {
        /// <summary>
        /// The configuration section these settings bind from.
        /// </summary>
        public const string SectionName = "Cloudstrap:Mvc";

        /// <summary>
        /// Gets or sets the session-state settings, bound from <c>Cloudstrap:Mvc:Session</c>.
        /// </summary>
        /// <value>The session settings; the hardened defaults when the section is absent.</value>
        public SessionSettings Session { get; set; } = new();
    }
}
