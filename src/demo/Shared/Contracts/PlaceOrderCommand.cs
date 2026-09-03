namespace Cloudstrap.Demo.Contracts
{
    /// <summary>
    /// The command the Api demo host sends to the Worker demo host (deliverable #14). A plain record in a
    /// project with <b>zero package references</b>: the <c>*Command</c> suffix is all the messaging package
    /// needs to classify and route it — contracts never depend on the engine.
    /// </summary>
    /// <param name="OrderId">The order to process.</param>
    public sealed record PlaceOrderCommand(Guid OrderId);
}
