namespace Cloudstrap.Hangfire.Tests
{
    using System.Reflection;
    using Cloudstrap.Hangfire.Tests.Fixtures.Tasks;
    using NUnit.Framework;

    /// <summary>
    /// The consumer contract (D-3, AC-HF18): <see cref="IBackgroundRecurringTask"/> keeps its default members,
    /// has exactly one token-aware <c>ExecuteAsync</c>, and references no Hangfire type.
    /// </summary>
    [TestFixture]
    public sealed class TaskContractTests
    {
        [Test]
        public void IBackgroundRecurringTask_Members_ReferenceNoHangfireTypes()
        {
            // Arrange
            MemberInfo[] members = typeof(IBackgroundRecurringTask).GetMembers();

            // Act
            string[] hangfireTypes = [.. members
                .SelectMany(ReferencedTypes)
                .Where(type => type.Namespace?.StartsWith("Hangfire", StringComparison.Ordinal) == true)
                .Select(type => type.FullName ?? type.Name)
                .Distinct(StringComparer.Ordinal)];

            // Assert
            Assert.That(hangfireTypes, Is.Empty);
        }

        [Test]
        public void IBackgroundRecurringTask_Defaults_AreTypeNameUtcDefaultQueueEnabledAndOverlapGuarded()
        {
            // Arrange
            IBackgroundRecurringTask task = new NightlyCleanupTask();

            // Act & Assert
            Assert.Multiple(() =>
            {
                Assert.That(task.JobId, Is.EqualTo(nameof(NightlyCleanupTask)));
                Assert.That(task.TimeZone, Is.EqualTo(TimeZoneInfo.Utc));
                Assert.That(task.Queue, Is.EqualTo("default"));
                Assert.That(task.IsEnabled, Is.True);
                Assert.That(task.PreventOverlappingRuns, Is.True);
            });
        }

        [Test]
        public void IBackgroundRecurringTask_HasExactlyOneExecuteAsync_TakingACancellationToken()
        {
            // Arrange & Act
            MethodInfo[] executeMethods = [.. typeof(IBackgroundRecurringTask).GetMethods()
                .Where(method => method.Name == nameof(IBackgroundRecurringTask.ExecuteAsync))];

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(executeMethods, Has.Length.EqualTo(1));
                Assert.That(
                    executeMethods[0].GetParameters().Select(parameter => parameter.ParameterType),
                    Is.EqualTo(new[] { typeof(CancellationToken) }));
                Assert.That(executeMethods[0].ReturnType, Is.EqualTo(typeof(Task)));
            });
        }

        private static IEnumerable<Type> ReferencedTypes(MemberInfo member)
        {
            return member switch
            {
                PropertyInfo property => [property.PropertyType],
                MethodInfo method => [method.ReturnType, .. method.GetParameters().Select(parameter => parameter.ParameterType)],
                _ => [],
            };
        }
    }
}
