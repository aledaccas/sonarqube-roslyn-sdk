﻿using Microsoft.CodeAnalysis.Diagnostics;
using NuGet;
using Roslyn.SonarQube.PluginGenerator;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Roslyn.SonarQube.AnalyzerPlugins
{
    public class AnalyzerPluginGenerator
    {
        public const string NuGetPackageSource = "https://www.nuget.org/api/v2/";

        private readonly Common.ILogger logger;

        public AnalyzerPluginGenerator(Common.ILogger logger)
        {
            if (logger == null)
            {
                throw new ArgumentNullException("logger");
            }
            this.logger = logger;
        }

        public bool Generate(string nuGetPackageId, SemanticVersion nuGetPackageVersion)
        {
            if (string.IsNullOrWhiteSpace(nuGetPackageId))
            {
                throw new ArgumentNullException("nuGetPackageId");
            }

            string baseDirectory = Path.Combine(
                Path.GetTempPath(),
                Assembly.GetEntryAssembly().GetName().Name);

            string nuGetDirectory = Path.Combine(baseDirectory, ".nuget");

            NuGetPackageHandler downloader = new NuGetPackageHandler(logger);

            IPackage package = downloader.FetchPackage(NuGetPackageSource, nuGetPackageId, nuGetPackageVersion, nuGetDirectory);

            if (package != null)
            {
                PluginDefinition pluginDefn = CreatePluginDefinition(package);

                string outputDirectory = Path.Combine(baseDirectory, ".output", Guid.NewGuid().ToString());
                Directory.CreateDirectory(outputDirectory);

                string outputFilePath = Path.Combine(outputDirectory, "rules.xml");

                string packageDirectory = Path.Combine(nuGetDirectory, package.Id + "." + package.Version.ToString());
                Debug.Assert(Directory.Exists(packageDirectory), "Expected package directory does not exist: {0}", packageDirectory);
                bool success = TryGenerateRulesFile(packageDirectory, nuGetDirectory, outputFilePath);

                if (success)
                {
                    this.logger.LogInfo(UIResources.APG_GeneratingPlugin);

                    string fullJarPath = Path.Combine(Directory.GetCurrentDirectory(), 
                        nuGetPackageId + "-plugin." + pluginDefn.Version + ".jar");
                    RulesPluginGenerator rulesPluginGen = new RulesPluginGenerator(logger);
                    rulesPluginGen.GeneratePlugin(pluginDefn, outputFilePath, fullJarPath);

                    this.logger.LogInfo(UIResources.APG_PluginGenerated, fullJarPath);
                }
            }

            return package != null;
        }

        /// <summary>
        /// Attempts to generate a rules file for assemblies in the package directory.
        /// Returns the path to the rules file.
        /// </summary>
        /// <param name="packageDirectory">Directory containing the analyzer assembly to generate rules for</param>
        /// <param name="nuGetDirectory">Directory containing other NuGet packages that might be required i.e. analyzer dependencies</param>
        private bool TryGenerateRulesFile(string packageDirectory, string nuGetDirectory, string outputFilePath)
        {
            bool success = false;
            this.logger.LogInfo(UIResources.APG_GeneratingRules);

            this.logger.LogInfo(UIResources.APG_LocatingAnalyzers);

            AnalyzerFinder finder = new AnalyzerFinder(this.logger);
            IEnumerable<DiagnosticAnalyzer> analyzers = finder.FindAnalyzers(packageDirectory, nuGetDirectory);

            this.logger.LogInfo(UIResources.APG_AnalyzersLocated, analyzers.Count());

            if (analyzers.Any())
            {
                RuleGenerator ruleGen = new RuleGenerator(this.logger);
                Rules rules = ruleGen.GenerateRules(analyzers);

                Debug.Assert(rules != null, "Not expecting the generated rules to be null");

                if (rules != null)
                {
                    rules.Save(outputFilePath, logger);
                    this.logger.LogDebug(UIResources.APG_RulesGeneratedToFile, rules.Count, outputFilePath);
                    success = true;
                }
            }
            else
            {
                this.logger.LogWarning(UIResources.APG_NoAnalyzersFound);
            }
            return success;
        }

        private static PluginDefinition CreatePluginDefinition(IPackage package)
        {
            PluginDefinition pluginDefn = new PluginDefinition();

            pluginDefn.Description = GetValidManifestString(package.Description);
            pluginDefn.Developers = GetValidManifestString(ListToString(package.Authors));

            pluginDefn.Homepage = GetValidManifestString(package.ProjectUrl?.ToString());
            pluginDefn.Key = GetValidManifestString(package.Id);

            // TODO: hard-coded to C#
            pluginDefn.Language = "cs";
            pluginDefn.Name = GetValidManifestString(package.Title) ?? pluginDefn.Key;
            pluginDefn.Organization = GetValidManifestString(ListToString(package.Owners));
            pluginDefn.Version = GetValidManifestString(package.Version.ToNormalizedString());

            //pluginDefn.IssueTrackerUrl
            //pluginDefn.License;
            //pluginDefn.SourcesUrl;
            //pluginDefn.TermsConditionsUrl;

            return pluginDefn;
        }

        private static string GetValidManifestString(string value)
        {
            string valid = value;

            if (valid != null)
            {
                valid = valid.Replace('\r', ' ');
                valid = valid.Replace('\n', ' ');
            }
            return valid;
        }

        private static string ListToString(IEnumerable<string> args)
        {
            if (args == null)
            {
                return null;
            }
            return string.Join(",", args);
        }
    }
}