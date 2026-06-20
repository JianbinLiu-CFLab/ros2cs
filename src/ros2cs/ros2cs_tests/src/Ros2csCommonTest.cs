// Modifications Copyright (c) 2026 Jianbin Liu.
//
// Modifications by Jianbin Liu:
// - Added isolated coverage for ros2cs_common primitives.

using System;
using NUnit.Framework;

namespace ROS2.Test
{
    [TestFixture]
    public class Ros2csCommonTest
    {
        [SetUp]
        public void ResetCommonState()
        {
            Ros2csLogger.LogLevel = LogLevel.DEBUG;
            GlobalVariables.SetLoaderSettings(false, "", "");
        }

        [TearDown]
        public void ClearLoggerCallbacks()
        {
            foreach (LogLevel level in Enum.GetValues(typeof(LogLevel)))
            {
                Ros2csLogger.SetCallback(level, null);
            }
            GlobalVariables.SetLoaderSettings(false, "", "");
        }

        [Test]
        public void LoggerCallbackExceptionDoesNotEscapeLogCall()
        {
            Ros2csLogger.SetCallback(LogLevel.INFO, _ => throw new InvalidOperationException("expected test callback failure"));

            Assert.DoesNotThrow(() => Ros2csLogger.GetInstance().LogInfo("callback failure should be isolated"));
        }

        [Test]
        public void FilteredDebugFactoryIsNotEvaluated()
        {
            Ros2csLogger.LogLevel = LogLevel.INFO;
            bool factoryEvaluated = false;

            Ros2csLogger.GetInstance().LogDebug(() =>
            {
                factoryEvaluated = true;
                return "debug message";
            });

            Assert.That(factoryEvaluated, Is.False);
        }

        [Test]
        public void EnabledDebugFactoryIsEvaluated()
        {
            string callbackMessage = null;
            Ros2csLogger.SetCallback(LogLevel.DEBUG, message => callbackMessage = (string)message);

            Ros2csLogger.GetInstance().LogDebug(() => "debug message");

            Assert.That(callbackMessage, Is.EqualTo("[ROS2CS] debug message"));
        }

        [Test]
        public void LoaderSettingsCanBeReplacedAtomically()
        {
            GlobalVariables.SetLoaderSettings(true, "libdependency.dylib", "/tmp/ros2cs/");

            Assert.That(GlobalVariables.preloadLibrary, Is.True);
            Assert.That(GlobalVariables.preloadLibraryName, Is.EqualTo("libdependency.dylib"));
            Assert.That(GlobalVariables.absolutePath, Is.EqualTo("/tmp/ros2cs/"));
        }

        [Test]
        public void LoaderSettingsNormalizeNullStrings()
        {
            GlobalVariables.SetLoaderSettings(true, null, null);

            Assert.That(GlobalVariables.preloadLibrary, Is.True);
            Assert.That(GlobalVariables.preloadLibraryName, Is.EqualTo(""));
            Assert.That(GlobalVariables.absolutePath, Is.EqualTo(""));
        }

        [Test]
        public void BenchmarkDisposeIsIdempotent()
        {
            // Simulates repeated cleanup paths around tight benchmark scopes and process shutdown.
            var benchmark = new Benchmark("common-test");

            benchmark.Dispose();
            benchmark.Dispose();

            Assert.That(benchmark.IsDisposed, Is.True);
        }

        [Test]
        public void UnsatisfiedLinkErrorIsCaughtByBaseException()
        {
            var exception = new UnsatisfiedLinkError("missing native library");

            Assert.That(exception, Is.InstanceOf<UnsatisfiedLinkException>());
            Assert.That(exception.Message, Is.EqualTo("missing native library"));
        }
    }
}
