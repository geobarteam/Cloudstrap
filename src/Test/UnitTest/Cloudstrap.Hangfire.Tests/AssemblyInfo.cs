using NUnit.Framework;

// Hangfire keeps process-global state (GlobalConfiguration, JobStorage.Current): fixtures never run concurrently.
[assembly: NonParallelizable]
