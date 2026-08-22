namespace Cloudstrap.BlazorCommon.Tests.Fixtures
{
    /// <summary>
    /// Custom-convention target: public, concrete, <c>Presenter</c> suffix — matched only when the
    /// consumer overrides <see cref="BlazorCommonOptions.ConventionSuffixes"/>.
    /// </summary>
    public sealed class CustomSuffixPresenter : ICustomSuffixPresenter
    {
    }
}
