namespace Cloudstrap.BlazorWasm
{
    /// <summary>
    /// The default in-memory singleton <see cref="IAntiforgeryTokenStore"/>.
    /// </summary>
    internal sealed class AntiforgeryTokenStore : IAntiforgeryTokenStore
    {
        public string? Token
        {
            get; set;
        }
    }
}
