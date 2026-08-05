using NUnit.Framework;

// One SUT process and one fixed port for the whole assembly — tests must not run in parallel.
[assembly: NonParallelizable]
