namespace Cloudstrap.Messaging.AzureBlob.Tests.Fixtures
{
    using Cloudstrap.Messaging.AzureBlob.Tests.Fixtures.Contracts;
    using Wolverine;

    /// <summary>
    /// A small fixture command whose transactional handler stages an <see cref="Export"/> row and cascades an
    /// above-threshold <see cref="ExportReadyCommand"/> of <paramref name="ContentLength"/> characters, then
    /// optionally throws. The command itself stays under the threshold so only the cascaded message is offloaded.
    /// </summary>
    public sealed record StageExportCommand(Guid ExportId, int ContentLength, bool Fail);

    /// <summary>
    /// The transactional fixture handler: a plain Wolverine handler taking the <see cref="ExportsDbContext"/>
    /// and the bus — entity write and outgoing message commit together, or not at all.
    /// </summary>
    public static class StageExportCommandHandler
    {
        /// <summary>Stages the export, sends the large command, and fails when asked to; every attempt is counted.</summary>
        public static async Task Handle(StageExportCommand command, ExportsDbContext db, IMessageBus bus, AttemptCounter attempts)
        {
            attempts.Increment(command.ExportId);
            db.Exports.Add(new Export { Id = command.ExportId, Label = "staged" });
            await bus.SendAsync(new ExportReadyCommand(command.ExportId, new string('o', command.ContentLength)));

            if (command.Fail)
            {
                throw new InvalidOperationException("The handler fails after staging the row and the message.");
            }
        }
    }
}
