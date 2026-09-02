// The contract fixtures: plain records in their own namespace, deliberately referencing no Wolverine or
// Cloudstrap type — the zero-package-reference contract the suffix conventions exist for (AC-MSG4).
// Nothing in this file may gain an attribute, interface or base class from a messaging package.
namespace Cloudstrap.Messaging.Tests.Fixtures.Contracts
{
    /// <summary>A command by suffix.</summary>
    public sealed record PlaceOrderCommand(Guid OrderId);

    /// <summary>An event by suffix.</summary>
    public sealed record OrderPlacedEvent(Guid OrderId);

    /// <summary>A message by suffix.</summary>
    public sealed record OrderNoteMessage(string Note);

    /// <summary>A type no suffix rule classifies.</summary>
    public sealed record OrderSnapshot(Guid OrderId);
}
