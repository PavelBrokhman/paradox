﻿// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SiliconStudio.Core.Diagnostics;
using SiliconStudio.Core.IO;

namespace SiliconStudio.Assets.Tests
{
  
    [TestFixture]
    public class TestPackage : TestBase
    {
        [Test]
        public void TestBasicPackageCreateSaveLoad()
        {
            var dirPath = DirectoryTestBase + @"TestBasicPackageCreateSaveLoad";

            string testGenerated1 = Path.Combine(dirPath, "TestPackage_TestBasicPackageCreateSaveLoad_Generated1.pdxpkg");
            string testGenerated2 = Path.Combine(dirPath,"TestPackage_TestBasicPackageCreateSaveLoad_Generated2.pdxpkg");
            string referenceFilePath = Path.Combine(dirPath,"TestPackage_TestBasicPackageCreateSaveLoad_Reference.pdxpkg");

            // Force the PackageId to be the same each time we run the test
            // Usually the PackageId is unique and generated each time we create a new project
            var project = new Package { Id = Guid.Empty, FullPath = testGenerated1 };
            var sharedProfile = new PackageProfile("Shared", new AssetFolder("."));
            project.Profiles.Add(sharedProfile);
            var projectReference = new ProjectReference(Guid.Empty, Path.Combine(dirPath, "test.csproj"), ProjectType.Executable);
            sharedProfile.ProjectReferences.Add(projectReference);

            var session = new PackageSession(project);
            // Write the solution when saving
            session.SolutionPath = Path.Combine(dirPath, "TestPackage_TestBasicPackageCreateSaveLoad_Generated1.sln");

            // Delete the solution before saving it 
            if (File.Exists(session.SolutionPath))
            {
                File.Delete(session.SolutionPath);
            }

            var result = session.Save();
            Assert.IsFalse(result.HasErrors);

            // Reload the raw package and if UFile and UDirectory were saved relative
            var rawPackage = (Package)AssetSerializer.Load(testGenerated1);
            var rawPackageSharedProfile = rawPackage.Profiles.FirstOrDefault();
            Assert.IsNotNull(rawPackageSharedProfile);
            var rawSourceFolder = rawPackage.Profiles.First().AssetFolders.FirstOrDefault();
            Assert.IsNotNull(rawSourceFolder);
            Assert.AreEqual(".", (string)rawSourceFolder.Path);
            Assert.AreEqual("test.csproj", (string)rawPackageSharedProfile.ProjectReferences[0].Location);

            // Reload the package directly from the pdxpkg
            var project2Result = PackageSession.Load(testGenerated1);
            AssertResult(project2Result);
            var project2 = project2Result.Session.LocalPackages.FirstOrDefault();
            Assert.IsNotNull(project2);
            Assert.AreEqual(project.Id, project2.Id);
            Assert.IsTrue(project2.Profiles.Count > 0);
            Assert.IsTrue(project2.Profiles.First().AssetFolders.Count > 0);
            var sourceFolder = project.Profiles.First().AssetFolders.First().Path;
            Assert.AreEqual(sourceFolder, project2.Profiles.First().AssetFolders.First().Path);

            // Reload the package from the sln
            var sessionResult = PackageSession.Load(session.SolutionPath);
            Assert.IsFalse(sessionResult.HasErrors);

            var sessionReload = sessionResult.Session;
            Assert.AreEqual(1, sessionReload.LocalPackages.Count());
            Assert.AreEqual(project.Id, sessionReload.LocalPackages.First().Id);
            Assert.AreEqual(1, sessionReload.LocalPackages.First().Profiles.Count);

            var sharedProfileReload = sessionReload.LocalPackages.First().Profiles.First();
            Assert.AreEqual(1, sharedProfileReload.ProjectReferences.Count);
            Assert.AreEqual(projectReference, sharedProfileReload.ProjectReferences[0]);
        }

        [Test]
        public void TestPackageAndAssetIdChange()
        {
            var project = new Package();
            var assetItem = new AssetItem("test", new AssetObjectTest());
            var asset = assetItem.Asset;
            project.Assets.Add(assetItem);

            // Can't change an asset id once it is loaded into a project
            Assert.Throws<InvalidOperationException>(() => asset.Id = Guid.Empty);

            project.Assets.Remove(assetItem);

            // Can change Id once the asset was removed from the project
            Assert.DoesNotThrow(() => asset.Id = Guid.Empty);
        }

        [Test]
        public void TestPackageLoadingWithAssets()
        {
            var basePath = Path.Combine(DirectoryTestBase, @"TestPackage");
            var projectPath = Path.Combine(basePath, "TestPackageLoadingWithAssets.pdxpkg");

            var sessionResult = PackageSession.Load(projectPath);
            AssertResult(sessionResult);
            var session = sessionResult.Session;

            var rootPackageId = new Guid("4102BF96-796D-4800-9983-9C227FAB7BBD");

            var project = session.Packages.Find(rootPackageId);
            Assert.IsNotNull(project);
            Assert.AreEqual(3, project.Assets.Count, "Invalid number of assets loaded");

            Assert.AreEqual(1, project.LocalDependencies.Count, "Expecting subproject");

            Assert.AreNotEqual(Guid.Empty, project.Assets.First().Id);

            // Check for UPathRelativeTo
            var profile = project.Profiles.FirstOrDefault();
            Assert.NotNull(profile);
            var folder = profile.AssetFolders.FirstOrDefault();
            Assert.NotNull(folder);
            Assert.NotNull(folder.Path);
            Assert.NotNull(folder.Path.IsAbsolute);
            var import = folder.RawImports.FirstOrDefault();
            Assert.NotNull(import);
            Assert.IsTrue(import.SourceDirectory != null && import.SourceDirectory.IsRelative);

            // Save project back to disk on a different location
            project.FullPath = Path.Combine(DirectoryTestBase, @"TestPackage2\TestPackage2.pdxpkg");
            var subPackage = session.Packages.Find(Guid.Parse("281321F0-7664-4523-B1DC-3CFC26F80F77"));
            subPackage.FullPath = Path.Combine(DirectoryTestBase, @"TestPackage2\SubPackage\SubPackage.pdxpkg");
            session.Save();

            var project2Result = PackageSession.Load(DirectoryTestBase + @"TestPackage2\TestPackage2.pdxpkg");
            AssertResult(project2Result);
            var project2 = project2Result.Session.Packages.Find(rootPackageId);
            Assert.IsNotNull(project2);
            Assert.AreEqual(3, project2.Assets.Count);
        }

        [Test]
        public void TestMovingAssets()
        {
            var basePath = Path.Combine(DirectoryTestBase, @"TestPackage");
            var projectPath = Path.Combine(basePath, "TestPackageLoadingWithAssets.pdxpkg");

            var rootPackageId = new Guid("4102BF96-796D-4800-9983-9C227FAB7BBD");
            var testAssetId = new Guid("C2D80EF9-2160-43B2-9FEE-A19A903A0BE0");

            // Load the project from the original location
            var sessionResult1 = PackageSession.Load(projectPath);
            {
                AssertResult(sessionResult1);
                var session = sessionResult1.Session;
                var project = session.Packages.Find(rootPackageId);
                Assert.IsNotNull(project);

                Assert.AreEqual(3, project.Assets.Count, "Invalid number of assets loaded");

                // Find the second asset that was referencing the changed asset
                var testAssetItem = session.FindAsset(testAssetId);
                Assert.NotNull(testAssetItem);

                var testAsset = (AssetObjectTest)testAssetItem.Asset;
                Assert.AreEqual(new UFile(Path.Combine(basePath, "SubFolder/TestAsset.pdxtest")), testAsset.RawAsset);

                // First save a copy of the project to TestPackageMovingAssets1
                project.FullPath = Path.Combine(DirectoryTestBase, @"TestPackageMovingAssets1\TestPackage2.pdxpkg");
                var subPackage = session.Packages.Find(Guid.Parse("281321F0-7664-4523-B1DC-3CFC26F80F77"));
                subPackage.FullPath = Path.Combine(DirectoryTestBase, @"TestPackageMovingAssets1\SubPackage\SubPackage.pdxpkg");
                session.Save();
            }

            // Reload the project from the location TestPackageMovingAssets1
            var sessionResult2 = PackageSession.Load(DirectoryTestBase + @"TestPackageMovingAssets1\TestPackage2.pdxpkg");
            {
                AssertResult(sessionResult2);
                var session = sessionResult2.Session;
                var project = session.Packages.Find(rootPackageId);
                Assert.IsNotNull(project);
                Assert.AreEqual(3, project.Assets.Count, "Invalid number of assets loaded");

                // Move asset into a different directory
                var assetItem = project.Assets.Find(new Guid("28D0DE9C-8913-41B1-B50E-848DD8A7AF65"));
                Assert.NotNull(assetItem);
                project.Assets.Remove(assetItem);

                var newAssetItem = new AssetItem("subTest/TestAsset2", assetItem.Asset);
                project.Assets.Add(newAssetItem);

                // Save the whole project to a different location
                project.FullPath = Path.Combine(DirectoryTestBase, @"TestPackageMovingAssets2\TestPackage2.pdxpkg");
                var subPackage = session.Packages.Find(Guid.Parse("281321F0-7664-4523-B1DC-3CFC26F80F77"));
                subPackage.FullPath = Path.Combine(DirectoryTestBase, @"TestPackageMovingAssets2\SubPackage\SubPackage.pdxpkg");
                session.Save();
            }

            // Reload the project from location TestPackageMovingAssets2
            var sessionResult3 = PackageSession.Load(DirectoryTestBase + @"TestPackageMovingAssets2\TestPackage2.pdxpkg");
            {
                AssertResult(sessionResult3);
                var session = sessionResult3.Session;
                var project = session.Packages.Find(rootPackageId);
                Assert.IsNotNull(project);
                Assert.AreEqual(3, project.Assets.Count, "Invalid number of assets loaded");

                // Find the second asset that was referencing the changed asset
                var assetItemChanged = session.FindAsset(testAssetId);
                Assert.NotNull(assetItemChanged);

                // Check that references were correctly updated
                var assetChanged = (AssetObjectTest)assetItemChanged.Asset;
                Assert.AreEqual(new UFile(Path.Combine(Environment.CurrentDirectory, DirectoryTestBase) + "/TestPackage/SubFolder/TestAsset.pdxtest"), assetChanged.RawAsset);
                var text = File.ReadAllText(assetItemChanged.FullPath);
                Assert.True(text.Contains("../../TestPackage/SubFolder/TestAsset.pdxtest"));

                Assert.AreEqual("subTest/TestAsset2", assetChanged.Reference.Location);
            }
        }

        private void AssertResult(LoggerResult log)
        {
            foreach (var logMessage in log.Messages)
            {
                Console.WriteLine(logMessage);
            }
            Assert.False(log.HasErrors);
        }

        static void Main()
        {
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < 10; i++)
            {
                var session = PackageSession.Load(@"E:\Code\SengokuRun\SengokuRun\WindowsLauncher\GameAssets\Assets.pdxpkg");
            }
            var elapsed = clock.ElapsedMilliseconds;
            Console.WriteLine("{0}ms", elapsed);
        }
    }
}
