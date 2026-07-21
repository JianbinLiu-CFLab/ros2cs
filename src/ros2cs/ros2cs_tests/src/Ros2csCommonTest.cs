// Modifications Copyright (c) 2026 Jianbin Liu.
//
// Modifications by Jianbin Liu:
// - Added isolated coverage for ros2cs_common primitives.
// - Added Windows native-loader registration and extended-length path coverage.
// - Added NativeLibraryHandle explicit-dispose and finalizer ownership regressions.
// - Added direct-spin fallback log-severity coverage.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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
        public void DirectSpinFallbackIsReportedOnceAsInformation()
        {
            FieldInfo loggedField = typeof(Ros2cs).GetField(
                "directSpinFallbackLogged",
                BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo logMethod = typeof(Ros2cs).GetMethod(
                "LogDirectSpinFallbackOnce",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(loggedField, Is.Not.Null);
            Assert.That(logMethod, Is.Not.Null);

            int previousLoggedValue = (int)loggedField.GetValue(null);
            int informationCount = 0;
            int warningCount = 0;
            string informationMessage = null;
            Ros2csLogger.SetCallback(LogLevel.INFO, message =>
            {
                informationCount++;
                informationMessage = (string)message;
            });
            Ros2csLogger.SetCallback(LogLevel.WARNING, _ => warningCount++);

            try
            {
                loggedField.SetValue(null, 0);
                logMethod.Invoke(null, new object[] { "ROS_DISTRO=lyrical" });
                logMethod.Invoke(null, new object[] { "ROS_DISTRO=lyrical" });

                Assert.That(informationCount, Is.EqualTo(1));
                Assert.That(warningCount, Is.Zero);
                StringAssert.Contains("using direct spin fallback without rcl_wait", informationMessage);
            }
            finally
            {
                loggedField.SetValue(null, previousLoggedValue);
            }
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
        public void RegisteredNativeDirectoriesAreDeduplicatedAndResetWithLoaderSettings()
        {
            string customDirectory = GetTestNativeDirectory("custom-plugins");
            string otherDirectory = GetTestNativeDirectory("other-plugins");
            GlobalVariables.RegisterNativeLibraryDirectory(customDirectory);
            GlobalVariables.RegisterNativeLibraryDirectory(customDirectory);
            GlobalVariables.RegisterNativeLibraryDirectory(otherDirectory);

            CollectionAssert.AreEqual(
                new[] { customDirectory, otherDirectory },
                GlobalVariables.GetRegisteredNativeLibraryDirectories());

            GlobalVariables.SetLoaderSettings(false, "", "");

            CollectionAssert.IsEmpty(GlobalVariables.GetRegisteredNativeLibraryDirectories());
        }

        [Test]
        public void RegisteredNativeDirectoriesReturnDefensiveSnapshotCopies()
        {
            string customDirectory = GetTestNativeDirectory("custom-plugins");
            GlobalVariables.RegisterNativeLibraryDirectory(customDirectory);

            string[] firstSnapshot = GlobalVariables.GetRegisteredNativeLibraryDirectories();
            firstSnapshot[0] = GetTestNativeDirectory("tampered");

            CollectionAssert.AreEquivalent(
                new[] { customDirectory },
                GlobalVariables.GetRegisteredNativeLibraryDirectories());
        }

        [Test]
        public void RegisteredNativeDirectoriesRemainDeduplicatedDuringConcurrentRegistration()
        {
            string customDirectory = GetTestNativeDirectory("custom-plugins");
            string otherDirectory = GetTestNativeDirectory("other-plugins");
            var failures = new ConcurrentQueue<Exception>();

            Parallel.For(0, 128, index =>
            {
                try
                {
                    GlobalVariables.RegisterNativeLibraryDirectory(
                        index % 2 == 0
                            ? customDirectory
                            : otherDirectory);
                    string[] snapshot = GlobalVariables.GetRegisteredNativeLibraryDirectories();
                    if (snapshot.Length > 2)
                    {
                        throw new InvalidOperationException("Registered directory snapshot contains duplicates.");
                    }
                }
                catch (Exception exception)
                {
                    failures.Enqueue(exception);
                }
            });

            Assert.That(failures, Is.Empty);
            CollectionAssert.AreEquivalent(
                new[] { customDirectory, otherDirectory },
                GlobalVariables.GetRegisteredNativeLibraryDirectories());
        }

        [Test]
        public void WindowsRegisteredDirectoryUsesAnExtendedLengthCandidatePath()
        {
            RequireWindows();

            string directory = @"C:\long\workspace\Packages\typesupport\Runtime\Ros2ForUnity\Plugins\Windows\x86_64";
            string library = "unity2foxglove_foxrun_interfaces_v1_phase181_state48_d288_ed82_f1_envelope__rosidl_typesupport_c_native.dll";

            string candidate = DllLoadUtilsWindowsDesktop.BuildRegisteredLibraryPath(directory, library);

            Assert.That(candidate, Is.EqualTo(@"\\?\C:\long\workspace\Packages\typesupport\Runtime\Ros2ForUnity\Plugins\Windows\x86_64\" + library));
        }

        [Test]
        public void WindowsRegisteredUncDirectoryUsesAnExtendedLengthCandidatePath()
        {
            RequireWindows();

            string directory = @"\\server\share\Ros2ForUnity\Plugins\Windows\x86_64";
            const string library = "custom_typesupport.dll";

            string candidate = DllLoadUtilsWindowsDesktop.BuildRegisteredLibraryPath(directory, library);

            Assert.That(candidate, Is.EqualTo(@"\\?\UNC\server\share\Ros2ForUnity\Plugins\Windows\x86_64\custom_typesupport.dll"));
        }

        [Test]
        public void WindowsRegisteredDirectoryPreservesAnAlreadyExtendedLengthCandidatePath()
        {
            RequireWindows();

            string directory = @"\\?\C:\long\workspace\Plugins\Windows\x86_64";
            const string library = "custom_typesupport.dll";

            string candidate = DllLoadUtilsWindowsDesktop.BuildRegisteredLibraryPath(directory, library);

            Assert.That(candidate, Is.EqualTo(@"\\?\C:\long\workspace\Plugins\Windows\x86_64\custom_typesupport.dll"));
        }

        [Test]
        public void WindowsDesktopLoaderDeclaresTheUnicodeLoadLibraryEntryPoint()
        {
            MethodInfo method = typeof(DllLoadUtilsWindowsDesktop).GetMethod(
                "LoadLibraryW",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            var attribute = (DllImportAttribute)Attribute.GetCustomAttribute(method, typeof(DllImportAttribute));
            Assert.That(attribute, Is.Not.Null);
            Assert.That(attribute.EntryPoint, Is.EqualTo("LoadLibraryW"));
            Assert.That(attribute.CharSet, Is.EqualTo(CharSet.Unicode));
            Assert.That(attribute.ExactSpelling, Is.True);
        }

        [Test]
        public void WindowsDesktopLoaderLoadsTheRegisteredLongPathFixture()
        {
            RequireWindows();

            string fixturePath = PrepareLongPathFixture();
            Assert.That(fixturePath.Length, Is.GreaterThan(260));
            Assert.That(File.Exists(fixturePath), Is.True);

            GlobalVariables.RegisterNativeLibraryDirectory(LongPathNativeFixture.Directory);
            var loader = (DllLoadUtils)new DllLoadUtilsWindowsDesktop();
            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = loader.LoadLibraryNoSuffix(LongPathNativeFixture.LibraryStem);
                Assert.That(handle, Is.Not.EqualTo(IntPtr.Zero));
            }
            finally
            {
                loader.FreeLibrary(handle);
            }
        }

        [Test]
        public void WindowsDesktopLoaderDoesNotFallbackAfterExistingRegisteredCandidateFails()
        {
            RequireWindows();

            const string libraryStem = "ros2cs_invalid_registered_native_fixture";
            PrepareLongPathFixture();
            string candidate = DllLoadUtilsWindowsDesktop.BuildRegisteredLibraryPath(
                LongPathNativeFixture.Directory,
                libraryStem + ".dll");
            File.WriteAllText(candidate, "not a native library");

            GlobalVariables.RegisterNativeLibraryDirectory(LongPathNativeFixture.Directory);
            var loader = (DllLoadUtils)new DllLoadUtilsWindowsDesktop();

            UnsatisfiedLinkError exception = Assert.Throws<UnsatisfiedLinkError>(
                () => loader.LoadLibraryNoSuffix(libraryStem));

            StringAssert.Contains(candidate, exception.Message);
        }

        /// <summary>Ensures the finalizer does not invoke a native loader unload.</summary>
        [Test]
        public void NativeLibraryHandleFinalizerNeverInvokesNativeUnload()
        {
            var loader = new RecordingDllLoadUtils();
            WeakReference handleReference = CreateUnreleasedNativeLibraryHandle(loader);

            for (int attempt = 0; handleReference.IsAlive && attempt < 3; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            Assert.That(handleReference.IsAlive, Is.False);
            Assert.That(loader.FreeLibraryCalls, Is.EqualTo(0));
            GC.KeepAlive(loader);
        }

        /// <summary>Ensures repeated explicit disposal unloads through the loader exactly once.</summary>
        [Test]
        public void NativeLibraryHandleExplicitDisposeInvokesNativeUnloadExactlyOnce()
        {
            var loader = new RecordingDllLoadUtils();
            var handle = NativeLibraryHandle.FromHandle(loader, new IntPtr(1));

            handle.Dispose();
            handle.Dispose();

            Assert.That(loader.FreeLibraryCalls, Is.EqualTo(1));
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

        /// <summary>Creates an unreleased handle without leaving a JIT-visible strong reference.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference CreateUnreleasedNativeLibraryHandle(RecordingDllLoadUtils loader)
        {
            return new WeakReference(NativeLibraryHandle.FromHandle(loader, new IntPtr(1)));
        }

        /// <summary>Fake loader that records only the unload calls relevant to ownership tests.</summary>
        private sealed class RecordingDllLoadUtils : DllLoadUtils
        {
            public int FreeLibraryCalls { get; private set; }

            public IntPtr LoadLibrary(string fileName) => new IntPtr(1);

            public IntPtr LoadLibraryNoSuffix(string fileName) => new IntPtr(1);

            public void FreeLibrary(IntPtr handle)
            {
                FreeLibraryCalls++;
            }

            public IntPtr GetProcAddress(IntPtr dllHandle, string name) => IntPtr.Zero;
        }

        private static void RequireWindows()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("Windows-only native loader coverage.");
            }
        }

        private static string GetTestNativeDirectory(string name)
        {
            return Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ros2cs-native-directory-tests", name));
        }

        private static string PrepareLongPathFixture()
        {
            string sourcePath = Path.Combine(
                LongPathNativeFixture.SourceDirectory,
                LongPathNativeFixture.LibraryFileName);
            Assert.That(File.Exists(sourcePath), Is.True);

            // MSVC cannot link directly to a >MAX_PATH output path, so only the test fixture
            // is copied into the isolated build tree. Product packaging never uses this path.
            Directory.CreateDirectory(LongPathNativeFixture.ExtendedDirectory);
            string fixturePath = Path.Combine(
                LongPathNativeFixture.ExtendedDirectory,
                LongPathNativeFixture.LibraryFileName);
            File.Copy(sourcePath, fixturePath, true);
            return fixturePath;
        }
    }
}
