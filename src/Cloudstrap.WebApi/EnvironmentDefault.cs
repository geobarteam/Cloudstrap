namespace Cloudstrap.WebApi
{
    /// <summary>
    /// The one place the "explicit wins, unset follows the environment" rule is written.
    /// </summary>
    /// <remarks>
    /// Three settings in this package are <see langword="bool"/>? for exactly this reason, and each states
    /// its environment default in its own documentation:
    /// <list type="bullet">
    ///   <item><description>
    ///     <see cref="ExceptionHandlingSettings.IncludeDetails"/> — unset means <c>Development</c> only.
    ///   </description></item>
    ///   <item><description>
    ///     <c>Cloudstrap:Scalar:Enabled</c> — unset means <c>Development</c> only.
    ///   </description></item>
    ///   <item><description>
    ///     <c>Cloudstrap:JwtBearer:RequireHttpsMetadata</c> — unset means everywhere <em>except</em>
    ///     <c>Development</c>.
    ///   </description></item>
    /// </list>
    /// </remarks>
    internal static class EnvironmentDefault
    {
        /// <summary>
        /// Resolves a three-state setting against the default its environment implies.
        /// </summary>
        /// <param name="explicitValue">The configured value, or <see langword="null"/> when unset.</param>
        /// <param name="environmentDefault">The value implied by the hosting environment.</param>
        /// <returns>
        /// <paramref name="explicitValue"/> when it is set; otherwise <paramref name="environmentDefault"/>.
        /// </returns>
        public static bool Resolve(bool? explicitValue, bool environmentDefault)
        {
            return explicitValue ?? environmentDefault;
        }
    }
}
