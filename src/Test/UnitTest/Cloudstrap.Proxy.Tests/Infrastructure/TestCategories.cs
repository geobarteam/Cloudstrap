namespace Cloudstrap.Proxy.Tests.Infrastructure
{
    /// <summary>NUnit category names used in this suite.</summary>
    internal static class TestCategories
    {
        /// <summary>
        /// A test that was green on arrival: it characterizes behavior YARP (or the code before the step) already had,
        /// and guards it against regression. It did not drive the production change of its step.
        /// </summary>
        public const string RegressionGuard = "RegressionGuard";
    }
}
