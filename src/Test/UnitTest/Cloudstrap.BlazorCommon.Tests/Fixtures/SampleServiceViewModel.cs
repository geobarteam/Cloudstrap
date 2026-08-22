namespace Cloudstrap.BlazorCommon.Tests.Fixtures
{
    /// <summary>
    /// Suffix-semantics cast: the name contains <c>Service</c> but ends only in <c>ViewModel</c> —
    /// ordinal <c>EndsWith</c> matching must register it exactly once.
    /// </summary>
    public sealed class SampleServiceViewModel : ISampleServiceViewModel
    {
    }
}
