namespace Cloudstrap.BlazorCommon.Tests.Fixtures
{
    /// <summary>
    /// Exclusion cast: matches the <c>ViewModel</c> suffix but is internal — the public-only scan
    /// must skip it.
    /// </summary>
    internal sealed class InternalSampleViewModel : ISampleViewModel
    {
    }
}
