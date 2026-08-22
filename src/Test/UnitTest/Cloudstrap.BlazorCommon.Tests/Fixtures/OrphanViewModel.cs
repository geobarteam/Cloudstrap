namespace Cloudstrap.BlazorCommon.Tests.Fixtures
{
    /// <summary>
    /// Edge-case cast: matches the <c>ViewModel</c> suffix but implements no interfaces — the
    /// interfaces-only registration shape must register nothing for it.
    /// </summary>
    public sealed class OrphanViewModel
    {
    }
}
