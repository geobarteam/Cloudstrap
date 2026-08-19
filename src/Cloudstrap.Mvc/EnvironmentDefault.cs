namespace Cloudstrap.Mvc
{
    /// <summary>
    /// The one place the "explicit wins, unset follows the environment" rule is written.
    /// </summary>
    /// <remarks>
    /// Two settings in this package are <see langword="bool"/>? for exactly this reason, and each states
    /// its environment default in its own documentation:
    /// <list type="bullet">
    ///   <item><description>
    ///     <see cref="ExceptionHandlingSettings.IncludeDetails"/> — unset means <c>Development</c> only.
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="ExceptionHandlingSettings.UseDeveloperExceptionPage"/> — unset means
    ///     <c>Development</c> only.
    ///   </description></item>
    /// </list>
    /// Deliberately re-expressed package-locally rather than shared with <c>Cloudstrap.WebApi</c>: D-2
    /// rejects a cross-package reference that would drag the versioning/OpenAPI stack into every MVC
    /// closure.
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
