namespace Cloudstrap.Demo.Contracts
{
    /// <summary>The body of <c>POST api/v1/orders</c>.</summary>
    /// <param name="Description">A free-form description; stored with the order, never carried by the command.</param>
    /// <param name="Notes">Optional notes of any size, carried by the command to the Worker (deliverable #15); never stored on the row.</param>
    public sealed record PlaceOrderDto(string Description, string? Notes = null);

    /// <summary>The <c>202 Accepted</c> body of <c>POST api/v1/orders</c>.</summary>
    /// <param name="Id">The id of the accepted order, to poll with <c>GET api/v1/orders/{id}</c>.</param>
    public sealed record OrderAcceptedDto(Guid Id);

    /// <summary>The <c>GET api/v1/orders/{id}</c> body: the demo query endpoint (spec D-3).</summary>
    /// <param name="Id">The order id.</param>
    /// <param name="Status"><c>Placed</c> until the Worker processed it, then <c>Processed</c>.</param>
    /// <param name="ProcessedCorrelationId">The correlation id the Worker's handler observed, once processed.</param>
    /// <param name="NotesLength">The length of the notes the Worker received, once processed — the notes themselves are never stored.</param>
    /// <param name="NotesSha256">The hex SHA-256 of the UTF-8 notes the Worker received, once processed.</param>
    public sealed record OrderDto(Guid Id, string Status, string? ProcessedCorrelationId, int? NotesLength, string? NotesSha256);
}
