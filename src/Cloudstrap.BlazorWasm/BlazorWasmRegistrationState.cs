namespace Cloudstrap.BlazorWasm
{
    /// <summary>
    /// The registration-time facts the deferred client configuration needs at resolve time — the
    /// base address the composite was registered with. First registration wins.
    /// </summary>
    internal sealed class BlazorWasmRegistrationState
    {
        public BlazorWasmRegistrationState(string baseAddress)
        {
            BaseAddress = baseAddress;
        }

        public string BaseAddress
        {
            get;
        }
    }
}
