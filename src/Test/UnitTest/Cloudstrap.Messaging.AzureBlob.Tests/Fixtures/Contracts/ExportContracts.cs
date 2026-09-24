// The contract fixtures: plain records in their own namespace, deliberately referencing no Wolverine,
// Cloudstrap or Azure type — the threshold, not a naming convention or an attribute, decides what is
// offloaded (AC-CK3). Nothing in this file may gain an attribute, interface or base class from a
// messaging or storage package.
namespace Cloudstrap.Messaging.AzureBlob.Tests.Fixtures.Contracts
{
    /// <summary>A command carrying an arbitrarily large export body.</summary>
    public sealed record ExportReadyCommand(Guid ExportId, string Content);

    /// <summary>A command whose serialized body is tiny.</summary>
    public sealed record SmallCommand(Guid Id);

    /// <summary>A large command whose handler fails the first <paramref name="FailuresBeforeSuccess"/> attempts.</summary>
    public sealed record FlakyExportCommand(Guid Id, string Content, int FailuresBeforeSuccess);
}
