namespace Cloudstrap.Demo.BlazorServer.ViewModels
{
    using Cloudstrap.BlazorCommon;
    using Cloudstrap.Demo.Contracts;

    /// <summary>
    /// The WhoAmI page's ViewModel contract: the signed-in user's name and the Api demo host's echo.
    /// </summary>
    public interface IWhoAmIViewModel : IViewModel
    {
        /// <summary>Gets the signed-in user's display name.</summary>
        string UserName
        {
            get;
        }

        /// <summary>Gets the Api demo host's answer, or <see langword="null"/> before initialization.</summary>
        DownstreamWhoAmIDto? WhoAmI
        {
            get;
        }
    }
}
