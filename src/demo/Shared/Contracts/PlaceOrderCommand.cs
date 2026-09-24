namespace Cloudstrap.Demo.Contracts
{
    /// <summary>
    /// The command the Api demo host sends to the Worker demo host (deliverable #14). A plain record in a
    /// project with <b>zero package references</b>: the <c>*Command</c> suffix is all the messaging package
    /// needs to classify and route it — contracts never depend on the engine.
    /// </summary>
    /// <param name="OrderId">The order to process.</param>
    /// <param name="Notes">
    /// Free-form notes of any size (deliverable #15). Nothing here marks them as large: no attribute, no
    /// naming convention — when the serialized body exceeds the claim-check threshold the whole body is
    /// stored in the <c>demo-claimcheck</c> container and only a reference travels; the handler sees the notes whole.
    /// </param>
    public sealed record PlaceOrderCommand(Guid OrderId, string? Notes = null);
}
