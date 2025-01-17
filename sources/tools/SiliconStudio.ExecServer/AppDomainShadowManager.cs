// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace SiliconStudio.ExecServer
{
    /// <summary>
    /// Manages <see cref="AppDomainShadow"/>.
    /// </summary>
    internal class AppDomainShadowManager : IDisposable
    {
        private readonly List<AppDomainShadow> appDomainShadows = new List<AppDomainShadow>();

        private readonly string mainAssemblyPath;

        private readonly int maximumConcurrentAppDomain;

        private readonly List<string> nativeDllsPathOrFolderList;

        /// <summary>
        /// Initializes a new instance of the <see cref="AppDomainShadowManager" /> class.
        /// </summary>
        /// <param name="mainAssemblyPath">The main assembly path.</param>
        /// <param name="maximumConcurrentAppDomain">The maximum concurrent application domain.</param>
        /// <param name="nativeDllsPathOrFolderList">An array of folders path (containing only native dlls) or directly a specific path to a dll.</param>
        /// <exception cref="System.ArgumentNullException">mainAssemblyPath</exception>
        /// <exception cref="System.InvalidOperationException">If the assembly does not exist</exception>
        public AppDomainShadowManager(string mainAssemblyPath, IEnumerable<string> nativeDllsPathOrFolderList, int maximumConcurrentAppDomain = 2)
        {
            if (mainAssemblyPath == null) throw new ArgumentNullException("mainAssemblyPath");
            if (!File.Exists(mainAssemblyPath)) throw new InvalidOperationException(string.Format("Assembly [{0}] does not exist", mainAssemblyPath));
            if (maximumConcurrentAppDomain < 1) throw new ArgumentOutOfRangeException("maximumConcurrentAppDomain", "Parameter must be >= 1");
            this.mainAssemblyPath = mainAssemblyPath;
            this.maximumConcurrentAppDomain = maximumConcurrentAppDomain;
            this.nativeDllsPathOrFolderList = new List<string>(nativeDllsPathOrFolderList);
        }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is caching application domain.
        /// </summary>
        /// <value><c>true</c> if this instance is caching application domain; otherwise, <c>false</c>.</value>
        public bool IsCachingAppDomain { get; set; }

        /// <summary>
        /// Runs the assembly with the specified arguments.xit
        /// </summary>
        /// <param name="args">The main arguments.</param>
        /// <param name="logger">The logger.</param>
        /// <returns>System.Int32.</returns>
        public int Run(string[] args, IServerLogger logger)
        {
            AppDomainShadow shadowDomain = null;
            try
            {
                shadowDomain = GetOrNew(IsCachingAppDomain);
                return shadowDomain.Run(args, logger);
            }
            finally
            {
                if (shadowDomain != null)
                {
                    shadowDomain.EndRun();
                    if (!IsCachingAppDomain)
                    {
                        shadowDomain.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Recycles any instance that are no longer in sync with original dlls
        /// </summary>
        public void Recycle(TimeSpan limitTimeAlive)
        {
            bool hasDisposed = false;
            lock (appDomainShadows)
            {
                for (int i = appDomainShadows.Count - 1; i >= 0; i--)
                {
                    var appDomainShadow = appDomainShadows[i];
                    var deltaTime = DateTime.Now - appDomainShadow.LastRunTime;
                    var isAppDomainExpired = deltaTime > limitTimeAlive;

                    if (!appDomainShadow.IsUpToDate() || isAppDomainExpired)
                    {
                        // Try to take the lock on the appdomain to dispose (may be running)
                        if (appDomainShadow.TryLock())
                        {
                            var reason =
                                isAppDomainExpired
                                    ? string.Format("Not used after {0}s", (int)deltaTime.TotalSeconds)
                                    : "Assembly files changed";

                            Console.WriteLine("Recycling AppDomain {0} (Reason: {1})", appDomainShadow.Name, reason);
                            appDomainShadow.Dispose();
                            appDomainShadows.RemoveAt(i);
                            hasDisposed = true;
                        }
                    }
                }
            }

            // Make sure we perform a collection of the app domain
            if (hasDisposed)
            {
                GC.Collect(2, GCCollectionMode.Forced);
            }
        }

        /// <summary>
        /// Get or create a new <see cref="AppDomainShadow"/>.
        /// </summary>
        /// <returns></returns>
        private AppDomainShadow GetOrNew(bool useCache)
        {
            lock (appDomainShadows)
            {
                var newAppDomainName = Path.GetFileNameWithoutExtension(mainAssemblyPath) + "#" + appDomainShadows.Count;
                while (true)
                {
                    foreach (var appDomainShadow in appDomainShadows)
                    {
                        if (appDomainShadow.TryLock())
                        {
                            Console.WriteLine("Use cached AppDomain {0}", appDomainShadow.Name);
                            return appDomainShadow;
                        }
                    }
                    
                    if (appDomainShadows.Count < maximumConcurrentAppDomain)
                    {
                        break;
                    }
                    else
                    {
                        // We should better use notify instead
                        Thread.Sleep(200);
                    }
                }

                Console.WriteLine("Create new AppDomain {0}", newAppDomainName);
                var newAppDomain = new AppDomainShadow(newAppDomainName, mainAssemblyPath, nativeDllsPathOrFolderList.ToArray());
                newAppDomain.TryLock();

                if (useCache)
                {
                    appDomainShadows.Add(newAppDomain);
                }

                return newAppDomain;
            }
        }

        /// <summary>
        /// Dispose the manager and wait that all app domain are finished.
        /// </summary>
        public void Dispose()
        {
            lock (appDomainShadows)
            {
                while (true)
                {
                    for (int i = appDomainShadows.Count - 1; i >= 0; i--)
                    {
                        var appDomainShadow = appDomainShadows[i];
                        if (appDomainShadow.TryLock())
                        {
                            appDomainShadows.RemoveAt(i);
                            appDomainShadow.Dispose();
                        }
                    }
                    if (appDomainShadows.Count == 0)
                    {
                        break;
                    }

                    // Active wait, not ideal, we should better have an event based locking mechanism
                    Thread.Sleep(500);
                }
            }
        }
    }
}