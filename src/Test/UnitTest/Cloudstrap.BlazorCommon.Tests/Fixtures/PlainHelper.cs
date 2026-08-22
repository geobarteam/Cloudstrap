namespace Cloudstrap.BlazorCommon.Tests.Fixtures
{
    /// <summary>
    /// Feature interface of the non-matching fixture.
    /// </summary>
    public interface IPlainHelper
    {
    }

    /// <summary>
    /// Exclusion cast: public, concrete and interfaced, but its name matches no convention suffix —
    /// the scan must skip it.
    /// </summary>
    public sealed class PlainHelper : IPlainHelper
    {
    }
}
