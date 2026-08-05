namespace Cloudstrap.WasmTestProject.E2E.Tests.Infrastructure
{
    using System.Diagnostics;
    using System.Reflection;
    using System.Text;

    /// <summary>
    /// Runs the Bff host as a child process (<c>dotnet run --no-build</c>, so the solution must be
    /// built first) and captures its console output for telemetry assertions.
    /// </summary>
    internal sealed class SutProcess : IDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _output = new StringBuilder();
        private readonly object _outputLock = new object();

        private SutProcess(Process process)
        {
            _process = process;
        }

        /// <summary>Whether the SUT process has already terminated (e.g. a startup crash).</summary>
        public bool HasExited => _process.HasExited;

        /// <summary>Everything the SUT wrote to stdout/stderr so far.</summary>
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

        /// <summary>The exit code of the terminated SUT process.</summary>
        public int ExitCode => _process.ExitCode;

        /// <summary>
        /// Waits for the SUT process to exit, then drains the async output streams so
        /// <see cref="CapturedOutput"/> is complete.
        /// </summary>
        public bool WaitForExit(TimeSpan timeout)
        {
            bool exited = _process.WaitForExit((int)timeout.TotalMilliseconds);
            if (exited)
            {
                _process.WaitForExit();
            }

            return exited;
        }

        /// <summary>
        /// Starts the Bff host bound to <paramref name="baseUrl"/>. Optional
        /// <paramref name="applicationArguments"/> are forwarded to the application (after
        /// <c>--</c>), e.g. configuration overrides such as <c>--Cloudstrap:Application:SystemName=</c>.
        /// </summary>
        public static SutProcess Start(string baseUrl, IReadOnlyList<string>? applicationArguments = null)
        {
            string projectPath = Path.Combine(
                FindRepoRoot(), "src", "Test", "WasmTestProject", "src", "Host", "Bff",
                "Cloudstrap.WasmTestProject.Host.Bff.csproj");
            if (!File.Exists(projectPath))
            {
                throw new FileNotFoundException($"SUT project not found at '{projectPath}'.", projectPath);
            }

            // Launch in the same configuration this test assembly was built in (Debug locally, Release in CI).
            string configuration = typeof(SutProcess).Assembly
                .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Debug";

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--no-build");
            // launchSettings.json would override ASPNETCORE_URLS — the fixture owns the address.
            startInfo.ArgumentList.Add("--no-launch-profile");
            startInfo.ArgumentList.Add("--configuration");
            startInfo.ArgumentList.Add(configuration);
            startInfo.ArgumentList.Add("--project");
            startInfo.ArgumentList.Add(projectPath);
            if (applicationArguments is { Count: > 0 })
            {
                startInfo.ArgumentList.Add("--");
                foreach (string argument in applicationArguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }
            }

            startInfo.Environment["ASPNETCORE_URLS"] = baseUrl;
            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            startInfo.Environment["DOTNET_ENVIRONMENT"] = "Development";

            Process process = new Process { StartInfo = startInfo };
            SutProcess sut = new SutProcess(process);
            process.OutputDataReceived += (_, e) => sut.AppendOutput(e.Data);
            process.ErrorDataReceived += (_, e) => sut.AppendOutput(e.Data);
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return sut;
        }

        public void Dispose()
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
                // The process already exited between the check and the kill — nothing to stop.
            }

            _process.Dispose();
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

        private static string FindRepoRoot()
        {
            DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "src", "Cloudstrap.sln")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException(
                $"Could not locate the repository root (a directory containing src/Cloudstrap.sln) above '{AppContext.BaseDirectory}'.");
        }
    }
}
