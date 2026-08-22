namespace Cloudstrap.BlazorCommon.Tests.Fixtures
{
    /// <summary>
    /// Exclusion cast: matches the <c>ViewModel</c> suffix but is abstract — the scan must skip it.
    /// </summary>
    public abstract class AbstractSampleViewModel : ISampleViewModel
    {
    }
}
