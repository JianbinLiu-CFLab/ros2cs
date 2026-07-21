// Copyright 2019-2021 Robotec.ai
// Copyright 2016-2018 Esteve Fernandez <esteve@apache.org>
// Modifications Copyright (c) 2026 Jianbin Liu.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

// Modifications by Jianbin Liu:
// - Added disposable ownership for native library handles.
// - Hardened platform detection and native unload guards.
// - Reworked Linux preload tracking to avoid stale handles.
// - Serialized native library handle disposal and symbol lookup diagnostics.
// - Made native loader settings an atomic snapshot and applied them on macOS.
// - Added Windows registered native directories and extended-length LoadLibraryW candidates.
// - Prevented NativeLibraryHandle finalizers from invoking native loader APIs during host shutdown.

// Based on http://dimitry-i.blogspot.com.es/2013/01/mononet-how-to-dynamically-load-native.html

using System;
using System.ComponentModel;
using System.IO;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading;

namespace ROS2
{
  /// <summary>
  /// Global native library loader settings shared by platform loader implementations.
  /// </summary>
  /// <remarks>
  /// Settings are read as an immutable snapshot by each load operation. Apply all
  /// loader settings before the first native library load to avoid mixed behavior
  /// across already-loaded assemblies.
  /// </remarks>
  public class GlobalVariables {
    private static readonly string[] EmptyRegisteredNativeLibraryDirectories = new string[0];
    private static Snapshot settings = new Snapshot(false, "", "", EmptyRegisteredNativeLibraryDirectories);
    private static readonly object settingsMutex = new object();

    /// <summary>Whether a native dependency should be preloaded before loading ros2cs libraries.</summary>
    public static bool preloadLibrary
    {
      get { return GetSnapshot().PreloadLibrary; }
      set
      {
        lock (settingsMutex)
        {
          Snapshot current = GetSnapshot();
          SetSnapshot(new Snapshot(value, current.PreloadLibraryName, current.AbsolutePath, current.RegisteredNativeLibraryDirectories));
        }
      }
    }

    /// <summary>Native dependency name used when <see cref="preloadLibrary"/> is enabled.</summary>
    public static string preloadLibraryName
    {
      get { return GetSnapshot().PreloadLibraryName; }
      set
      {
        lock (settingsMutex)
        {
          Snapshot current = GetSnapshot();
          SetSnapshot(new Snapshot(current.PreloadLibrary, value, current.AbsolutePath, current.RegisteredNativeLibraryDirectories));
        }
      }
    }

    /// <summary>
    /// Optional absolute directory prefix prepended directly to the library file name.
    /// </summary>
    /// <remarks>
    /// Include the trailing directory separator, for example <c>/opt/ros/jazzy/lib/</c>.
    /// If the combined path does not identify an existing library, the loader retries
    /// with the bare file name using the platform default search paths. On Windows, an
    /// existing explicit candidate that fails to load is reported without a same-name fallback.
    /// </remarks>
    public static string absolutePath
    {
      get { return GetSnapshot().AbsolutePath; }
      set
      {
        lock (settingsMutex)
        {
          Snapshot current = GetSnapshot();
          SetSnapshot(new Snapshot(current.PreloadLibrary, current.PreloadLibraryName, value, current.RegisteredNativeLibraryDirectories));
        }
      }
    }

    /// <summary>
    /// Register an explicit native-plugin directory for the Windows desktop loader.
    /// </summary>
    /// <remarks>
    /// Directories are normalized, de-duplicated case-insensitively, and kept in
    /// registration order. Register all directories before the first native load.
    /// </remarks>
    public static void RegisterNativeLibraryDirectory(string directory)
    {
      string normalizedDirectory = NormalizeNativeLibraryDirectory(directory);
      lock (settingsMutex)
      {
        Snapshot current = GetSnapshot();
        foreach (string registeredDirectory in current.RegisteredNativeLibraryDirectories)
        {
          if (String.Equals(registeredDirectory, normalizedDirectory, StringComparison.OrdinalIgnoreCase))
          {
            return;
          }
        }

        string[] updatedDirectories = new string[current.RegisteredNativeLibraryDirectories.Length + 1];
        Array.Copy(current.RegisteredNativeLibraryDirectories, updatedDirectories, current.RegisteredNativeLibraryDirectories.Length);
        updatedDirectories[updatedDirectories.Length - 1] = normalizedDirectory;
        SetSnapshot(new Snapshot(current.PreloadLibrary, current.PreloadLibraryName, current.AbsolutePath, updatedDirectories));
      }
    }

    /// <summary>Return a defensive copy of the registered native-plugin directories.</summary>
    public static string[] GetRegisteredNativeLibraryDirectories()
    {
      return CopyRegisteredNativeLibraryDirectories(GetSnapshot().RegisteredNativeLibraryDirectories);
    }

    internal sealed class Snapshot
    {
      internal readonly bool PreloadLibrary;
      internal readonly string PreloadLibraryName;
      internal readonly string AbsolutePath;
      internal readonly string[] RegisteredNativeLibraryDirectories;

      internal Snapshot(bool preloadLibrary, string preloadLibraryName, string absolutePath, string[] registeredNativeLibraryDirectories)
      {
        PreloadLibrary = preloadLibrary;
        PreloadLibraryName = preloadLibraryName ?? "";
        AbsolutePath = absolutePath ?? "";
        RegisteredNativeLibraryDirectories = CopyRegisteredNativeLibraryDirectories(registeredNativeLibraryDirectories);
      }
    }

    internal static Snapshot GetSnapshot()
    {
      return Volatile.Read(ref settings);
    }

    /// <summary>Atomically replace all native loader settings.</summary>
    public static void SetLoaderSettings(bool preloadLibrary, string preloadLibraryName, string absolutePath)
    {
      lock (settingsMutex)
      {
        SetSnapshot(new Snapshot(preloadLibrary, preloadLibraryName, absolutePath, EmptyRegisteredNativeLibraryDirectories));
      }
    }

    private static string NormalizeNativeLibraryDirectory(string directory)
    {
      if (String.IsNullOrWhiteSpace(directory))
      {
        throw new ArgumentException("Native library directory cannot be empty.", nameof(directory));
      }

      string normalizedDirectory = Path.GetFullPath(directory);
      string rootDirectory = Path.GetPathRoot(normalizedDirectory);
      int endIndex = normalizedDirectory.Length;
      while (endIndex > rootDirectory.Length &&
        (normalizedDirectory[endIndex - 1] == Path.DirectorySeparatorChar || normalizedDirectory[endIndex - 1] == Path.AltDirectorySeparatorChar))
      {
        endIndex--;
      }
      return endIndex == normalizedDirectory.Length ? normalizedDirectory : normalizedDirectory.Substring(0, endIndex);
    }

    private static string[] CopyRegisteredNativeLibraryDirectories(string[] directories)
    {
      if (directories == null || directories.Length == 0)
      {
        return new string[0];
      }

      string[] copy = new string[directories.Length];
      Array.Copy(directories, copy, directories.Length);
      return copy;
    }

    private static void SetSnapshot(Snapshot snapshot)
    {
      Volatile.Write(ref settings, snapshot);
    }
  }

  /// <summary>Native loader platform selected by <see cref="DllLoadUtilsFactory"/>.</summary>
  public enum Platform {
    /// <summary>Linux/Unix loader using <c>libdl.so.2</c> and <c>.so</c> libraries.</summary>
    Unix,
    /// <summary>macOS loader using <c>libdl.dylib</c> and <c>.dylib</c> libraries.</summary>
    MacOSX,
    /// <summary>Windows desktop loader using kernel32 library loading APIs.</summary>
    WindowsDesktop,
    /// <summary>Universal Windows Platform loader using packaged-library APIs.</summary>
    UWP,
    /// <summary>Unsupported or unrecognized runtime platform.</summary>
    Unknown
  }

  public class DllLoadUtilsFactory {
    [DllImport ("api-ms-win-core-libraryloader-l2-1-0.dll", EntryPoint = "LoadPackagedLibrary", SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr LoadPackagedLibrary ([MarshalAs (UnmanagedType.LPWStr)] string fileName, int reserved = 0);

    [DllImport ("api-ms-win-core-libraryloader-l1-2-0.dll", EntryPoint = "FreeLibrary", SetLastError = true, ExactSpelling = true)]
    private static extern int FreeLibraryUWP (IntPtr handle);

    public static DllLoadUtils GetDllLoadUtils () {
      switch (CheckPlatform ()) {
        case Platform.Unix:
          return new DllLoadUtilsUnix ();
        case Platform.MacOSX:
          return new DllLoadUtilsMacOSX ();
        case Platform.WindowsDesktop:
          return new DllLoadUtilsWindowsDesktop ();
        case Platform.UWP:
          return new DllLoadUtilsUWP ();
        case Platform.Unknown:
        default:
          throw new UnknownPlatformError ();
      }
    }

    private static bool IsUWP () {
      try {
        // Probe a stable system library; app containers reject this path on desktop-only runtimes.
        IntPtr ptr = LoadPackagedLibrary ("kernel32.dll");
        if (ptr == IntPtr.Zero) {
          return false;
        }
        FreeLibraryUWP (ptr);
        return true;
      } catch (Exception e) when (
          e is TypeLoadException ||
          e is DllNotFoundException ||
          e is EntryPointNotFoundException ||
          e is BadImageFormatException) {
        return false;
      }
    }

    private static Platform CheckPlatform () {
      // Prefer RuntimeInformation for coarse OS detection before probing platform-specific loaders.
      if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
      {
        return IsUWP() ? Platform.UWP : Platform.WindowsDesktop;
      }
      if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
      {
        return Platform.MacOSX;
      }
      if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
      {
        return Platform.Unix;
      }
      return Platform.Unknown;
    }
  }

  /// <summary>Platform-agnostic native library loader contract used by ros2cs.</summary>
  public interface DllLoadUtils {
    /// <summary>
    /// Load a ros2cs-style generated native library by appending the platform-specific
    /// <c>_native</c> infix and native library extension.
    /// </summary>
    IntPtr LoadLibrary (string fileName);

    /// <summary>
    /// Load a native library by appending only the platform-specific native library extension.
    /// </summary>
    IntPtr LoadLibraryNoSuffix (string fileName);

    /// <summary>Release a native library handle returned by this loader.</summary>
    void FreeLibrary (IntPtr handle);

    /// <summary>Resolve a native symbol from a loaded library handle.</summary>
    IntPtr GetProcAddress (IntPtr dllHandle, string name);
  }

  /// <summary>Owns a loaded native library and releases it when disposed.</summary>
  /// <remarks>
  /// Keeps native libraries alive while delegates created from them remain reachable.
  /// Only explicit <see cref="Dispose()"/> invokes the native loader; finalization
  /// clears managed ownership and leaves process-exit cleanup to the operating system.
  /// </remarks>
  public sealed class NativeLibraryHandle : IDisposable
  {
    private readonly DllLoadUtils dllLoadUtils;
    private readonly object mutex = new object();
    private IntPtr handle;
    private bool disposed;

    private NativeLibraryHandle(DllLoadUtils dllLoadUtils, IntPtr handle)
    {
      this.dllLoadUtils = dllLoadUtils;
      this.handle = handle;
    }

    /// <summary>Native library handle used for symbol resolution.</summary>
    public IntPtr Handle
    {
      get
      {
        lock (mutex)
        {
          if (disposed)
          {
            throw new ObjectDisposedException(nameof(NativeLibraryHandle));
          }
          return handle;
        }
      }
    }

    /// <summary>Load a ros2cs-style native library and wrap ownership of the returned handle.</summary>
    public static NativeLibraryHandle LoadLibrary(DllLoadUtils dllLoadUtils, string fileName)
    {
      return new NativeLibraryHandle(dllLoadUtils, dllLoadUtils.LoadLibrary(fileName));
    }

    /// <summary>Load a native library by its exact platform name and wrap ownership of the handle.</summary>
    public static NativeLibraryHandle LoadLibraryNoSuffix(DllLoadUtils dllLoadUtils, string fileName)
    {
      return new NativeLibraryHandle(dllLoadUtils, dllLoadUtils.LoadLibraryNoSuffix(fileName));
    }

    /// <summary>Wrap an already loaded native library handle so it is released exactly once.</summary>
    public static NativeLibraryHandle FromHandle(DllLoadUtils dllLoadUtils, IntPtr handle)
    {
      if (handle == IntPtr.Zero)
      {
        throw new ArgumentException("Native library handle cannot be zero", nameof(handle));
      }
      return new NativeLibraryHandle(dllLoadUtils, handle);
    }

    /// <summary>Clears managed ownership without invoking a native loader API during host teardown.</summary>
    ~NativeLibraryHandle()
    {
      Dispose(false);
    }

    /// <summary>Release the native library handle.</summary>
    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases managed ownership for both paths and unloads natively only for explicit disposal.
    /// </summary>
    private void Dispose(bool disposing)
    {
      lock (mutex)
      {
        if (disposed)
        {
          return;
        }

        try
        {
          // The finalizer can run after a host such as Unity has started
          // unloading its Mono/native runtime. Calling a native loader entry
          // point then is unsafe and can terminate the process. Explicit
          // ownership still releases immediately; process teardown lets the
          // operating system reclaim an undisposed handle.
          if (disposing && handle != IntPtr.Zero)
          {
            dllLoadUtils.FreeLibrary(handle);
          }
        }
        catch
        {
        }
        finally
        {
          handle = IntPtr.Zero;
          disposed = true;
        }
      }
    }
  }

  /// <summary>Native library loader for Universal Windows Platform packaged applications.</summary>
  public class DllLoadUtilsUWP : DllLoadUtils {

    [DllImport ("api-ms-win-core-libraryloader-l2-1-0.dll", SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr LoadPackagedLibrary ([MarshalAs (UnmanagedType.LPWStr)] string fileName, int reserved = 0);

    [DllImport ("api-ms-win-core-libraryloader-l1-2-0.dll", SetLastError = true, ExactSpelling = true)]
    private static extern int FreeLibrary (IntPtr handle);

    [DllImport ("api-ms-win-core-libraryloader-l1-2-0.dll", SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr GetProcAddress (IntPtr handle, string procedureName);

    private static string preloadedLibraryKey = "";
    private static NativeLibraryHandle preloadedLibraryHandle = null;
    private static readonly object preloadMutex = new object();

    private static string LastLoadErrorMessage(string libraryName)
    {
      int errorCode = Marshal.GetLastWin32Error();
      return libraryName + ": Win32 error " + errorCode + " (" + new Win32Exception(errorCode).Message + ")";
    }

    private IntPtr LoadExactLibrary(string libraryName)
    {
      Ros2csLogger.GetInstance().LogDebug(() => "Loading packaged library: " + libraryName);
      IntPtr ptr = LoadPackagedLibrary(libraryName);
      if (ptr == IntPtr.Zero)
      {
        throw new UnsatisfiedLinkError(LastLoadErrorMessage(libraryName));
      }
      return ptr;
    }

    private IntPtr LoadLibraryByName(string libraryName)
    {
      GlobalVariables.Snapshot settings = GlobalVariables.GetSnapshot();
      if (settings.PreloadLibrary)
      {
        CheckPreloadLibraries(settings);
      }

      return LoadWithFallback(libraryName, settings.AbsolutePath);
    }

    private void CheckPreloadLibraries(GlobalVariables.Snapshot settings)
    {
      string preloadKey = settings.AbsolutePath + "|" + settings.PreloadLibraryName;
      if (settings.PreloadLibraryName == "")
      {
        return;
      }

      lock (preloadMutex)
      {
        if (preloadedLibraryKey == preloadKey)
        {
          return;
        }

        IntPtr libPtr = LoadWithFallback(settings.PreloadLibraryName, settings.AbsolutePath);
        NativeLibraryHandle newHandle = NativeLibraryHandle.FromHandle(this, libPtr);
        NativeLibraryHandle oldHandle = preloadedLibraryHandle;
        preloadedLibraryHandle = newHandle;
        preloadedLibraryKey = preloadKey;

        if (oldHandle != null)
        {
          oldHandle.Dispose();
        }
      }
    }

    private IntPtr LoadWithFallback(string libraryName, string absolutePath)
    {
      if (!String.IsNullOrEmpty(absolutePath))
      {
        string libraryPath = ApplyAbsolutePath(absolutePath, libraryName);
        IntPtr ptr = LoadPackagedLibrary(libraryPath);
        if (ptr != IntPtr.Zero)
        {
          return ptr;
        }

        int errorCode = Marshal.GetLastWin32Error();
        Ros2csLogger.GetInstance().LogDebug(() => "Could not find " + libraryPath + ": Win32 error " + errorCode + ". Fallback to " + libraryName);
      }

      return LoadExactLibrary(libraryName);
    }

    private static string ApplyAbsolutePath(string absolutePath, string libraryName)
    {
      return String.IsNullOrEmpty(absolutePath) ? libraryName : absolutePath + libraryName;
    }

    void DllLoadUtils.FreeLibrary (IntPtr handle) {
      if (handle != IntPtr.Zero) {
        FreeLibrary (handle);
      }
    }

    IntPtr DllLoadUtils.GetProcAddress (IntPtr dllHandle, string name) {
      IntPtr ptr = GetProcAddress (dllHandle, name);
      if (ptr == IntPtr.Zero) {
        throw new EntryPointNotFoundException(name);
      }
      return ptr;
    }

    IntPtr DllLoadUtils.LoadLibrary (string fileName) {
      string libraryName = fileName + "_native.dll";
      return LoadLibraryByName(libraryName);
    }

    IntPtr DllLoadUtils.LoadLibraryNoSuffix (string fileName) {
      string libraryName = fileName + ".dll";
      return LoadLibraryByName(libraryName);
    }
  }

  /// <summary>Native library loader for Windows desktop processes.</summary>
  public class DllLoadUtilsWindowsDesktop : DllLoadUtils {

    [DllImport ("kernel32.dll", EntryPoint = "LoadLibraryW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr LoadLibraryW ([MarshalAs(UnmanagedType.LPWStr)] string fileName);

    [DllImport ("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern int FreeLibrary (IntPtr handle);

    [DllImport ("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr GetProcAddress (IntPtr handle, string procedureName);

    private static string preloadedLibraryKey = "";
    private static NativeLibraryHandle preloadedLibraryHandle = null;
    private static readonly object preloadMutex = new object();

    private static string LastLoadErrorMessage(string libraryName)
    {
      int errorCode = Marshal.GetLastWin32Error();
      return libraryName + ": Win32 error " + errorCode + " (" + new Win32Exception(errorCode).Message + ")";
    }

    private IntPtr LoadExactLibrary(string libraryName)
    {
      Ros2csLogger.GetInstance().LogDebug(() => "Loading library: " + libraryName);
      IntPtr ptr = LoadLibraryW(libraryName);
      if (ptr == IntPtr.Zero)
      {
        throw new UnsatisfiedLinkError(LastLoadErrorMessage(libraryName));
      }
      return ptr;
    }

    private IntPtr LoadLibraryByName(string libraryName)
    {
      GlobalVariables.Snapshot settings = GlobalVariables.GetSnapshot();
      if (settings.PreloadLibrary)
      {
        CheckPreloadLibraries(settings);
      }

      return LoadWithFallback(libraryName, settings);
    }

    private void CheckPreloadLibraries(GlobalVariables.Snapshot settings)
    {
      string preloadKey = GetPreloadKey(settings);
      if (settings.PreloadLibraryName == "")
      {
        return;
      }

      lock (preloadMutex)
      {
        if (preloadedLibraryKey == preloadKey)
        {
          return;
        }

        IntPtr libPtr = LoadWithFallback(settings.PreloadLibraryName, settings);
        NativeLibraryHandle newHandle = NativeLibraryHandle.FromHandle(this, libPtr);
        NativeLibraryHandle oldHandle = preloadedLibraryHandle;
        preloadedLibraryHandle = newHandle;
        preloadedLibraryKey = preloadKey;

        if (oldHandle != null)
        {
          oldHandle.Dispose();
        }
      }
    }

    private IntPtr LoadWithFallback(string libraryName, GlobalVariables.Snapshot settings)
    {
      if (!String.IsNullOrEmpty(settings.AbsolutePath))
      {
        string libraryPath = ApplyAbsolutePath(settings.AbsolutePath, libraryName);
        if (File.Exists(libraryPath))
        {
          return LoadExactLibrary(libraryPath);
        }

        IntPtr ptr = LoadLibraryW(libraryPath);
        if (ptr != IntPtr.Zero)
        {
          return ptr;
        }

        int errorCode = Marshal.GetLastWin32Error();
        Ros2csLogger.GetInstance().LogDebug(() => "Could not find " + libraryPath + ": Win32 error " + errorCode + ". Fallback to " + libraryName);
      }

      foreach (string directory in settings.RegisteredNativeLibraryDirectories)
      {
        string candidatePath = BuildRegisteredLibraryPath(directory, libraryName);
        if (!File.Exists(candidatePath))
        {
          continue;
        }

        // An existing exact candidate must either load or fail visibly; never fall back to a same-name DLL.
        return LoadExactLibrary(candidatePath);
      }

      return LoadExactLibrary(libraryName);
    }

    /// <summary>
    /// Build the exact extended-length candidate used for a registered Windows plugin directory.
    /// </summary>
    public static string BuildRegisteredLibraryPath(string directory, string libraryName)
    {
      if (String.IsNullOrWhiteSpace(directory))
      {
        throw new ArgumentException("Native library directory cannot be empty.", nameof(directory));
      }
      if (String.IsNullOrWhiteSpace(libraryName) || Path.IsPathRooted(libraryName))
      {
        throw new ArgumentException("Native library name must be a relative file name.", nameof(libraryName));
      }

      return ToExtendedLengthPath(Path.Combine(directory, libraryName));
    }

    private static string GetPreloadKey(GlobalVariables.Snapshot settings)
    {
      return settings.AbsolutePath + "|" + settings.PreloadLibraryName + "|" + String.Join("|", settings.RegisteredNativeLibraryDirectories);
    }

    private static string ToExtendedLengthPath(string path)
    {
      if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
      {
        return path;
      }
      if (path.StartsWith(@"\\", StringComparison.Ordinal))
      {
        return @"\\?\UNC\" + path.Substring(2);
      }
      return @"\\?\" + path;
    }

    private static string ApplyAbsolutePath(string absolutePath, string libraryName)
    {
      return String.IsNullOrEmpty(absolutePath) ? libraryName : absolutePath + libraryName;
    }

    void DllLoadUtils.FreeLibrary (IntPtr handle) {
      if (handle != IntPtr.Zero) {
        FreeLibrary (handle);
      }
    }

    IntPtr DllLoadUtils.GetProcAddress (IntPtr dllHandle, string name) {
      IntPtr ptr = GetProcAddress (dllHandle, name);
      if (ptr == IntPtr.Zero) {
        throw new EntryPointNotFoundException(name);
      }
      return ptr;
    }

    IntPtr DllLoadUtils.LoadLibrary (string fileName) {
      string libraryName = fileName + "_native.dll";
      return LoadLibraryByName(libraryName);
    }

    IntPtr DllLoadUtils.LoadLibraryNoSuffix (string fileName) {
      string libraryName = fileName + ".dll";
      return LoadLibraryByName(libraryName);
    }
  }

  /// <summary>Native library loader for Linux/Unix processes using <c>dlopen</c>.</summary>
  internal class DllLoadUtilsUnix : DllLoadUtils {

    [DllImport ("libdl.so.2", ExactSpelling = true)]
    private static extern IntPtr dlopen (String fileName, int flags);

    [DllImport ("libdl.so.2", ExactSpelling = true)]
    private static extern IntPtr dlsym (IntPtr handle, String symbol);

    [DllImport ("libdl.so.2", ExactSpelling = true)]
    private static extern int dlclose (IntPtr handle);

    [DllImport ("libdl.so.2", ExactSpelling = true)]
    private static extern IntPtr dlerror ();

    // RTLD_NOW (2): resolve all symbols immediately; dlopen fails if any symbol is missing.
    const int RTLD_NOW = 2;

    // Keep one preloaded dependency alive for Unity editor/player native plugin stability.
    // Tracks the exact preload target so path/name changes refresh the native handle.
    private static string preloadedLibraryKey = "";
    // Retains ownership of the preloaded dependency for the lifetime of the Unix loader.
    private static NativeLibraryHandle preloadedLibraryHandle = null;
    private static readonly object preloadMutex = new object();
    /// <summary>Preload a configured dependency once per absolute path/name pair.</summary>
    void CheckPreloadLibraries(GlobalVariables.Snapshot settings)
    {
      string preloadKey = settings.AbsolutePath + "|" + settings.PreloadLibraryName;
      if (settings.PreloadLibraryName == "")
      {
        return;
      }

      lock (preloadMutex)
      {
        if (preloadedLibraryKey == preloadKey)
        {
            return;
        }

        Ros2csLogger.GetInstance().LogDebug(() => "Preloading " + settings.PreloadLibraryName);
        IntPtr libPtr = Load(settings.PreloadLibraryName, settings.AbsolutePath);
        NativeLibraryHandle newHandle = NativeLibraryHandle.FromHandle(this, libPtr);
        NativeLibraryHandle oldHandle = preloadedLibraryHandle;
        preloadedLibraryHandle = newHandle;
        preloadedLibraryKey = preloadKey;

        Ros2csLogger.GetInstance().LogDebug(() => "Preloading " + settings.PreloadLibraryName + " successful.");

        if (oldHandle != null)
        {
          oldHandle.Dispose();
        }
      }
    }

    public void FreeLibrary (IntPtr handle) {
      if (handle != IntPtr.Zero) {
        dlclose (handle);
      }
    }

    public IntPtr GetProcAddress (IntPtr dllHandle, string name) {
      // clear previous errors if any
      dlerror ();
      var res = dlsym (dllHandle, name);
      var errPtr = dlerror ();
      if (errPtr != IntPtr.Zero) {
        throw new EntryPointNotFoundException (name + ": " + Marshal.PtrToStringAnsi (errPtr));
      }
      return res;
    }

    private IntPtr Load(string libraryFileName, string absolutePath) {
      string libraryPath = absolutePath + libraryFileName;
      string dlopenSearchString = libraryPath;
      Ros2csLogger.GetInstance().LogDebug(() => "Loading lib: " + dlopenSearchString);
      IntPtr ptr = dlopen(dlopenSearchString, RTLD_NOW);
      if (ptr == IntPtr.Zero) {
        if (!String.IsNullOrEmpty(absolutePath)) {
          // Fallback - look for library in default paths
          var errPtr = dlerror ();
          Ros2csLogger.GetInstance().LogDebug(() => "Could not find " + dlopenSearchString + ": " + Marshal.PtrToStringAnsi (errPtr) + ". Fallback to " + libraryFileName);
          dlopenSearchString = libraryFileName;
          ptr = dlopen(dlopenSearchString, RTLD_NOW);
        }
      }      
      if (ptr == IntPtr.Zero) {
        var errPtr = dlerror ();
        string detail = Marshal.PtrToStringAnsi (errPtr);
        if (!String.IsNullOrEmpty(detail)) {
          throw new UnsatisfiedLinkError(dlopenSearchString + ": " + detail);
        }
        throw new UnsatisfiedLinkError(dlopenSearchString);
      }
      Ros2csLogger.GetInstance().LogDebug(() => "Loaded library: " + dlopenSearchString);
      return ptr;
    }

    private IntPtr LoadLibraryByName(string libraryFileName) {
      GlobalVariables.Snapshot settings = GlobalVariables.GetSnapshot();
      if (settings.PreloadLibrary)
        CheckPreloadLibraries(settings);
      return Load(libraryFileName, settings.AbsolutePath);
    }

    public IntPtr LoadLibrary(string fileName) {
      string libraryName = "lib" + fileName + "_native.so";
      return LoadLibraryByName(libraryName);
    }

    public IntPtr LoadLibraryNoSuffix(string fileName) {
      string libraryName = "lib" + fileName + ".so";
      return LoadLibraryByName(libraryName);
    }
  }

  /// <summary>Native library loader for macOS processes using <c>dlopen</c>.</summary>
  /// <remarks>
  /// Kept separate from <see cref="DllLoadUtilsUnix"/> because the P/Invoke library name
  /// and native library suffix differ, and keeping each class explicit makes future
  /// platform-specific loader changes localized.
  /// </remarks>
  internal class DllLoadUtilsMacOSX : DllLoadUtils {

    [DllImport ("libdl.dylib", ExactSpelling = true)]
    private static extern IntPtr dlopen (String fileName, int flags);

    [DllImport ("libdl.dylib", ExactSpelling = true)]
    private static extern IntPtr dlsym (IntPtr handle, String symbol);

    [DllImport ("libdl.dylib", ExactSpelling = true)]
    private static extern int dlclose (IntPtr handle);

    [DllImport ("libdl.dylib", ExactSpelling = true)]
    private static extern IntPtr dlerror ();

    // RTLD_NOW (2): resolve all symbols immediately; dlopen fails if any symbol is missing.
    const int RTLD_NOW = 2;
    private static string preloadedLibraryKey = "";
    private static NativeLibraryHandle preloadedLibraryHandle = null;
    private static readonly object preloadMutex = new object();

    /// <summary>Preload a configured dependency once per absolute path/name pair.</summary>
    private void CheckPreloadLibraries(GlobalVariables.Snapshot settings)
    {
      string preloadKey = settings.AbsolutePath + "|" + settings.PreloadLibraryName;
      if (settings.PreloadLibraryName == "")
      {
        return;
      }

      lock (preloadMutex)
      {
        if (preloadedLibraryKey == preloadKey)
        {
          return;
        }

        Ros2csLogger.GetInstance().LogDebug(() => "Preloading " + settings.PreloadLibraryName);
        IntPtr libPtr = Load(settings.PreloadLibraryName, settings.AbsolutePath);
        NativeLibraryHandle newHandle = NativeLibraryHandle.FromHandle(this, libPtr);
        NativeLibraryHandle oldHandle = preloadedLibraryHandle;
        preloadedLibraryHandle = newHandle;
        preloadedLibraryKey = preloadKey;

        Ros2csLogger.GetInstance().LogDebug(() => "Preloading " + settings.PreloadLibraryName + " successful.");

        if (oldHandle != null)
        {
          oldHandle.Dispose();
        }
      }
    }

    public void FreeLibrary (IntPtr handle) {
      if (handle != IntPtr.Zero) {
        dlclose (handle);
      }
    }

    public IntPtr GetProcAddress (IntPtr dllHandle, string name) {
      // clear previous errors if any
      dlerror ();
      var res = dlsym (dllHandle, name);
      var errPtr = dlerror ();
      if (errPtr != IntPtr.Zero) {
        throw new EntryPointNotFoundException (name + ": " + Marshal.PtrToStringAnsi (errPtr));
      }
      return res;
    }

    private IntPtr Load(string libraryFileName, string absolutePath) {
      string libraryPath = absolutePath + libraryFileName;
      string dlopenSearchString = libraryPath;
      Ros2csLogger.GetInstance().LogDebug(() => "Loading lib: " + dlopenSearchString);
      IntPtr ptr = dlopen(dlopenSearchString, RTLD_NOW);
      if (ptr == IntPtr.Zero) {
        if (!String.IsNullOrEmpty(absolutePath)) {
          var errPtr = dlerror ();
          Ros2csLogger.GetInstance().LogDebug(() => "Could not find " + dlopenSearchString + ": " + Marshal.PtrToStringAnsi (errPtr) + ". Fallback to " + libraryFileName);
          dlopenSearchString = libraryFileName;
          ptr = dlopen(dlopenSearchString, RTLD_NOW);
        }
      }
      if (ptr == IntPtr.Zero) {
        var errPtr = dlerror ();
        string detail = Marshal.PtrToStringAnsi (errPtr);
        if (!String.IsNullOrEmpty(detail)) {
          throw new UnsatisfiedLinkError(dlopenSearchString + ": " + detail);
        }
        throw new UnsatisfiedLinkError(dlopenSearchString);
      }
      Ros2csLogger.GetInstance().LogDebug(() => "Loaded library: " + dlopenSearchString);
      return ptr;
    }

    private IntPtr LoadLibraryByName(string libraryFileName) {
      GlobalVariables.Snapshot settings = GlobalVariables.GetSnapshot();
      if (settings.PreloadLibrary)
        CheckPreloadLibraries(settings);
      return Load(libraryFileName, settings.AbsolutePath);
    }

    public IntPtr LoadLibrary (string fileName) {
      string libraryName = "lib" + fileName + "_native.dylib";
      return LoadLibraryByName(libraryName);
    }

    public IntPtr LoadLibraryNoSuffix (string fileName) {
      string libraryName = "lib" + fileName + ".dylib";
      return LoadLibraryByName(libraryName);
    }
  }
}
