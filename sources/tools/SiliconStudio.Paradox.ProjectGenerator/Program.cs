﻿// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Mono.Options;
using SiliconStudio.Assets;
using SiliconStudio.Assets.Templates;
using SiliconStudio.Core;
using SiliconStudio.Core.Diagnostics;
using SiliconStudio.Core.Extensions;
using SiliconStudio.Core.IO;
using SiliconStudio.Core.VisualStudio;
using SiliconStudio.Paradox.Assets;
using SiliconStudio.Paradox.Graphics;
using SiliconStudio.ProjectTemplating;

namespace SiliconStudio.Paradox.ProjectGenerator
{
    class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            var exeName = Path.GetFileName(Assembly.GetExecutingAssembly().Location);
            var showHelp = false;
            int exitCode = 0;
            string outputFile = null;
            string platform = null;
            string projectName = null;
            string outputDirectory = null;

            var p = new OptionSet
                {
                    "Copyright (C) 2011-2013 Silicon Studio Corporation. All Rights Reserved",
                    "Paradox Project Generator Tool - Version: "
                    +
                    String.Format(
                        "{0}.{1}.{2}",
                        typeof(Program).Assembly.GetName().Version.Major,
                        typeof(Program).Assembly.GetName().Version.Minor,
                        typeof(Program).Assembly.GetName().Version.Build) + string.Empty,
                    string.Format("Usage: {0} [operation] [input-file] [options]*", exeName),
                    "=== Operations ===",
                    " solution 'solution-file.sln'   Generate platform-specific solution from solution-file.sln",
                    " project-unittest               Create unit-test from template",
                    string.Empty,
                    "=== General options ===",
                    string.Empty,
                    { "h|help", "Show this message and exit", v => showHelp = v != null },
                    string.Empty,
                    "=== Options for: solution ===",
                    string.Empty,
                    { "p|platform=", "Set platform name", v => platform = v },
                    { "o|output-file=", "Output file", v => outputFile = v },
                    string.Empty,
                    "=== Options for: project-unittest ===",
                    string.Empty,
                    { "project-name=", "Project name", v => projectName = v },
                    { "d|output-directory=", "Output directory", v => outputDirectory = v },
                    string.Empty,
                };

            try
            {
                var commandArgs = p.Parse(args);
                if (showHelp)
                {
                    p.WriteOptionDescriptions(Console.Out);
                    return 0;
                }

                var templateFolder = @"..\..\sources\tools\SiliconStudio.Paradox.ProjectGenerator\Templates";

                switch (commandArgs[0])
                {
                    case "solution":
                        if (commandArgs.Count != 2)
                            throw new OptionException("Expect only one input file", "");

                        var inputFile = commandArgs[1];

                        GenerateSolution(outputFile, platform, inputFile);
                        break;
                    case "project-unittest":
                        if (projectName == null)
                            throw new OptionException("Project name is not set.", "project-name");

                        GenerateUnitTestProject(
                            outputDirectory,
                            Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), Path.Combine(templateFolder, @"Paradox.UnitTests\Paradox.UnitTests.ttproj")),
                            projectName);
                        break;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("{0}: {1}", exeName, e);
                if (e is OptionException)
                    p.WriteOptionDescriptions(Console.Out);
                exitCode = 1;
            }

            return exitCode;
        }

        private static void GenerateUnitTestProject(string outputDirectory, string templateFile, string name)
        {
            var projectTemplate = ProjectTemplate.Load(templateFile);

            // Force reference to Paradox.Assets (to have acess to SolutionPlatform)
            projectTemplate.Assemblies.Add(typeof(GraphicsProfile).Assembly.FullName);
            projectTemplate.Assemblies.Add(typeof(ParadoxConfig).Assembly.FullName);

            var options = new Dictionary<string, object>();

            var session = new PackageSession();
            var result = new LoggerResult();

            var templateGeneratorParameters = new TemplateGeneratorParameters();
            templateGeneratorParameters.OutputDirectory = outputDirectory;
            templateGeneratorParameters.Session = session;
            templateGeneratorParameters.Name = name;
            templateGeneratorParameters.Logger = result;
            templateGeneratorParameters.Description = new TemplateDescription();

            PackageUnitTestGenerator.Default.Generate(templateGeneratorParameters);
            if (result.HasErrors)
            {
                Console.WriteLine("Error generating package: {0}", result.ToText());
                return;
            }

            var package = templateGeneratorParameters.Package;

            var previousCurrent = session.CurrentPackage;
            session.CurrentPackage = package;

            // Compute Paradox Sdk relative path
            // We are supposed to be in standard output binary folder, so Paradox root should be at ..\..
            var paradoxPath = UDirectory.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), new UDirectory(@"..\.."));
            var paradoxRelativePath = new UDirectory(paradoxPath)
                .MakeRelative(outputDirectory)
                .ToString()
                .Replace('/', '\\');
            paradoxRelativePath = paradoxRelativePath.TrimEnd('\\');

            options["Namespace"] = name;
            options["Package"] = package;
            options["Platforms"] = new List<SolutionPlatform>(AssetRegistry.SupportedPlatforms);
            options["ParadoxSdkRelativeDir"] = paradoxRelativePath;

            // Generate project template
            var projectGuid = Guid.NewGuid();
            result = projectTemplate.Generate(outputDirectory, name, projectGuid, options);
            if (result.HasErrors)
            {
                Console.WriteLine("Error generating solution: {0}", result.ToText());
                return;
            }

            var sharedProfile = package.Profiles.FindSharedProfile();

            // Setup the assets folder
            Directory.CreateDirectory(UPath.Combine(outputDirectory, (UDirectory)"Assets/Shared"));

            // Add Windows test as Shared library
            var projectWindowsRef = new ProjectReference();
            projectWindowsRef.Id = projectGuid;
            projectWindowsRef.Location = UPath.Combine(outputDirectory, (UFile)(name + ".Windows.csproj"));
            projectWindowsRef.Type = SiliconStudio.Assets.ProjectType.Library;
            sharedProfile.ProjectReferences.Add(projectWindowsRef);

            // Generate executable projects for each platform
            foreach (var platform in AssetRegistry.SupportedPlatforms)
            {
                var platformProfile = new PackageProfile(platform.Name) { Platform = platform.Type };
                platformProfile.AssetFolders.Add(new AssetFolder("Assets/"+ platform.Name));
                
                // Log progress
                var projectName = name + "." + platform.Type;

                // Create project reference
                var projectPlatformRef = new ProjectReference();
                projectPlatformRef.Id = projectGuid;
                projectPlatformRef.Location = UPath.Combine(outputDirectory, (UFile)(projectName + ".csproj"));
                projectPlatformRef.Type = SiliconStudio.Assets.ProjectType.Executable;

                platformProfile.ProjectReferences.Add(projectPlatformRef);

                // Add build configuration per platform
                platform.Properties.CopyTo(platformProfile.Properties, true);

                package.Profiles.Add(platformProfile);
            }

            session.CurrentPackage = previousCurrent;

            result = session.Save();
            if (result.HasErrors)
            {
                Console.WriteLine("Error saving package: {0}", result.ToText());
                return;
            }
        }

        private static void GenerateSolution(string outputFile, string platform, string inputFile)
        {
            if (outputFile == null)
                throw new OptionException("Expect one output file", "o");

            if (platform == null)
                throw new OptionException("Platform not specified", "p");

            string projectSuffix = platform;

            // Read .sln
            var solution = Solution.FromFile(inputFile);

            var processors = new List<IProjectProcessor>();

            ProjectType projectType;
            if (Enum.TryParse(platform, out projectType))
            {
                processors.Add(new SynchronizeProjectProcessor(projectType));
            }

            var projectProcessorContexts = new List<ProjectProcessorContext>();

            var removedProjects = new List<Project>();

            // Select active projects
            SelectActiveProjects(solution, platform, projectProcessorContexts, removedProjects);

            // Remove unnecessary project dependencies
            CleanProjectDependencies(projectProcessorContexts, removedProjects);

            // Process projects
            foreach (var context in projectProcessorContexts)
            {
                foreach (var processor in processors)
                    processor.Process(context);
            }

            // Update project references
            UpdateProjectReferences(projectProcessorContexts, projectSuffix);

            // Update solution with project that were recreated differently
            UpdateSolutionWithModifiedProjects(projectProcessorContexts, projectSuffix);

            // Rebuild solution configurations
            UpdateSolutionBuildConfigurations(platform, solution, projectProcessorContexts);

            // Remove empty solution folders
            RemoveEmptySolutionFolders(solution);

            // Save .sln
            solution.SaveAs(outputFile);

            // If there is a DotSettings (Resharper team shared file), create one that also reuse this one
            // Note: For now, it assumes input and output solutions are in the same folder (when constructing relative path to DotSetting file)
            var dotSettingsFile = inputFile + ".DotSettings";
            if (File.Exists(dotSettingsFile))
            {
                // Generate a deterministic GUID based on output file
                var cryptoProvider = new System.Security.Cryptography.MD5CryptoServiceProvider();
                var inputBytes = Encoding.Default.GetBytes(Path.GetFileName(outputFile));
                var deterministicGuid = new Guid(cryptoProvider.ComputeHash(inputBytes));

                var resharperDotSettingsGenerator = new ResharperDotSettings
                {
                    SharedSolutionDotSettings = new FileInfo(dotSettingsFile),
                    FileInjectedGuid = deterministicGuid,
                };

                var outputDotSettingsFile = resharperDotSettingsGenerator.TransformText();
                File.WriteAllText(outputFile + ".DotSettings", outputDotSettingsFile);
            }
        }

        private static void RemoveEmptySolutionFolders(Solution solution)
        {
            var usedSolutionFolders = new HashSet<Project>();

            // Find projects and solution folders containing files (there is a section)
            var projects = solution.Projects.Where(x => !x.IsSolutionFolder || x.Sections.Count > 0).ToArray();

            // Mark solution folders of projects as needed
            foreach (var project in projects)
            {
                var currentProject = project;

                // Go through parents and add them to usedSolutionFolders
                while (currentProject != null)
                {
                    usedSolutionFolders.Add(currentProject);
                    currentProject = currentProject.ParentProject;
                }
            }

            // Remove unused solution folders
            solution.Projects.RemoveWhere(x => !usedSolutionFolders.Contains(x));
        }

        /// <summary>
        /// Process each project and select the one that needs to be included.
        /// </summary>
        /// <param name="solution">The solution.</param>
        /// <param name="platform">The platform.</param>
        /// <param name="projectProcessorContexts">The project processor contexts.</param>
        /// <param name="removedProjects">The removed projects.</param>
        private static void SelectActiveProjects(Solution solution, string platform, List<ProjectProcessorContext> projectProcessorContexts, List<Project> removedProjects)
        {
            foreach (var solutionProject in solution.Projects.ToArray())
            {
                // Is it really a project?
                if (!solutionProject.FullPath.EndsWith(".csproj") && !solutionProject.FullPath.EndsWith(".vcxproj"))
                    continue;

                // Load XML project
                var doc = XDocument.Load(solutionProject.FullPath);
                var ns = doc.Root.Name.Namespace;
                var mgr = new XmlNamespaceManager(new NameTable());
                mgr.AddNamespace("x", ns.NamespaceName);

                bool shouldKeep = false;

                // Check ParadoxSupportedPlatforms
                var paradoxBuildTagsNode = doc.XPathSelectElement("/x:Project/x:PropertyGroup/x:ParadoxBuildTags", mgr);
                if (paradoxBuildTagsNode != null)
                {
                    var paradoxBuildTags = paradoxBuildTagsNode.Value;
                    if (paradoxBuildTags == "*" ||
                        paradoxBuildTags.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Contains(platform))
                        shouldKeep = true;
                }

                // Windows-specific project without a platform-specific equivalent are removed.
                var fullPath = solutionProject.FullPath;
                if (Path.GetFileNameWithoutExtension(fullPath).EndsWith(".Windows"))
                {
                    // Replace .Windows with current platform
                    fullPath = fullPath.Replace(".Windows", "." + platform);

                    if (!File.Exists(fullPath))
                    {
                        shouldKeep = false;
                    }
                }

                if (shouldKeep)
                {
                    projectProcessorContexts.Add(new ProjectProcessorContext(solution, solutionProject, doc, mgr));
                }
                else
                {
                    removedProjects.Add(solutionProject);
                    solution.Projects.Remove(solutionProject);
                }
            }
        }

        /// <summary>
        /// Remove unecessary project dependencies.
        /// </summary>
        /// <param name="projectProcessorContexts">The project processor contexts.</param>
        /// <param name="removedProjects">The removed projects.</param>
        private static void CleanProjectDependencies(List<ProjectProcessorContext> projectProcessorContexts, List<Project> removedProjects)
        {
            foreach (var context in projectProcessorContexts)
            {
                if (context.Project.Sections.Contains("ProjectDependencies"))
                {
                    var projectDependencies = context.Project.Sections["ProjectDependencies"].Properties;
                    var projectDependenciesToRemove = new List<PropertyItem>();
                    foreach (var projectDependency in projectDependencies)
                    {
                        Guid dependencyGuid;
                        if (!Guid.TryParse(projectDependency.Name, out dependencyGuid))
                            continue;

                        // No other project left that has this ID: Remove dependency (and issue warning)
                        var removedProject = removedProjects.FirstOrDefault(project => project.Guid == dependencyGuid);
                        if (removedProject != null)
                        {
                            projectDependenciesToRemove.Add(projectDependency);
                            Console.WriteLine("[{0}] Removed solution dependency to {1}", context.Project.Name,
                                removedProject.Name);
                        }
                    }
                    foreach (var projectDependency in projectDependenciesToRemove)
                    {
                        projectDependencies.Remove(projectDependency);
                    }

                    // If no more dependencies, remove the section
                    if (projectDependencies.Count == 0)
                    {
                        context.Project.Sections.Remove("ProjectDependencies");
                    }
                }
            }
        }

        /// <summary>
        /// Updates the project references.
        /// </summary>
        /// <param name="projectProcessorContexts">The project processor contexts.</param>
        /// <param name="projectSuffix">The project suffix.</param>
        private static void UpdateProjectReferences(List<ProjectProcessorContext> projectProcessorContexts, string projectSuffix)
        {
            bool modifiedChanged = false;

            // Since a change might affect other projects, let's loop until it stabilizes.
            do
            {
                modifiedChanged = false;

                // For each project,
                foreach (var context in projectProcessorContexts)
                {
                    // Process every reference
                    foreach (var projectReference in context.Document.XPathSelectElements("/x:Project/x:ItemGroup/x:ProjectReference", context.NamespaceManager))
                    {
                        var includeAttribute = projectReference.Attribute(XName.Get("Include"));
                        var referencedContext =
                            projectProcessorContexts.FirstOrDefault(x => x.Modified && includeAttribute.Value.EndsWith(Path.GetFileName(x.Project.FullPath)));
                        if (referencedContext != null)
                        {
                            // This project has been "modified" (new .csproj), let's update reference to it
                            var projectFileName = Path.GetFileName(referencedContext.Project.FullPath);
                            var generatedProjectFileName = projectFileName.Replace(".Windows", string.Empty);
                            var fileExtPosition = generatedProjectFileName.LastIndexOf('.');
                            generatedProjectFileName = generatedProjectFileName.Substring(0, fileExtPosition + 1) + projectSuffix +
                                                           generatedProjectFileName.Substring(fileExtPosition);

                            includeAttribute.Value = includeAttribute.Value.Replace(projectFileName, generatedProjectFileName);

                            if (!context.Modified)
                            {
                                // Restart from beginning if one project got modified
                                context.Modified = true;
                                modifiedChanged = true;
                                break;
                            }
                        }
                    }

                    // Nothing changed? Go to next project
                    if (modifiedChanged)
                        break;
                }
            } while (modifiedChanged); // If something changed, process another time
        }

        /// <summary>
        /// Updates the solution with modified projects.
        /// </summary>
        /// <param name="projectProcessorContexts">The project processor contexts.</param>
        /// <param name="projectSuffix">The project suffix.</param>
        private static void UpdateSolutionWithModifiedProjects(List<ProjectProcessorContext> projectProcessorContexts, string projectSuffix)
        {
            foreach (var context in projectProcessorContexts)
            {
                if (context.Modified)
                {
                    var projectDirectory = Path.GetDirectoryName(context.Project.FullPath);
                    var projectFileName = Path.GetFileName(context.Project.FullPath);
                    var generatedProjectFileName = projectFileName.Replace(".Windows", string.Empty);
                    var fileExtPosition = generatedProjectFileName.LastIndexOf('.');
                    generatedProjectFileName = generatedProjectFileName.Substring(0, fileExtPosition + 1) + projectSuffix +
                                                   generatedProjectFileName.Substring(fileExtPosition);

                    context.Document.Save(projectDirectory + Path.DirectorySeparatorChar + generatedProjectFileName);

                    // Solution should point to new generated file
                    context.Project.Name = context.Project.Name.Replace(".Windows", string.Empty) + "." + projectSuffix;
                    context.Project.RelativePath = context.Project.RelativePath.Replace(projectFileName,
                        generatedProjectFileName);
                }
            }
        }

        /// <summary>
        /// Updates the solution build configurations.
        /// </summary>
        /// <param name="platform">The platform.</param>
        /// <param name="solution">The solution.</param>
        /// <param name="projectProcessorContexts">The project processor contexts.</param>
        private static void UpdateSolutionBuildConfigurations(string platform, Solution solution, List<ProjectProcessorContext> projectProcessorContexts)
        {
            var configurations = new Dictionary<string, string>();
            bool needDeploy = false;

            if (platform == "WindowsPhone")
            {
                configurations.Add("WindowsPhone", "WindowsPhone");
                needDeploy = true;
            }
            else if (platform == "WindowsStore")
            {
                configurations.Add("WindowsStore", "WindowsStore");
                needDeploy = true;
            }
            else if (platform == "Windows10")
            {
                configurations.Add("Windows10", "Windows10");
                needDeploy = true;
            }
            else if (platform == "iOS")
            {
                configurations.Add("iPhone", "iPhone");
                configurations.Add("iPhoneSimulator", "iPhoneSimulator");
                needDeploy = true;
            }
            else if (platform == "Android")
            {
                configurations.Add("Android", "Android");
                needDeploy = true;
            }

            // Update .sln for build configurations
            if (configurations.Count > 0)
            {
                var regex = new Regex(@"^(.*)\|((.*?)(?:\.(.*))?)$");

                var solutionConfigurations = solution.GlobalSections["SolutionConfigurationPlatforms"].Properties
                    .Select(x => Tuple.Create(x, regex.Match(x.Name), regex.Match(x.Value)))
                    .ToArray();

                solution.GlobalSections["SolutionConfigurationPlatforms"].Properties.Clear();

                // Generate solution configurations
                foreach (var solutionConfiguration in solutionConfigurations)
                {
                    var name = solutionConfiguration.Item2;
                    var value = solutionConfiguration.Item3;

                    foreach (var configuration in configurations)
                    {
                        solution.GlobalSections["SolutionConfigurationPlatforms"].Properties.Add(
                            new PropertyItem(name.Groups[1].Value + "|" + configuration.Key,
                                value.Groups[1] + "|" + configuration.Key));
                    }
                }

                // Generate project configurations
                foreach (var context in projectProcessorContexts)
                {
                    // Check if platform is forced (in which case configuration line value should be kept as is)
                    var siliconStudioPlatformNode =
                        context.Document.XPathSelectElement("/x:Project/x:PropertyGroup/x:SiliconStudioPlatform",
                            context.NamespaceManager);

                    // Keep project platform only if SiliconStudioPlatform is set manually to Windows and not a platform specific project
                    bool keepProjectPlatform = (siliconStudioPlatformNode != null && siliconStudioPlatformNode.Value == "Windows") && !context.Modified;

                    // Extract config, we want to parse {78A3E406-EC0E-43B8-8EF2-30D3A149FBB6}.Debug|Mixed Platforms.ActiveCfg = Debug|Any CPU
                    // into:
                    // - {78A3E406-EC0E-43B8-8EF2-30D3A149FBB6}.Debug|
                    // - .ActiveCfg
                    // - Debug|
                    var projectConfigLines = context.Project.PlatformProperties
                        .Select(x => Tuple.Create(x, regex.Match(x.Name), regex.Match(x.Value)))
                        .ToArray();
                    context.Project.PlatformProperties.Clear();

                    var matchingProjectConfigLines = new List<Tuple<PropertyItem, Match, Match>>();

                    foreach (var projectConfigLine in projectConfigLines)
                    {
                        var name = projectConfigLine.Item2;
                        var value = projectConfigLine.Item3;

                        // Simply copy lines that doesn't match pattern
                        if (!name.Success || !name.Groups[4].Success || !value.Success)
                        {
                            context.Project.PlatformProperties.Add(projectConfigLine.Item1);
                        }
                        else
                        {
                            matchingProjectConfigLines.Add(projectConfigLine);
                        }
                    }

                    // Print configuration again
                    foreach (var configuration in configurations)
                    {
                        foreach (var projectConfigLine in matchingProjectConfigLines)
                        {
                            var name = projectConfigLine.Item2;
                            var value = projectConfigLine.Item3;

                            var newName = name.Groups[1].Value + "|" + configuration.Key + "." + name.Groups[4].Value;
                            var newValue = keepProjectPlatform
                                ? projectConfigLine.Item1.Value
                                : value.Groups[1] + "|" + configuration.Value;

                            context.Project.PlatformProperties.Add(new PropertyItem(newName, newValue));

                            // Active Deploy in solution configuration
                            if (needDeploy && !keepProjectPlatform && newName.EndsWith("Build.0"))
                            {
                                context.Project.PlatformProperties.Add(new PropertyItem(newName.Replace("Build.0", "Deploy.0"), newValue));
                            }
                        }
                    }
                }
            }
        }
    }
}
