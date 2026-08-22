namespace Cloudstrap.BlazorCommon.Tests
{
    using System.Reflection;
    using NUnit.Framework;

    /// <summary>
    /// Permanent one-way-door pins on the band contracts: the D-1 <see cref="IViewModel"/>
    /// signature (AC-BC6), the D-2 two-method <see cref="IErrorHandler"/> trim, and AC-BC5's
    /// contract-only guarantee (the package ships no implementation).
    /// </summary>
    [TestFixture]
    public sealed class ContractShapeTests
    {
        [Test]
        public void IViewModel_InitializeAsync_TakesAnOptionalCancellationTokenAndReturnsTask()
        {
            // Arrange
            MethodInfo[] methods = typeof(IViewModel).GetMethods();

            // Assert — exactly one method, Task InitializeAsync(CancellationToken = default)
            Assert.That(methods, Has.Length.EqualTo(1));
            MethodInfo initialize = methods[0];
            ParameterInfo[] parameters = initialize.GetParameters();
            Assert.Multiple(() =>
            {
                Assert.That(initialize.Name, Is.EqualTo("InitializeAsync"));
                Assert.That(initialize.ReturnType, Is.EqualTo(typeof(Task)));
                Assert.That(parameters, Has.Length.EqualTo(1));
                Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(CancellationToken)));
                Assert.That(parameters[0].IsOptional, Is.True, "The CancellationToken must be optional (D-1).");
            });
        }

        [Test]
        public void IErrorHandler_DeclaresExactlyHandleErrorAndShowError()
        {
            // Arrange
            MethodInfo[] methods = typeof(IErrorHandler).GetMethods();

            // Assert — the D-2 trim: two methods, nothing else, both void
            MethodInfo? handleError = methods.SingleOrDefault(method => method.Name == "HandleError");
            MethodInfo? showError = methods.SingleOrDefault(method => method.Name == "ShowError");
            Assert.Multiple(() =>
            {
                Assert.That(methods, Has.Length.EqualTo(2));
                Assert.That(handleError, Is.Not.Null);
                Assert.That(showError, Is.Not.Null);
                Assert.That(handleError!.ReturnType, Is.EqualTo(typeof(void)));
                Assert.That(
                    handleError.GetParameters().Select(parameter => parameter.ParameterType),
                    Is.EqualTo(new[] { typeof(Exception) }));
                Assert.That(showError!.ReturnType, Is.EqualTo(typeof(void)));
                Assert.That(
                    showError.GetParameters().Select(parameter => parameter.ParameterType),
                    Is.EqualTo(new[] { typeof(string) }));
            });
        }

        [Test]
        public void BlazorCommonAssembly_DeclaresNoErrorHandlerImplementation()
        {
            // Arrange
            Type[] types = typeof(IErrorHandler).Assembly.GetTypes();

            // Assert — AC-BC5's contract-only half: the package never ships an implementation
            Assert.That(
                types.Where(type =>
                    type != typeof(IErrorHandler) && typeof(IErrorHandler).IsAssignableFrom(type)),
                Is.Empty);
        }
    }
}
