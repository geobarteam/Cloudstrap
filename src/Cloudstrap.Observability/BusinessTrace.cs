namespace Cloudstrap.Observability
{
    using System.Diagnostics;

    /// <summary>
    /// The default <see cref="IBusinessTrace"/>: a singleton owning the <c>Cloudstrap.Business</c> activity
    /// source, disposed with the container at shutdown. Tags use the <c>cloudstrap.business.*</c> vocabulary.
    /// </summary>
    internal sealed class BusinessTrace : IBusinessTrace, IDisposable
    {
        internal const string ComponentTagKey = "cloudstrap.business.component";

        private readonly ActivitySource _activitySource = new(CloudstrapActivitySources.Business);

        public IBusinessTraceScope StartSpan(string operation, string component)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(operation);
            ArgumentException.ThrowIfNullOrWhiteSpace(component);

            Activity? activity = _activitySource.StartActivity(operation);
            activity?.SetTag(ComponentTagKey, component);

            return new BusinessTraceScope(activity);
        }

        public void Dispose() => _activitySource.Dispose();
    }
}
