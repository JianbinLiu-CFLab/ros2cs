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

// Based on http://dimitry-i.blogspot.com.es/2013/01/mononet-how-to-dynamically-load-native.html

using System;
using System.ComponentModel;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading;

namespace ROS2
{
  public class GlobalVariables {
    private static Snapshot settings = new Snapshot(false, "", "");
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
          SetSnapshot(new Snapshot(value, current.PreloadLibraryName, current.AbsolutePath));
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
          SetSnapshot(new Snapshot(current.PreloadLibrary, value, current.AbsolutePath));
        }
      }
    }

    /// <summary>Optional absolute search path prepended before falling back to platform defaults.</summary>
    public static string absolutePath
    {
      get { return GetSnapshot().AbsolutePath; }
      set
      {
        lock (settingsMutex)
        {
          Snapshot current = GetSnapshot();
          SetSnapshot(new Snapshot(current.PreloadLibrary, current.PreloadLibraryName, value));
        }
      }
    }

    internal sealed class Snapshot
    {
      internal readonly bool PreloadLibrary;
      internal readonly string PreloadLibraryName;
      internal readonly string AbsolutePath;

      internal Snapshot(bool preloadLibrary, string preloadLibraryName, string absolutePath)
      {
        PreloadLibrary = preloadLibrary;
        PreloadLibraryName = preloadLibraryName ?? "";
        AbsolutePath = absolutePath ?? "";
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
        SetSnapshot(new Snapshot(preloadLibrary, preloadLibraryName, absolutePath));
      }
    }

    private static void SetSnapshot(Snapshot snapshot)
    {
      Volatile.Write(ref settings, snapshot);
    }
  }

  public enum Platform {
    Unix,
    MacOSX,
    WindowsDesktop,
    UWP,
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

  public interface DllLoadUtils {
    IntPtr LoadLibrary (string fileName);
    IntPtr LoadLibraryNoSuffix (string fileName);
    void FreeLibrary (IntPtr handle);
    IntPtr GetProcAddress (IntPtr dllHandle, string name);
  }

  /// <summary>Owns a loaded native library and releases it when disposed.</summary>
  /// <remarks>Keeps native libraries alive while delegates created from them remain reachable.</remarks>
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

    /// <summary>Shared dispose path used by explicit disposal and the finalizer.</summary>
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
          if (handle != IntPtr.Zero)
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

  public class DllLoadUtilsWindowsDesktop : DllLoadUtils {

    [DllImport ("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibrary (string fileName);

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
      IntPtr ptr = LoadLibrary(libraryName);
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
        IntPtr ptr = LoadLibrary(libraryPath);
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

  internal class DllLoadUtilsUnix : DllLoadUtils {

    [DllImport ("libdl.so.2", ExactSpelling = true)]
    private static extern IntPtr dlopen (String fileName, int flags);

    [DllImport ("libdl.so.2", ExactSpelling = true)]
    private static extern IntPtr dlsym (IntPtr handle, String symbol);

    [DllImport ("libdl.so.2", ExactSpelling = true)]
    private static extern int dlclose (IntPtr handle);

    [DllImport ("libdl.so.2", ExactSpelling = true)]
    private static extern IntPtr dlerror ();

    const int RTLD_NOW = 0x00002;

    //TODO (adamdbrw) Somewhat hacky solution to open (and dereference) the problematic library
    //that otherwise causes crashes in Unity Editor.
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

  internal class DllLoadUtilsMacOSX : DllLoadUtils {

    [DllImport ("libdl.dylib", ExactSpelling = true)]
    private static extern IntPtr dlopen (String fileName, int flags);

    [DllImport ("libdl.dylib", ExactSpelling = true)]
    private static extern IntPtr dlsym (IntPtr handle, String symbol);

    [DllImport ("libdl.dylib", ExactSpelling = true)]
    private static extern int dlclose (IntPtr handle);

    [DllImport ("libdl.dylib", ExactSpelling = true)]
    private static extern IntPtr dlerror ();

    const int RTLD_NOW = 2;
    private static string preloadedLibraryKey = "";
    private static NativeLibraryHandle preloadedLibraryHandle = null;
    private static readonly object preloadMutex = new object();

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
