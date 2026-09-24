namespace Cloudstrap.Demo.E2E.Tests.Infrastructure
{
    using System.Diagnostics;
    using System.Runtime.InteropServices;
    using System.Text;
    using Azure.Storage.Blobs;

    /// <summary>
    /// The blob backend of the E2E suite (deliverable #15, DL-10 — the D-3 template): when
    /// <c>CLOUDSTRAP_TEST_BLOB</c> is unset the fixture starts the <c>azurite-blob</c> emulator from PATH
    /// (the npm <c>azurite</c> package's blob-only binary) on <c>127.0.0.1:10000</c> with a per-run temp
    /// location and stops it at teardown; when set, nothing is started and the value is the connection
    /// string every host is handed (attach mode — CI's service container, or a real account). In both modes
    /// readiness is polled through the SDK before any host boots. A missing emulator fails loudly with the
    /// install command — never a silent skip (the Playwright-install precedent).
    /// </summary>
    internal sealed class AzuriteProcess : IDisposable
    {
        /// <summary>The environment variable that switches the fixture to attach mode.</summary>
        public const string EnvironmentVariable = "CLOUDSTRAP_TEST_BLOB";

        /// <summary>The storage-emulator shortcut connection string (account <c>devstoreaccount1</c>, port 10000).</summary>
        public const string DevelopmentStorage = "UseDevelopmentStorage=true";

        private const string _executable = "azurite-blob";
        private readonly Process? _process;
        private readonly string? _location;
        private readonly StringBuilder _output = new StringBuilder();
        private readonly object _outputLock = new object();

        private AzuriteProcess(Process? process, string? location, string connectionString)
        {
            _process = process;
            _location = location;
            ConnectionString = connectionString;
        }

        /// <summary>The connection string every host is handed: the override, or the emulator shortcut.</summary>
        public string ConnectionString
        {
            get;
        }

        /// <summary>Whether the fixture attached to an external backend instead of starting the emulator.</summary>
        public bool IsAttached => _process is null;

        /// <summary>Everything the emulator wrote so far (empty in attach mode).</summary>
        public string CapturedOutput
        {
            get
            {
                lock (_outputLock)
                {
                    return _output.ToString();
                }
            }
        }

        /// <summary>
        /// Attaches to <c>CLOUDSTRAP_TEST_BLOB</c> when set, else to an emulator already answering on the
        /// development-storage endpoint (a developer's own Azurite), else starts <c>azurite-blob</c>; in every
        /// case waits until the account answers before returning.
        /// </summary>
        public static async Task<AzuriteProcess> StartOrAttachAsync()
        {
            string? configured = Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                AzuriteProcess attached = new AzuriteProcess(null, null, configured);
                await attached.WaitUntilReadyAsync();
                return attached;
            }

            if (await AnswersAsync(DevelopmentStorage))
            {
                return new AzuriteProcess(null, null, DevelopmentStorage);
            }

            AzuriteProcess started = StartEmulator();
            try
            {
                await started.WaitUntilReadyAsync();
                return started;
            }
            catch
            {
                // Never leave an emulator behind: an orphan keeps the parent shell's pipes open.
                started.Dispose();
                throw;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_process is not null)
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill(entireProcessTree: true);
                        _process.WaitForExit(10_000);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Already gone between the check and the kill.
                }

                _process.Dispose();
            }

            if (_location is not null)
            {
                try
                {
                    Directory.Delete(_location, recursive: true);
                }
                catch (IOException)
                {
                    // Best effort: a per-run temp folder the OS will reclaim.
                }
                catch (UnauthorizedAccessException)
                {
                    // Best effort, same.
                }
            }
        }

        private static AzuriteProcess StartEmulator()
        {
            string executable = LocateOnPath()
                ?? throw new InvalidOperationException(
                    $"The E2E suite needs the Azurite blob emulator: '{_executable}' was not found on PATH. " +
                    $"Install it once with 'npm install -g azurite', or set {EnvironmentVariable} to the connection " +
                    "string of a running emulator or storage account to attach instead.");

            string location = Path.Combine(Path.GetTempPath(), $"cloudstrap-azurite-{Guid.NewGuid():N}");
            Directory.CreateDirectory(location);

            // The npm shim is a .cmd on Windows: run it through the command interpreter, and kill the tree as a whole.
            bool windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = windows ? "cmd.exe" : executable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            if (windows)
            {
                startInfo.ArgumentList.Add("/c");
                startInfo.ArgumentList.Add(executable);
            }

            // --skipApiVersionCheck: the Azure SDK the suite pins may speak a newer service version than the
            // installed emulator knows; Azurite then serves it anyway (the documented emulator switch).
            foreach (string argument in new[] { "--silent", "--skipApiVersionCheck", "--location", location, "--blobHost", "127.0.0.1", "--blobPort", "10000" })
            {
                startInfo.ArgumentList.Add(argument);
            }

            Process process = new Process { StartInfo = startInfo };
            AzuriteProcess azurite = new AzuriteProcess(process, location, DevelopmentStorage);
            process.OutputDataReceived += (_, e) => azurite.AppendOutput(e.Data);
            process.ErrorDataReceived += (_, e) => azurite.AppendOutput(e.Data);
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Safety net for a test process that dies without running its teardown.
            AppDomain.CurrentDomain.ProcessExit += (_, _) => azurite.Dispose();
            return azurite;
        }

        private static async Task<bool> AnswersAsync(string connectionString)
        {
            try
            {
                await ProbeClient(connectionString).GetPropertiesAsync();
                return true;
            }
            catch (Azure.RequestFailedException)
            {
                return false;
            }
            catch (AggregateException)
            {
                return false;
            }
        }

        private static string? LocateOnPath()
        {
            string[] candidates = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? [.. (Environment.GetEnvironmentVariable("PATHEXT") ?? ".CMD;.EXE").Split(';', StringSplitOptions.RemoveEmptyEntries).Select(ext => _executable + ext.ToLowerInvariant()), _executable]
                : [_executable];

            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (string candidate in candidates)
                {
                    string path = Path.Combine(directory.Trim('"'), candidate);
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
            }

            return null;
        }

        private async Task WaitUntilReadyAsync()
        {
            BlobServiceClient client = ProbeClient(ConnectionString);
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            Exception? last = null;
            while (DateTime.UtcNow < deadline)
            {
                if (_process is { HasExited: true })
                {
                    break;
                }

                try
                {
                    await client.GetPropertiesAsync();
                    return;
                }
                catch (Azure.RequestFailedException failure)
                {
                    last = failure;
                }
                catch (AggregateException failure)
                {
                    last = failure;
                }

                await Task.Delay(250);
            }

            throw new InvalidOperationException(
                $"The blob backend did not answer within 60 seconds ({(IsAttached ? EnvironmentVariable + " attach mode" : "fixture-started azurite-blob")}). " +
                $"Last failure: {last?.Message}{Environment.NewLine}{CapturedOutput}");
        }

        /// <summary>A client that answers fast and never retries: one probe, one verdict.</summary>
        private static BlobServiceClient ProbeClient(string connectionString)
        {
            BlobClientOptions options = new BlobClientOptions();
            options.Retry.MaxRetries = 0;
            options.Retry.NetworkTimeout = TimeSpan.FromSeconds(3);
            return new BlobServiceClient(connectionString, options);
        }

        private void AppendOutput(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (_outputLock)
            {
                _output.AppendLine(line);
            }
        }
    }
}
