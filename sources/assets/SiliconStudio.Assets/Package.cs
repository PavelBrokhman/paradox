﻿// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Build.Utilities;

using SharpYaml;
using SiliconStudio.Assets.Analysis;
using SiliconStudio.Assets.Diagnostics;
using SiliconStudio.Assets.Templates;
using SiliconStudio.Core;
using SiliconStudio.Core.Diagnostics;
using SiliconStudio.Core.IO;
using SiliconStudio.Core.Reflection;
using SiliconStudio.Core.Storage;

using Logger = SiliconStudio.Core.Diagnostics.Logger;

namespace SiliconStudio.Assets
{
    public enum PackageState
    {
        /// <summary>
        /// Package has been deserialized. References and assets are not ready.
        /// </summary>
        Raw,

        /// <summary>
        /// Dependencies have all been resolved and are also in <see cref="DependenciesReady"/> state.
        /// </summary>
        DependenciesReady,

        /// <summary>
        /// Package upgrade has been failed (either error or denied by user).
        /// Dependencies are ready, but not assets.
        /// Should be manually switched back to DependenciesReady to try upgrade again.
        /// </summary>
        UpgradeFailed,

        /// <summary>
        /// Assembly references and assets have all been loaded.
        /// </summary>
        AssetsReady,
    }

    /// <summary>
    /// A package managing assets.
    /// </summary>
    [DataContract("Package")]
    [AssetDescription(PackageFileExtension)]
    [DebuggerDisplay("Id: {Id}, Name: {Meta.Name}, Version: {Meta.Version}, Assets [{Assets.Count}]")]
    public sealed class Package : Asset, IFileSynchronizable
    {
        private readonly PackageAssetCollection assets;

        private readonly AssetItemCollection temporaryAssets;

        private readonly List<PackageReference> localDependencies;

        private readonly List<UDirectory> explicitFolders;

        private readonly List<PackageLoadedAssembly> loadedAssemblies;

        private readonly List<UFile> filesToDelete = new List<UFile>();

        private PackageSession session;

        private UFile packagePath;
        private bool isDirty;
        private Lazy<PackageSettings> settings;

        /// <summary>
        /// The file extension used for <see cref="Package"/>.
        /// </summary>
        public const string PackageFileExtension = ".pdxpkg";

        /// <summary>
        /// Occurs when an asset dirty changed occured.
        /// </summary>
        public event Action<Asset> AssetDirtyChanged;

        /// <summary>
        /// Initializes a new instance of the <see cref="Package"/> class.
        /// </summary>
        public Package()
        {
            localDependencies = new List<PackageReference>();
            temporaryAssets = new AssetItemCollection();
            assets = new PackageAssetCollection(this);
            explicitFolders = new List<UDirectory>();
            loadedAssemblies = new List<PackageLoadedAssembly>();
            Bundles = new BundleCollection(this);
            Meta = new PackageMeta();
            TemplateFolders = new List<TemplateFolder>();
            Templates = new List<TemplateDescription>();
            Profiles = new PackageProfileCollection();
            IsDirty = true;
            settings = new Lazy<PackageSettings>(() => new PackageSettings(this));
        }

        /// <summary>
        /// Gets or sets a value indicating whether this package is a system package.
        /// </summary>
        /// <value><c>true</c> if this package is a system package; otherwise, <c>false</c>.</value>
        [DataMemberIgnore]
        public bool IsSystem { get; internal set; }

        /// <summary>
        /// Gets or sets the metadata associated with this package.
        /// </summary>
        /// <value>The meta.</value>
        [DataMember(10)]
        public PackageMeta Meta { get; set; }

        /// <summary>
        /// Gets the local package dependencies used by this package (only valid for local references). Global dependencies
        /// are defined through the <see cref="Meta"/> property in <see cref="PackageMeta.Dependencies"/> 
        /// </summary>
        /// <value>The package local dependencies.</value>
        [DataMember(30)]
        public List<PackageReference> LocalDependencies
        {
            get
            {
                return localDependencies;
            }
        }

        /// <summary>
        /// Gets the profiles.
        /// </summary>
        /// <value>The profiles.</value>
        [DataMember(50)]
        public PackageProfileCollection Profiles { get; private set; }

        /// <summary>
        /// Gets or sets the list of folders that are explicitly created but contains no assets.
        /// </summary>
        [DataMember(70)]
        public List<UDirectory> ExplicitFolders
        {
            get
            {
                return explicitFolders;
            }
        }

        /// <summary>
        /// Gets the bundles defined for this package.
        /// </summary>
        /// <value>The bundles.</value>
        [DataMember(80)]
        public BundleCollection Bundles { get; private set; }

        /// <summary>
        /// Gets the template folders.
        /// </summary>
        /// <value>The template folders.</value>
        [DataMember(90)]
        public List<TemplateFolder> TemplateFolders { get; private set; }

        /// <summary>
        /// Gets the loaded templates from the <see cref="TemplateFolders"/>
        /// </summary>
        /// <value>The templates.</value>
        [DataMemberIgnore]
        public List<TemplateDescription> Templates { get; private set; }
        
        /// <summary>
        /// Gets the assets stored in this package.
        /// </summary>
        /// <value>The assets.</value>
        [DataMemberIgnore]
        public PackageAssetCollection Assets
        {
            get
            {
                return assets;
            }
        }

        /// <summary>
        /// Gets the temporary assets list loaded from disk before they are going into <see cref="Assets"/>.
        /// </summary>
        /// <value>The temporary assets.</value>
        [DataMemberIgnore]
        public AssetItemCollection TemporaryAssets
        {
            get
            {
                return temporaryAssets;
            }
        }

        /// <summary>
        /// Gets the path to the package file. May be null if the package was not loaded or saved.
        /// </summary>
        /// <value>The package path.</value>
        [DataMemberIgnore]
        public UFile FullPath
        {
            get
            {
                return packagePath;
            }
            set
            {
                SetPackagePath(value, true);
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this instance has been modified since last saving.
        /// </summary>
        /// <value><c>true</c> if this instance is dirty; otherwise, <c>false</c>.</value>
        [DataMemberIgnore]
        public bool IsDirty
        {
            get
            {
                return isDirty;
            }
            set
            {
                isDirty = value;
                OnAssetDirtyChanged(this);
            }
        }

        [DataMemberIgnore]
        public PackageState State { get; set; }

        /// <summary>
        /// Gets the top directory of this package on the local disk.
        /// </summary>
        /// <value>The top directory.</value>
        [DataMemberIgnore]
        public UDirectory RootDirectory
        {
            get
            {
                return FullPath != null ? FullPath.GetParent() : null;
            }
        }

        /// <summary>
        /// Gets the session.
        /// </summary>
        /// <value>The session.</value>
        /// <exception cref="System.InvalidOperationException">Cannot attach a package to more than one session</exception>
        [DataMemberIgnore]
        public PackageSession Session
        {
            get
            {
                return session;
            }
            internal set
            {
                if (value != null && session != null && !ReferenceEquals(session, value))
                {
                    throw new InvalidOperationException("Cannot attach a package to more than one session");
                }
                session = value;
                IsIdLocked = (session != null);
            }
        }

        /// <summary>
        /// Gets the package settings. Usually stored in a .user file alongside the package. Lazily loaded on first time.
        /// </summary>
        /// <value>
        /// The package settings.
        /// </value>
        [DataMemberIgnore]
        public PackageSettings Settings
        {
            get { return settings.Value; }
        }

        /// <summary>
        /// Gets the list of assemblies loaded by this package.
        /// </summary>
        /// <value>
        /// The loaded assemblies.
        /// </value>
        [DataMemberIgnore]
        public List<PackageLoadedAssembly> LoadedAssemblies
        {
            get { return loadedAssemblies; }
        }

        /// <summary>
        /// Adds an exiting project to this package.
        /// </summary>
        /// <param name="pathToMsproj">The path to msproj.</param>
        /// <returns>LoggerResult.</returns>
        public LoggerResult AddExitingProject(UFile pathToMsproj)
        {
            var logger = new LoggerResult();
            AddExitingProject(pathToMsproj, logger);
            return logger;
        }

        /// <summary>
        /// Adds an exiting project to this package.
        /// </summary>
        /// <param name="pathToMsproj">The path to msproj.</param>
        /// <param name="logger">The logger.</param>
        public void AddExitingProject(UFile pathToMsproj, LoggerResult logger)
        {
            if (pathToMsproj == null) throw new ArgumentNullException("pathToMsproj");
            if (logger == null) throw new ArgumentNullException("logger");
            if (!pathToMsproj.IsAbsolute) throw new ArgumentException("Expecting relative path", "pathToMsproj");

            try
            {
                // Load a project without specifying a platform to make sure we get the correct platform type
                var msProject = VSProjectHelper.LoadProject(pathToMsproj, platform: "NoPlatform");
                try
                {

                    var projectType = VSProjectHelper.GetProjectTypeFromProject(msProject);
                    if (!projectType.HasValue)
                    {
                        logger.Error("This project is not a project created with the editor");
                    }
                    else
                    {
                        var platformType = VSProjectHelper.GetPlatformTypeFromProject(msProject) ?? PlatformType.Shared;

                        var projectReference = new ProjectReference()
                        {
                            Id = VSProjectHelper.GetProjectGuid(msProject),
                            Location = pathToMsproj.MakeRelative(RootDirectory),
                            Type = projectType.Value
                        };

                        // Add the ProjectReference only for the compatible profiles (same platform or no platform)
                        foreach (var profile in Profiles.Where(profile => platformType == profile.Platform))
                        {
                            profile.ProjectReferences.Add(projectReference);
                        }
                    }
                }
                finally
                {
                    msProject.ProjectCollection.UnloadAllProjects();
                    msProject.ProjectCollection.Dispose();
                }
            }
            catch (Exception ex)
            {
                logger.Error("Unexpected exception while loading project [{0}]", ex, pathToMsproj);
            }
        }

        internal UDirectory GetDefaultAssetFolder()
        {
            var sharedProfile = Profiles.FindSharedProfile();
            if (sharedProfile != null)
            {
                var folder = sharedProfile.AssetFolders.FirstOrDefault();
                if (folder != null && folder.Path != null)
                {
                    return folder.Path;
                }
            }

            return "Assets/" + PackageProfile.SharedName;
        }


        /// <summary>
        /// Deep clone this package.
        /// </summary>
        /// <param name="deepCloneAsset">if set to <c>true</c> assets will stored in this package will be also deeply cloned.</param>
        /// <returns>The package cloned.</returns>
        public Package Clone(bool deepCloneAsset)
        {
            // Use a new ShadowRegistry to copy override parameters
            // Clone this asset
            var package = (Package)AssetCloner.Clone(this); 
            package.FullPath = FullPath;
            foreach (var asset in Assets)
            {
                var newAsset = deepCloneAsset ? (Asset)AssetCloner.Clone(asset.Asset) : asset.Asset;
                var assetItem = new AssetItem(asset.Location, newAsset);
                package.Assets.Add(assetItem);
            }
            return package;
        }

        /// <summary>
        /// Sets the package path.
        /// </summary>
        /// <param name="newPath">The new path.</param>
        /// <param name="copyAssets">if set to <c>true</c> assets will be copied relatively to the new location.</param>
        public void SetPackagePath(UFile newPath, bool copyAssets = true)
        {
            var previousPath = packagePath;
            var previousRootDirectory = RootDirectory;
            packagePath = newPath;
            if (packagePath != null && !packagePath.IsAbsolute)
            {
                packagePath = UPath.Combine(Environment.CurrentDirectory, packagePath);
            }

            if (copyAssets && packagePath != previousPath)
            {
                // Update source folders
                var currentRootDirectory = RootDirectory;
                if (previousRootDirectory != null && currentRootDirectory != null)
                {
                    foreach (var profile in Profiles)
                    {
                        foreach (var sourceFolder in profile.AssetFolders)
                        {
                            if (sourceFolder.Path.IsAbsolute)
                            {
                                var relativePath = sourceFolder.Path.MakeRelative(previousRootDirectory);
                                sourceFolder.Path = UPath.Combine(currentRootDirectory, relativePath);
                            }
                        }
                    }
                }

                foreach (var asset in Assets)
                {
                    asset.IsDirty = true;
                }
                IsDirty = true;
            }
        }

        internal void OnAssetDirtyChanged(Asset asset)
        {
            Action<Asset> handler = AssetDirtyChanged;
            if (handler != null) handler(asset);
        }

        /// <summary>
        /// Saves this package and all dirty assets. See remarks.
        /// </summary>
        /// <param name="saveAllAssets">if set to <c>true</c> [save all assets].</param>
        /// <returns>LoggerResult.</returns>
        /// <remarks>When calling this method directly, it does not handle moving assets between packages. 
        /// Call <see cref="PackageSession.Save"/> instead.
        /// </remarks>
        public LoggerResult Save()
        {
            var result = new LoggerResult();
            Save(result);
            return result;
        }

        /// <summary>
        /// Saves this package and all dirty assets. See remarks.
        /// </summary>
        /// <param name="log">The log.</param>
        /// <exception cref="System.ArgumentNullException">log</exception>
        /// <remarks>When calling this method directly, it does not handle moving assets between packages.
        /// Call <see cref="PackageSession.Save" /> instead.</remarks>
        public void Save(ILogger log)
        {
            if (log == null) throw new ArgumentNullException("log");

            if (FullPath == null)
            {
                log.Error(this, null, AssetMessageCode.PackageCannotSave, "null");
                return;
            }

            // Use relative paths when saving
            var analysis = new PackageAnalysis(this, new PackageAnalysisParameters()
            {
                SetDirtyFlagOnAssetWhenFixingUFile = false,
                ConvertUPathTo = UPathType.Relative,
                IsProcessingUPaths = true
            });
            analysis.Run(log);

            try
            {
                // Update source folders
                UpdateSourceFolders();

                if (IsDirty)
                {
                    try
                    {
                        // Notifies the dependency manager that a package with the specified path is being saved
                        if (session != null && session.HasDependencyManager)
                        {
                            session.DependencyManager.AddFileBeingSaveDuringSessionSave(FullPath);
                        }

                        AssetSerializer.Save(FullPath, this);

                        IsDirty = false;
                    }
                    catch (Exception ex)
                    {
                        log.Error(this, null, AssetMessageCode.PackageCannotSave, ex, FullPath);
                        return;
                    }
                    
                    // Delete obsolete files
                    foreach (var file in filesToDelete)
                    {
                        if (File.Exists(file.FullPath))
                        {
                            try
                            {
                                File.Delete(file.FullPath);
                            }
                            catch (Exception ex)
                            {
                                log.Error(this, null, AssetMessageCode.AssetCannotDelete, ex, file.FullPath);
                            }
                        }
                    }
                    filesToDelete.Clear();
                }

                foreach (var asset in Assets)
                {
                    if (asset.IsDirty)
                    {
                        var assetPath = asset.FullPath;
                        try
                        {
                            // Notifies the dependency manager that an asset with the specified path is being saved
                            if (session != null && session.HasDependencyManager)
                            {
                                session.DependencyManager.AddFileBeingSaveDuringSessionSave(assetPath);
                            }

                            // Incject a copy of the base into the current asset when saving
                            var assetBase = asset.Asset.Base;
                            if (assetBase != null && !assetBase.IsRootImport)
                            {
                                var assetBaseItem = session != null ? session.FindAsset(assetBase.Id) : Assets.Find(assetBase.Id);
                                if (assetBaseItem != null)
                                {
                                    var newBase = (Asset)AssetCloner.Clone(assetBaseItem.Asset);
                                    newBase.Base = null;
                                    asset.Asset.Base = new AssetBase(asset.Asset.Base.Location, newBase);
                                }
                            }

                            AssetSerializer.Save(assetPath, asset.Asset);
                            asset.IsDirty = false;
                        }
                        catch (Exception ex)
                        {
                            log.Error(this, asset.ToReference(), AssetMessageCode.AssetCannotSave, ex, assetPath);
                        }
                    }
                }

                Assets.IsDirty = false;

                // Save properties like the Paradox version used
                PackageSessionHelper.SaveProperties(this);
            }
            finally
            {
                // Rollback all relative UFile to absolute paths
                analysis.Parameters.ConvertUPathTo = UPathType.Absolute;
                analysis.Run();
            }
        }

        /// <summary>
        /// Gets the package identifier from file.
        /// </summary>
        /// <param name="filePath">The file path.</param>
        /// <returns>Guid.</returns>
        /// <exception cref="System.ArgumentNullException">
        /// log
        /// or
        /// filePath
        /// </exception>
        public static Guid GetPackageIdFromFile(string filePath)
        {
            if (filePath == null) throw new ArgumentNullException("filePath");
            return AssetSerializer.Load<Package>(filePath).Id;
        }

        /// <summary>
        /// Loads only the package description but not assets or plugins.
        /// </summary>
        /// <param name="log">The log to receive error messages.</param>
        /// <param name="filePath">The file path.</param>
        /// <param name="loadParametersArg">The load parameters argument.</param>
        /// <returns>A package.</returns>
        /// <exception cref="System.ArgumentNullException">log
        /// or
        /// filePath</exception>
        public static Package Load(ILogger log, string filePath, PackageLoadParameters loadParametersArg = null)
        {
            var package = LoadRaw(log, filePath);
            if (package != null)
            {
                if (!package.LoadAssembliesAndAssets(log, loadParametersArg))
                    package = null;
            }

            return package;
        }

        /// <summary>
        /// Performs first part of the loading sequence, by deserializing the package but without processing anything yet.
        /// </summary>
        /// <param name="log">The log.</param>
        /// <param name="filePath">The file path.</param>
        /// <returns></returns>
        /// <exception cref="System.ArgumentNullException">
        /// log
        /// or
        /// filePath
        /// </exception>
        internal static Package LoadRaw(ILogger log, string filePath)
        {
            if (log == null) throw new ArgumentNullException("log");
            if (filePath == null) throw new ArgumentNullException("filePath");

            filePath = FileUtility.GetAbsolutePath(filePath);

            if (!File.Exists(filePath))
            {
                log.Error("Package file [{0}] was not found", filePath);
                return null;
            }

            try
            {
                var package = AssetSerializer.Load<Package>(filePath, log);
                package.FullPath = filePath;
                package.IsDirty = false;

                return package;
            }
            catch (Exception ex)
            {
                log.Error("Error while pre-loading package [{0}]", ex, filePath);
            }

            return null;
        }

        /// <summary>
        /// Second part of the package loading process, when references, assets and package analysis is done.
        /// </summary>
        /// <param name="package">The package.</param>
        /// <param name="log">The log.</param>
        /// <param name="loadParametersArg">The load parameters argument.</param>
        /// <returns></returns>
        internal bool LoadAssembliesAndAssets(ILogger log, PackageLoadParameters loadParametersArg)
        {
            var loadParameters = loadParametersArg ?? PackageLoadParameters.Default();

            try
            {

                // Load assembly references
                if (loadParameters.LoadAssemblyReferences)
                {
                    LoadAssemblyReferencesForPackage(log, loadParameters);
                }
                // Load assets
                if (loadParameters.AutoLoadTemporaryAssets)
                {
                    LoadTemporaryAssets(log, loadParameters.AssetFiles, loadParameters.CancelToken);
                }

                // Convert UPath to absolute
                if (loadParameters.ConvertUPathToAbsolute)
                {
                    var analysis = new PackageAnalysis(this, new PackageAnalysisParameters()
                    {
                        ConvertUPathTo = UPathType.Absolute,
                        IsProcessingUPaths = true, // This is done already by Package.Load
                        SetDirtyFlagOnAssetWhenFixingAbsoluteUFile = true // When loading tag attributes that have an absolute file
                    });
                    analysis.Run(log);
                }

                // Load templates
                LoadTemplates(log);

                return true;
            }
            catch (Exception ex)
            {
                log.Error("Error while pre-loading package [{0}]", ex, FullPath);

                return false;
            }
        }

        public void ValidateAssets(bool alwaysGenerateNewAssetId = false)
        {
            if (TemporaryAssets.Count == 0)
            {
                return;
            }

            try
            {
                // Make sure we are suspending notifications before updating all assets
                Assets.SuspendCollectionChanged();

                Assets.Clear();

                // Get generated output items
                var outputItems = new AssetItemCollection();

                // Create a resolver from the package
                var resolver = AssetResolver.FromPackage(this);
                resolver.AlwaysCreateNewId = alwaysGenerateNewAssetId;

                // Clean assets
                AssetCollision.Clean(TemporaryAssets, outputItems, resolver, false);

                // Add them back to the package
                foreach (var item in outputItems)
                {
                    Assets.Add(item);
                }

                TemporaryAssets.Clear();
            }
            finally
            {
                // Restore notification on assets
                Assets.ResumeCollectionChanged();
            }
        }

        /// <summary>
        /// Refreshes this package from the disk by loading or reloading all assets.
        /// </summary>
        /// <param name="log">The log.</param>
        /// <param name="assetFiles">The asset files (loaded from <see cref="ListAssetFiles"/> if null).</param>
        /// <param name="cancelToken">The cancel token.</param>
        /// <returns>A logger that contains error messages while refreshing.</returns>
        /// <exception cref="System.InvalidOperationException">Package RootDirectory is null
        /// or
        /// Package RootDirectory [{0}] does not exist.ToFormat(RootDirectory)</exception>
        public void LoadTemporaryAssets(ILogger log, IList<PackageLoadingAssetFile> assetFiles = null, CancellationToken? cancelToken = null)
        {
            if (log == null) throw new ArgumentNullException("log");

            // If FullPath is null, then we can't load assets from disk, just return
            if (FullPath == null)
            {
                log.Warning("Fullpath not set on this package");
                return;
            }

            // Clears the assets already loaded and reload them
            TemporaryAssets.Clear();

            // List all package files on disk
            if (assetFiles == null)
                assetFiles = ListAssetFiles(log, this, cancelToken);

            var progressMessage = String.Format("Loading Assets from Package [{0}]", FullPath.GetFileNameWithExtension());

            // Display this message at least once if the logger does not log progress (And it shouldn't in this case)
            var loggerResult = log as LoggerResult;
            if (loggerResult == null || !loggerResult.IsLoggingProgressAsInfo)
            {
                log.Info(progressMessage);
            }

            // Update step counter for log progress
            var tasks = new List<System.Threading.Tasks.Task>();
            for (int i = 0; i < assetFiles.Count; i++)
            {
                var assetFile = assetFiles[i];
                // Update the loading progress
                if (loggerResult != null)
                {
                    loggerResult.Progress(progressMessage, i, assetFiles.Count);
                }

                var task = cancelToken.HasValue ?
                    System.Threading.Tasks.Task.Factory.StartNew(() => LoadAsset(log, assetFile, loggerResult), cancelToken.Value) : 
                    System.Threading.Tasks.Task.Factory.StartNew(() => LoadAsset(log, assetFile, loggerResult));

                tasks.Add(task);
            }

            if (cancelToken.HasValue)
            {
                System.Threading.Tasks.Task.WaitAll(tasks.ToArray(), cancelToken.Value);
            }
            else
            {
                System.Threading.Tasks.Task.WaitAll(tasks.ToArray());
            }

            // DEBUG
            // StaticLog.Info("[{0}] Assets files loaded in {1}", assetFiles.Count, clock.ElapsedMilliseconds);

            if (cancelToken.HasValue && cancelToken.Value.IsCancellationRequested)
            {
                log.Warning("Skipping loading assets. PackageSession.Load cancelled");
            }
        }

        private void LoadAsset(ILogger log, PackageLoadingAssetFile assetFile, LoggerResult loggerResult)
        {
            var fileUPath = assetFile.FilePath;
            var sourceFolder = assetFile.SourceFolder;

            // Check if asset has been deleted by an upgrader
            if (assetFile.Deleted)
            {
                IsDirty = true;
                filesToDelete.Add(assetFile.FilePath);
            }

                // An exception can occur here, so we make sure that loading a single asset is not going to break 
                // the loop
            try
            {
                AssetMigration.MigrateAssetIfNeeded(log, assetFile);

                // Try to load only if asset is not already in the package or assetRef.Asset is null
                var assetPath = fileUPath.MakeRelative(sourceFolder).GetDirectoryAndFileName();

                var assetFullPath = fileUPath.FullPath;
                var assetContent = assetFile.AssetContent;

                var asset = LoadAsset(log, assetFullPath, assetPath, fileUPath, assetContent);

                // Create asset item
                    var assetItem = new AssetItem(assetPath, asset, this)
                {
                    IsDirty = assetContent != null,
                    SourceFolder = sourceFolder.MakeRelative(RootDirectory)
                };
                // Set the modified time to the time loaded from disk
                if (!assetItem.IsDirty)
                    assetItem.ModifiedTime = File.GetLastWriteTime(assetFullPath);

                // TODO: Let's review that when we rework import process
                // Not fixing asset import anymore, as it was only meant for upgrade
                // However, it started to make asset dirty, for ex. when we create a new texture, choose a file and reload the scene later
                // since there was no importer id and base.
                //FixAssetImport(assetItem);

                // Add to temporary assets
                lock (TemporaryAssets)
                {
                    TemporaryAssets.Add(assetItem);
                }
            }
            catch (Exception ex)
            {
                int row = 1;
                int column = 1;
                var yamlException = ex as YamlException;
                if (yamlException != null)
                {
                    row = yamlException.Start.Line + 1;
                    column = yamlException.Start.Column;
                }

                var module = log.Module;

                var assetReference = new AssetReference<Asset>(Guid.Empty, fileUPath.FullPath);

                // TODO: Change this instead of patching LoggerResult.Module, use a proper log message
                if (loggerResult != null)
                {
                    loggerResult.Module = "{0}({1},{2})".ToFormat(Path.GetFullPath(fileUPath.FullPath), row, column);
                }

                log.Error(this, assetReference, AssetMessageCode.AssetLoadingFailed, ex, fileUPath, ex.Message);

                if (loggerResult != null)
                {
                    loggerResult.Module = module;
                }
            }
        }

        /// <summary>
        /// Loads the assembly references that were not loaded before.
        /// </summary>
        /// <param name="log">The log.</param>
        /// <param name="loadParametersArg">The load parameters argument.</param>
        public void UpdateAssemblyReferences(ILogger log, PackageLoadParameters loadParametersArg = null)
        {
            if (State < PackageState.DependenciesReady)
                return;

            var loadParameters = loadParametersArg ?? PackageLoadParameters.Default();
            LoadAssemblyReferencesForPackage(log, loadParameters);
        }

        private static Asset LoadAsset(ILogger log, string assetFullPath, string assetPath, UFile fileUPath, byte[] assetContent)
        {
            var asset = assetContent != null
                ? (Asset)AssetSerializer.Load(new MemoryStream(assetContent), Path.GetExtension(assetFullPath), log)
                : AssetSerializer.Load<Asset>(assetFullPath, log);

            // Set location on source code asset
            var sourceCodeAsset = asset as SourceCodeAsset;
            if (sourceCodeAsset != null)
            {
                // Use an id generated from the location instead of the default id
                sourceCodeAsset.Id = SourceCodeAsset.GenerateGuidFromLocation(assetPath);
                sourceCodeAsset.AbsoluteSourceLocation = fileUPath;
            }

            return asset;
        }

        private void LoadAssemblyReferencesForPackage(ILogger log, PackageLoadParameters loadParameters)
        {
            if (log == null) throw new ArgumentNullException("log");
            if (loadParameters == null) throw new ArgumentNullException("loadParameters");
            var assemblyContainer = loadParameters.AssemblyContainer ?? AssemblyContainer.Default;
            foreach (var profile in Profiles)
            {
                foreach (var projectReference in profile.ProjectReferences.Where(projectRef => projectRef.Type == ProjectType.Plugin || projectRef.Type == ProjectType.Library))
                {
                    // Check if already loaded
                    // TODO: More advanced cases: unload removed references, etc...
                    if (loadedAssemblies.Any(x => x.ProjectReference == projectReference))
                        continue;

                    string assemblyPath = null;
                    var fullProjectLocation = UPath.Combine(RootDirectory, projectReference.Location);

                    try
                    {
                        var forwardingLogger = new ForwardingLoggerResult(log);
                        assemblyPath = VSProjectHelper.GetOrCompileProjectAssembly(fullProjectLocation, forwardingLogger, loadParameters.AutoCompileProjects, extraProperties: loadParameters.ExtraCompileProperties, onlyErrors: true);
                        if (String.IsNullOrWhiteSpace(assemblyPath))
                        {
                            log.Error("Unable to locate assembly reference for project [{0}]", fullProjectLocation);
                            continue;
                        }

                        var loadedAssembly = new PackageLoadedAssembly(projectReference, assemblyPath);
                        loadedAssemblies.Add(loadedAssembly);

                        if (!File.Exists(assemblyPath) || forwardingLogger.HasErrors)
                        {
                            log.Error("Unable to build assembly reference [{0}]", assemblyPath);
                            continue;
                        }

                        var assembly = assemblyContainer.LoadAssemblyFromPath(assemblyPath, log);
                        if (assembly == null)
                        {
                            log.Error("Unable to load assembly reference [{0}]", assemblyPath);
                        }

                        loadedAssembly.Assembly = assembly;

                        if (assembly != null)
                        {
                            // Register assembly in the registry
                            AssemblyRegistry.Register(assembly, AssemblyCommonCategories.Assets);
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Error("Unexpected error while loading project [{0}] or assembly reference [{1}]", ex, fullProjectLocation, assemblyPath);
                    }
                }
            }
        }

        private void UpdateSourceFolders()
        {
            // If there are not assets, we don't need to update or create an asset folder
            if (Assets.Count == 0)
            {
                return;
            }

            // Make sure there is a shared profile at least
            var sharedProfile = Profiles.FindSharedProfile();
            if (sharedProfile == null)
            {
                sharedProfile = PackageProfile.NewShared();
                Profiles.Add(sharedProfile);
            }

            // Use by default the first asset folders if not defined on the asset item
            var defaultFolder = sharedProfile.AssetFolders.Count > 0 ? sharedProfile.AssetFolders.First().Path : UDirectory.This;
            var assetFolders = new HashSet<UDirectory>(GetDistinctAssetFolderPaths());
            foreach (var asset in Assets)
            {
                if (asset.SourceFolder == null)
                {
                    asset.SourceFolder = defaultFolder.IsAbsolute ? defaultFolder.MakeRelative(RootDirectory) : defaultFolder;
                    asset.IsDirty = true;
                }

                var assetFolderAbsolute = UPath.Combine(RootDirectory, asset.SourceFolder);
                if (!assetFolders.Contains(assetFolderAbsolute))
                {
                    assetFolders.Add(assetFolderAbsolute);
                    sharedProfile.AssetFolders.Add(new AssetFolder(assetFolderAbsolute));
                    IsDirty = true;
                }
            }
        }

        /// <summary>
        /// Loads the templates.
        /// </summary>
        /// <param name="log">The log result.</param>
        private void LoadTemplates(ILogger log)
        {
            foreach (var templateDir in TemplateFolders)
            {
                foreach (var filePath in templateDir.Files)
                {
                    try
                    {
                        var file = new FileInfo(filePath);
                        if (!file.Exists)
                        {
                            log.Warning("Template [{0}] does not exist ", file);
                            continue;
                        }

                        var templateDescription = AssetSerializer.Load<TemplateDescription>(file.FullName);
                        templateDescription.FullPath = file.FullName;
                        Templates.Add(templateDescription);
                    }
                    catch (Exception ex)
                    {
                        log.Error("Error while loading template from [{0}]", ex, filePath);
                    }
                }
            }
        }

        private List<UDirectory> GetDistinctAssetFolderPaths()
        {
            var existingAssetFolders = new List<UDirectory>();
            foreach (var profile in Profiles)
            {
                foreach (var folder in profile.AssetFolders)
                {
                    var folderPath = RootDirectory != null ? UPath.Combine(RootDirectory, folder.Path) : folder.Path;
                    if (!existingAssetFolders.Contains(folderPath))
                    {
                        existingAssetFolders.Add(folderPath);
                    }
                }
            }
            return existingAssetFolders;
        }

        public static List<PackageLoadingAssetFile> ListAssetFiles(ILogger log, Package package, CancellationToken? cancelToken)
        {
            var listFiles = new List<PackageLoadingAssetFile>();

            // TODO Check how to handle refresh correctly as a public API
            if (package.RootDirectory == null)
            {
                throw new InvalidOperationException("Package RootDirectory is null");
            }

            if (!Directory.Exists(package.RootDirectory))
            {
                return listFiles;
            }

            // Iterate on each source folders
            foreach (var sourceFolder in package.GetDistinctAssetFolderPaths())
            {
                // Lookup all files
                foreach (var directory in FileUtility.EnumerateDirectories(sourceFolder, SearchDirection.Down))
                {
                    var files = directory.GetFiles();

                    foreach (var filePath in files)
                    {
                        // Don't load package via this method
                        if (filePath.FullName.EndsWith(PackageFileExtension))
                        {
                            continue;
                        }

                        // Make an absolute path from the root of this package
                        var fileUPath = new UFile(filePath.FullName);
                        if (fileUPath.GetFileExtension() == null)
                        {
                            continue;
                        }

                        // If this kind of file an asset file?
                        if (!AssetRegistry.IsAssetFileExtension(fileUPath.GetFileExtension()))
                        {
                            continue;
                        }

                        listFiles.Add(new PackageLoadingAssetFile(fileUPath, sourceFolder));
                    }
                }
            }

            return listFiles;
        }

        /// <summary>
        /// Fixes asset import that were imported by the previous method. Add a AssetImport.SourceHash and ImporterId
        /// </summary>
        /// <param name="item">The item.</param>
        private static void FixAssetImport(AssetItem item)
        {
            // TODO: this whole method is a temporary migration. This should be removed in the next version

            var assetImport = item.Asset as AssetImport;
            if (assetImport == null || assetImport.Source == null)
            {
                return;
            }

            // If the asset has a source but no import base, then we are going to simulate an original import
            if (assetImport.Base == null)
            {
                var fileExtension = assetImport.Source.GetFileExtension();

                var assetImportBase = (AssetImport)AssetCloner.Clone(assetImport);
                assetImportBase.SetAsRootImport();
                assetImportBase.SetDefaults();

                // Setup default importer
                if (!String.IsNullOrEmpty(fileExtension))
                {
                    var importerId = AssetRegistry.FindImporterByExtension(fileExtension).FirstOrDefault();
                    if (importerId != null)
                    {
                        assetImport.ImporterId = importerId.Id;
                    }
                }
                var assetImportTracked = assetImport as AssetImportTracked;
                if (assetImportTracked != null)
                {
                    assetImportTracked.SourceHash = ObjectId.Empty;
                }

                assetImport.Base = new AssetBase(assetImportBase);
                item.IsDirty = true;
            }
        }
    }
}