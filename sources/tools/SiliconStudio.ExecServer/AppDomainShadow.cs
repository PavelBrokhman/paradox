// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using SiliconStudio.Core;

namespace SiliconStudio.ExecServer
{
    /// <summary>
    /// A AppDomain container for managing shadow copy AppDomain that is working with native dlls.
    /// </summary>
    internal class AppDomainShadow : MarshalByRefObject, IDisposable
    {
        // NOTE: This keys should not be changed unless changing them also in the ExecServer.
        // They are used when multiple appdomain are sharing the same console
        private const string AppDomainLogToActionKey = "AppDomainLogToAction";

        private const string CacheFolder = ".shadow";

        private readonly object singletonLock = new object();

        private readonly string applicationPath;

        private readonly string[] nativeDllsPathOrFolderList;

        private readonly string appDomainName;

        private readonly string mainAssemblyPath;

        private bool isDllImportShadowCopy;

        private AppDomain appDomain;

        private AssemblyLoaderCallback appDomainCallback;

        private readonly List<FileLoaded> filesLoaded;

        private bool isRunning;

        private bool isUpToDate = true;

        private DateTime lastRunTime; 

        /// <summary>
        /// Initializes a new instance of the <see cref="AppDomainShadow" /> class.
        /// </summary>
        /// <param name="appDomainName">Name of the application domain.</param>
        /// <param name="mainAssemblyPath">The main assembly path.</param>
        /// <param name="nativeDllsPathOrFolderList">An array of folders path (containing only native dlls) or directly a specific path to a dll.</param>
        /// <exception cref="System.ArgumentNullException">mainAssemblyPath</exception>
        /// <exception cref="System.InvalidOperationException">If the assembly does not exist</exception>
        public AppDomainShadow(string appDomainName, string mainAssemblyPath, params string[] nativeDllsPathOrFolderList)
        {
            if (mainAssemblyPath == null) throw new ArgumentNullException("mainAssemblyPath");
            if (nativeDllsPathOrFolderList == null) throw new ArgumentNullException("nativeDllsPathOrFolderList");
            if (!File.Exists(mainAssemblyPath)) throw new InvalidOperationException(string.Format("Assembly [{0}] does not exist", mainAssemblyPath));

            this.appDomainName = appDomainName;
            this.mainAssemblyPath = mainAssemblyPath;
            this.nativeDllsPathOrFolderList = nativeDllsPathOrFolderList;
            applicationPath = Path.GetDirectoryName(mainAssemblyPath);
            filesLoaded = new List<FileLoaded>();
            CreateAppDomain();
            lastRunTime = DateTime.Now;
        }

        /// <summary>
        /// Gets the name.
        /// </summary>
        /// <value>The name.</value>
        public string Name
        {
            get
            {
                return appDomainName;
            }
        }


        /// <summary>
        /// Gets the application domain managed by this container.
        /// </summary>
        /// <value>The application domain.</value>
        public AppDomain AppDomain
        {
            get
            {
                return appDomain;
            }
        }

        public DateTime LastRunTime
        {
            get
            {
                return lastRunTime;
            }
        }

        /// <summary>
        /// Tries to take the ownership of this container to run an exe/method from the app domain.
        /// </summary>
        /// <returns><c>true</c> if ownership was successfull (you can then use <see cref="Run"/> method), <c>false</c> otherwise.</returns>
        public bool TryLock()
        {
            lock (singletonLock)
            {
                if (!isRunning)
                {
                    isRunning = true;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Determines whether all assemblies and native dlls have not changed.
        /// </summary>
        /// <returns><c>true</c> if the appdomain is up-to-date; otherwise, <c>false</c>.</returns>
        public bool IsUpToDate()
        {
            if (isUpToDate)
            {
                var filesToCheck = new List<FileLoaded>();
                lock (filesLoaded)
                {
                    filesToCheck.AddRange(filesLoaded);
                }

                foreach (var fileLoaded in filesToCheck)
                {
                    if (!fileLoaded.IsUpToDate())
                    {
                        Console.WriteLine("Dll File changed: {0}", fileLoaded.FilePath);

                        isUpToDate = false;
                        break;
                    }
                }
            }

            return isUpToDate;
        }

        /// <summary>
        /// Runs the main entry point method passing arguments to it
        /// </summary>
        /// <param name="args">The arguments.</param>
        /// <param name="logger">The logger.</param>
        /// <returns>System.Int32.</returns>
        /// <exception cref="System.InvalidOperationException">Must call TryLock before calling this method</exception>
        public int Run(string[] args, IServerLogger logger)
        {
            if (!isRunning)
            {
                throw new InvalidOperationException("Must call TryLock before calling this method");
            }

            try
            {
                lastRunTime = DateTime.Now;
                using (var appDomainRedirectLogger = new AppDomainRedirectLogger(logger))
                {
                    appDomainCallback.Logger = appDomainRedirectLogger;
                    appDomainCallback.Arguments = args;
                    appDomain.DoCallBack(appDomainCallback.Run);
                    var result = appDomainCallback.Result;

                    //var result = appDomain.ExecuteAssembly(mainAssemblyPath, args);
                    Console.WriteLine("Return result: {0}", result);
                    return result;
                }
            }
            catch (Exception exception)
            {
                logger.OnLog(string.Format("Unexpected exception: {0}", exception), ConsoleColor.Red);
                return 1;
            }
            finally
            {
                lastRunTime = DateTime.Now;
            }
        }

        public void EndRun()
        {
            lock (singletonLock)
            {
                isRunning = false;
            }
        }

        private void AssemblyLoaded(string location)
        {
            if (!location.StartsWith(applicationPath, true, CultureInfo.InvariantCulture))
            {
                return;
            }

            if (!isDllImportShadowCopy)
            {
                var cachePath = GetRootCachePath(location);
                if (cachePath != null)
                {
                    ShadowCopyNativeDlls(cachePath.FullName);
                    isDllImportShadowCopy = true;
                }
            }

            // Register the assembly in order to unload this appdomain if it is no longer relevant
            var assemblyFileName = Path.GetFileName(location);
            RegisterFileLoaded(new FileInfo(Path.Combine(applicationPath, assemblyFileName)));
        }

        private void ShadowCopyNativeDlls(string cachePath)
        {
            // In this method, we copy all native dlls to a subfolder under the shadow cache
            // Each dll has a hash computed from its name and last timestamp
            // This hash is used to create a directory from which the dlls will be stored
            // Later in the AppDomain running and use the NativeLibrary.PreLoadLibrary()
            // The method in PreLoadLibrary will use the dll that have been copied by this instance

            // Get the shadow folder for native dlls
            var nativeDllShadowRootFolder = Path.Combine(cachePath, "native");
            Directory.CreateDirectory(nativeDllShadowRootFolder);

            // Copy check any new native dlls
            var appPath = Path.GetDirectoryName(mainAssemblyPath);

            foreach (var nativeDllFolderOrPath in nativeDllsPathOrFolderList)
            {
                var absolutePathOrFolder = Path.Combine(appPath, nativeDllFolderOrPath);

                // Native dll files to load
                var files = File.Exists(absolutePathOrFolder) ? 
                    new[] { new FileInfo(absolutePathOrFolder) } : 
                    new DirectoryInfo(absolutePathOrFolder).EnumerateFiles("*.dll");

                var hashBuffer = new MemoryStream(new byte[1024]);
                foreach (var file in files)
                {
                    var fileHash = GetFileHash(hashBuffer, file);
                    var shadowDllPath = Path.Combine(nativeDllShadowRootFolder, fileHash, file.Name);
                    if (!File.Exists(shadowDllPath))
                    {
                        SafeCopy(file.FullName, shadowDllPath);
                    }

                    // Register our native path
                    NativeLibraryInternal.SetShadowPathForNativeDll(appDomain, file.Name, Path.GetDirectoryName(shadowDllPath));

                    // Register this dll 
                    RegisterFileLoaded(file);
                }
            }
        }

        private DirectoryInfo GetRootCachePath(string currentPath)
        {
            var info = new DirectoryInfo(currentPath);
            while (info != null)
            {
                if (String.Compare(info.Name, "dl3", StringComparison.InvariantCultureIgnoreCase) == 0)
                {
                    return info;
                }
                info = info.Parent;
            }
            return null;
        }

        private static string Hash(byte[] buffer)
        {
            uint hash = 2166136261;
            for (int i = 0; i < buffer.Length; i++)
            {
                hash = hash ^ buffer[i];
                hash = hash * 16777619;
            }
            return hash.ToString("x");
        }

        private static string GetFileHash(MemoryStream hashBuffer, FileInfo file)
        {
            hashBuffer.Position = 0;
            var nameAsBytes = Encoding.UTF8.GetBytes(file.FullName);
            hashBuffer.Write(nameAsBytes, 0, nameAsBytes.Length);
            var timeAsBytes = BitConverter.GetBytes(file.LastWriteTimeUtc.Ticks);
            hashBuffer.Write(timeAsBytes, 0, timeAsBytes.Length);
            return Hash(hashBuffer.ToArray());
        }

        private void RegisterFileLoaded(FileInfo file)
        {
            lock (filesLoaded)
            {
                filesLoaded.Add(new FileLoaded(file));
            }
        }

        private static void SafeCopy(string sourceFilePath, string destinationFilePath)
        {
            var fileName = Path.GetFileName(sourceFilePath);

            var destinationDirectory = Path.GetDirectoryName(destinationFilePath);

            // Case where the directory exists but the file not (not expected but got this case, will have to check why)
            if (Directory.Exists(destinationDirectory) && !File.Exists(destinationFilePath))
            {
                try
                {
                    File.Copy(sourceFilePath, destinationFilePath, true);
                    return;
                }
                catch (IOException)
                {
                }
            }

            var destinationParentDirectory = Directory.GetParent(destinationDirectory).FullName;
            var destinationTempDirectory = Path.Combine(destinationParentDirectory, Guid.NewGuid().ToString());
            Directory.CreateDirectory(destinationTempDirectory);
            bool tempDirDeleted = false;
            try
            {
                File.Copy(sourceFilePath, Path.Combine(destinationTempDirectory, fileName), true);
                try
                {
                    if (!Directory.Exists(destinationDirectory))
                    {
                        Directory.Move(destinationTempDirectory, destinationDirectory);
                        tempDirDeleted = true;
                    }
                }
                catch (IOException)
                {
                }
            }
            catch (IOException)
            {
            }
            finally
            {
                if (!tempDirDeleted)
                {
                    try
                    {
                        Directory.Delete(destinationTempDirectory, true);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        private void CreateAppDomain()
        {
            var appDomainSetup = new AppDomainSetup
            {
                ApplicationBase = applicationPath,
                ShadowCopyFiles = "true",
                CachePath = Path.Combine(applicationPath, CacheFolder),
            };

            // Create AppDomain
            appDomain = AppDomain.CreateDomain(appDomainName, AppDomain.CurrentDomain.Evidence, appDomainSetup);

            // Create appDomain Callback
            appDomainCallback = new AssemblyLoaderCallback(AssemblyLoaded, mainAssemblyPath);

            // Install the appDomainCallback to prepare the new app domain
            appDomain.DoCallBack(appDomainCallback.RegisterAssemblyLoad);
        }

        private struct FileLoaded
        {
            public FileLoaded(FileInfo file)
            {
                FilePath = file.FullName;
                lastWriteTime = file.LastWriteTimeUtc;
            }

            public readonly string FilePath;

            private readonly DateTime lastWriteTime;

            public bool IsUpToDate()
            {
                if (!File.Exists(FilePath))
                {
                    return false;
                }

                try
                {
                    var currentTime = new FileInfo(FilePath).LastWriteTimeUtc;
                    return currentTime == lastWriteTime;
                }
                catch (IOException)
                {
                }
                return false;
            }
        }

        [Serializable]
        private class AssemblyLoaderCallback
        {
            private const string AppDomainExecServerEntryAssemblyKey = "AppDomainExecServerEntryAssembly";
            private readonly Action<string> callback;

            private readonly string executablePath;

            public AssemblyLoaderCallback(Action<string> callback, string executablePath)
            {
                this.callback = callback;
                this.executablePath = executablePath;
            }

            public IServerLogger Logger { get; set; }

            public string[] Arguments { get; set; }

            public int Result { get; private set; }

            public void RegisterAssemblyLoad()
            {
                var currentDomain = AppDomain.CurrentDomain;

                // NOTE: This part is important to have native dlls resolved correctly by Mixed Assemblies
                var path = Environment.GetEnvironmentVariable("PATH");
                if (!path.Contains(currentDomain.BaseDirectory))
                {
                    path = currentDomain.BaseDirectory + ";" + path;
                    Environment.SetEnvironmentVariable("PATH", path);
                }

                // This method is executed in the child application domain
                currentDomain.AssemblyLoad += AppDomainOnAssemblyLoad;

                // Preload main entry point assembly
                var mainAssembly = currentDomain.Load(Path.GetFileNameWithoutExtension(executablePath));
                currentDomain.SetData(AppDomainExecServerEntryAssemblyKey, mainAssembly);
            }

            public void Run()
            {
                var currentDomain = AppDomain.CurrentDomain;
                currentDomain.SetData(AppDomainLogToActionKey, new Action<string, ConsoleColor>((text, color) => Logger.OnLog(text, color)));
                var assembly = (Assembly)currentDomain.GetData(AppDomainExecServerEntryAssemblyKey);
                Result = Convert.ToInt32(assembly.EntryPoint.Invoke(null, new object[] { Arguments }));

                // Force a GC after the process is finished
                GC.Collect(2, GCCollectionMode.Forced);
            }

            private void AppDomainOnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
            {
                var assembly = args.LoadedAssembly;
                if (!assembly.IsDynamic)
                {
                    // This method will be executed in the ExecServer application domain
                    callback(assembly.Location);
                }
            }
        }

        private sealed class AppDomainRedirectLogger : MarshalByRefObject, IServerLogger, IDisposable
        {
            private IServerLogger logger;

            public AppDomainRedirectLogger(IServerLogger logger)
            {
                this.logger = logger;
            }

            public void OnLog(string text, ConsoleColor color)
            {
                var localLogger = logger;
                if (localLogger != null)
                {
                    Task.Factory.StartNew(() => localLogger.OnLog(text, color));
                }
            }

            public void Dispose()
            {
                logger = null;
            }
        }

        public void Dispose()
        {
            System.AppDomain.Unload(appDomain);
            Console.WriteLine("AppDomain {0} Disposed", appDomainName);
        }
    }
}