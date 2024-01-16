// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Configuration.Assemblies;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Build.BackEnd.Logging;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.Build.Framework.Telemetry;
using Microsoft.Build.Shared;

namespace Microsoft.Build.Analyzers
{

#pragma warning disable SA1005 // Single line comments should begin with single space

      //internal class PreEvalData
      //{
      //    public PreEvalData(ProjectRootElement projectElement, Project evaluatedProject, ProjectInstance evaluatedProjectB)
      //    {


    //        evaluatedProject.ReevaluateIfNecessary();
    //        // evaluatedProject.GetItems("item type"); // get***

    //        ProjectInstance projectInstance = evaluatedProject.CreateProjectInstance();

    //        projectInstance.GetItems("item type"); // get***


    //        //evaluatedProject.Items.First().Xml
    //        //evaluatedProjectB.Items.First().

    //        evaluatedProject.GetItems("item type"); // get***
    //        //evaluatedProject.Imports.First().ImportedProject.Location.File
    //        //evaluatedProjectB.ImportPaths
    //        // evaluatedProject.Imports

    //        ProjectProperty pp = evaluatedProject.GetProperty("a");
    //        ProjectPropertyInstance ppi = projectInstance.GetProperty("a");

    //        // Project, ProjectProperty, ProjectItem, ProjectMetadata
    //        //  vs
    //        // ProjectInstance, ProjectPropertyInstance, ProjectItemInstance, ProjectMetadataInstance

    //        // The first group is much more rich in info - specifically it has locations (to distinguish which file it comes from to decide whether to analyze, and for locations themselves - for analyzer results)
    //        // The second group doesn't have anything like that.
    //        // However - first group is not created during evaluation - only on explicit API calls  - e.g.:
    //        //    Microsoft.Build.Evaluation.Project rootProj = new Microsoft.Build.Evaluation.Project(projectElement, evaluatedProjectB.GlobalProperties, evaluatedProjectB.ToolsVersion);
    //        //    rootProj.ReevaluateIfNecessary();
    //        // BUT - this requires reevaluation
    //        // Afaik - there is no cheap way of creating Project from ProjectInstance without reevaluation

    //    }
    //}

    public enum LifeTimeScope
    {
        Stateless,
        PerProject,
        PerBuild,
    }

    public enum Concurency
    {
        Sequential,
        Parallel,
    }

    public enum PerformanceWeightClass
    {
        Lightweight,
        Normal,
        Heavyweight,
    }

    public enum EvaluationAnalysisScope
    {
        AnalyzedProjectOnly,
        AnalyzedProjectWithImportsFromCurrentWorkTree,
        AnalyzedProjectWithImportsWithoutSdks,
        AnalyzedProjectWithAllImports,
    }

    // todo: better naming - configuration by user, from editorconfig
    // should have same values as BuildAnalysisRule - that one provides the default values, this one overrides
    public class BuildAnalyzerConfiguration
    {
        public static BuildAnalyzerConfiguration Null { get; } = new();

        public LifeTimeScope LifeTimeScope { get; internal init; }
        public Concurency Concurency { get; internal init; }
        public PerformanceWeightClass PerformanceWeightClass { get; internal init; }
        public EvaluationAnalysisScope EvaluationAnalysisScope { get; internal init; }
        public BuildAnalysisResultSeverity Severity { get; internal init; }
        public bool IsEnabled { get; internal init; }
    }

    public class BuildAnalyzerConfigurationFromUser
    {
        public static BuildAnalyzerConfigurationFromUser Null { get; } = new();

        public LifeTimeScope? LifeTimeScope { get; init; }
        public Concurency? Concurency { get; init; }
        public PerformanceWeightClass? PerformanceWeightClass { get; init; }
        public EvaluationAnalysisScope? EvaluationAnalysisScope { get; init; }
        public BuildAnalysisResultSeverity? Severity { get; init; }
        public bool? IsEnabled { get; init; }
    }

    public class ConfigurationContext
    {
        // TBD - an advanced way to fetch additional info from editorconfig
        // similar to: https://www.mytechramblings.com/posts/configure-roslyn-analyzers-using-editorconfig/

        public static ConfigurationContext Null { get; } = new();
    }

    public enum BuildAnalysisResultSeverity
    {
        Info,
        Warning,
        Error,
    }

    public class BuildAnalysisRule
    {
        public BuildAnalysisRule(string id, string title, string description, string category, string messageFormat,
            BuildAnalyzerConfiguration defaultConfiguration)
        {
            Id = id;
            Title = title;
            Description = description;
            Category = category;
            MessageFormat = messageFormat;
            DefaultConfiguration = defaultConfiguration;
        }

        public string Id { get; }
        public string Title { get; }
        public string Description { get; }

        // or maybe enum? eval, syntax, etc
        public string Category { get; }
        public string MessageFormat { get; }
        public BuildAnalyzerConfiguration DefaultConfiguration { get; }
    }

    // might need to be dedicated type - to be flexible between error, warning, info
    public class BuildAnalysisResult
    {
        public static BuildAnalysisResult Create(BuildAnalysisRule rule, ElementLocation location, params string[] messageArgs)
        {
            return new BuildAnalysisResult(rule, location, messageArgs);
        }

        public BuildAnalysisResult(BuildAnalysisRule buildAnalysisRule, ElementLocation location, string[] messageArgs)
        {
            BuildAnalysisRule = buildAnalysisRule;
            Location = location;
            MessageArgs = messageArgs;
        }

        internal BuildEventArgs ToEventArgs(BuildAnalysisResultSeverity severity)
            => severity switch
            {
                BuildAnalysisResultSeverity.Info => new BuildAnalysisResultMessage(this),
                BuildAnalysisResultSeverity.Warning => new BuildAnalysisResultWarning(this),
                BuildAnalysisResultSeverity.Error => new BuildAnalysisResultError(this),
                _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null),
            };

        public BuildAnalysisRule BuildAnalysisRule { get; }
        public ElementLocation Location { get; }
        public string[] MessageArgs { get; }

        private string? _message;
        public string Message => _message ??= $"{BuildAnalysisRule.Id}: {string.Format(BuildAnalysisRule.MessageFormat, MessageArgs)}";
    }

    public sealed class BuildAnalysisResultWarning : BuildWarningEventArgs
    {
        public BuildAnalysisResultWarning(BuildAnalysisResult result)
        {
            this.Message = result.Message;
        }


        internal override void WriteToStream(BinaryWriter writer)
        {
            base.WriteToStream(writer);

            // TODO: TBD
            writer.Write(Message!);
        }

        internal override void CreateFromStream(BinaryReader reader, int version)
        {
            base.CreateFromStream(reader, version);

            // TODO: TBD
            Message = reader.ReadString();
        }

        public override string? Message { get; protected set; }
    }

    public sealed class BuildAnalysisResultError : BuildErrorEventArgs
    {
        public BuildAnalysisResultError(BuildAnalysisResult result)
        {
            this.Message = result.Message;
        }


        internal override void WriteToStream(BinaryWriter writer)
        {
            base.WriteToStream(writer);

            // TODO: TBD
            writer.Write(Message!);
        }

        internal override void CreateFromStream(BinaryReader reader, int version)
        {
            base.CreateFromStream(reader, version);

            // TODO: TBD
            Message = reader.ReadString();
        }

        public override string? Message { get; protected set; }
    }

    public sealed class BuildAnalysisResultMessage : BuildMessageEventArgs
    {
        public BuildAnalysisResultMessage(BuildAnalysisResult result)
        {
            this.Message = result.Message;
        }


        internal override void WriteToStream(BinaryWriter writer)
        {
            base.WriteToStream(writer);

            // TODO: TBD
            writer.Write(Message!);
        }

        internal override void CreateFromStream(BinaryReader reader, int version)
        {
            base.CreateFromStream(reader, version);

            // TODO: TBD
            Message = reader.ReadString();
        }

        public override string? Message { get; protected set; }
    }

    public class BuildAnalysisContext
    {
        private readonly LoggingContext _loggingContext;

        internal BuildAnalysisContext(LoggingContext loggingContext) => _loggingContext = loggingContext;

        // TODO: make those immutable - we'll need context per invocation (can we somehow overcome that? E.g. hide data in the BuildAnalysisResult)
        //  one way might be to have a lookup rule->configuration within the infra, other is to attach the configuration to the rule during creation of analyzer
        internal BuildAnalyzer? BuildAnalyzer { get; set; }
        internal BuildAnalyzerConfiguration? BuildAnalyzerConfiguration { get; set; }

        public void ReportResult(BuildAnalysisResult result)
        {
            BuildEventArgs eventArgs = result.ToEventArgs(BuildAnalyzerConfiguration!.Severity);
            eventArgs.BuildEventContext = _loggingContext.BuildEventContext;
            _loggingContext.LogBuildEvent(eventArgs);
        }
    }

    public class DocumentAnalysisContext : BuildAnalysisContext
    {
        internal DocumentAnalysisContext(LoggingContext loggingContext, ProjectRootElement projectRootElement, bool isImported) :
            base(loggingContext) => (ProjectRootElement, IsImported) = (projectRootElement, isImported);

        public ProjectRootElement ProjectRootElement { get; }

        public bool IsImported { get; }
    }

    public class ConditionAnalysisContext : BuildAnalysisContext
    {
        internal ConditionAnalysisContext(LoggingContext loggingContext, ProjectElement elementWitCondition) :
            base(loggingContext) => ElementWitCondition = elementWitCondition;

        public ProjectElement ElementWitCondition { get; }
    }

    public class EvaluatedPropertiesContext : BuildAnalysisContext
    {
        internal EvaluatedPropertiesContext(
            LoggingContext loggingContext,
            IReadOnlyDictionary<string, string> evaluatedProperties,
            string projectFilePath) :
            base(loggingContext) => (EvaluatedProperties, ProjectFilePath) = (evaluatedProperties, projectFilePath);

        public IReadOnlyDictionary<string, string> EvaluatedProperties { get; }

        public string ProjectFilePath { get; }
    }

    public delegate void DocumentAction(DocumentAnalysisContext context);
    public delegate void ConditionAction(ConditionAnalysisContext context);
    public delegate void EvaluatedPropertiesAction(EvaluatedPropertiesContext context);

    internal record AnalyzerDocumentAction(
        DocumentAction DocumentAction,
        BuildAnalyzer Analyzer,
        BuildAnalyzerConfiguration Configuration);

    internal record AnalyzerConditionAction(
        ConditionAction ConditionAction,
        BuildAnalyzer Analyzer,
        BuildAnalyzerConfiguration Configuration);

    internal record AnalyzerEvaluatedPropertiesAction(
        EvaluatedPropertiesAction EvaluatedPropertiesAction,
        BuildAnalyzer Analyzer,
        BuildAnalyzerConfiguration Configuration);

    internal class CentralBuildAnalyzerContext
    {
        private readonly List<AnalyzerDocumentAction> _documentActions = new();
        private readonly List<AnalyzerConditionAction> _conditionActions = new();
        private readonly List<AnalyzerEvaluatedPropertiesAction> _evaluatedPropertiesActions = new();

        // todo: other optional arguments to filter in/out what to analyze
        internal void RegisterDocumentAction(AnalyzerDocumentAction documentAction)
        {
            // tbd
            _documentActions.Add(documentAction);
        }

        internal void RegisterConditionAction(AnalyzerConditionAction conditionAction)
        {
            // tbd
            _conditionActions.Add(conditionAction);
        }

        internal void RegisterEvaluatedPropertiesAction(AnalyzerEvaluatedPropertiesAction evaluatedPropertiesAction)
        {
            // tbd
            _evaluatedPropertiesActions.Add(evaluatedPropertiesAction);
        }

        internal void RunEvaluatedPropertiesActions(EvaluatedPropertiesContext evaluatedPropertiesContext)
        {
            foreach (var analyzerEvaluatedPropertiesAction in _evaluatedPropertiesActions)
            {
                evaluatedPropertiesContext.BuildAnalyzer = analyzerEvaluatedPropertiesAction.Analyzer;
                evaluatedPropertiesContext.BuildAnalyzerConfiguration = analyzerEvaluatedPropertiesAction.Configuration;

                analyzerEvaluatedPropertiesAction.EvaluatedPropertiesAction(evaluatedPropertiesContext);
            }
        }

        internal void RunDocumentActions(DocumentAnalysisContext documentAnalysisContext)
        {
            foreach (var documentAction in _documentActions)
            {
                documentAnalysisContext.BuildAnalyzer = documentAction.Analyzer;
                documentAnalysisContext.BuildAnalyzerConfiguration = documentAction.Configuration;

                //TODO: filtering as needed (disabled/enbled etc.)
                documentAction.DocumentAction(documentAnalysisContext);
            }
        }

        internal void RunConditionActions(ConditionAnalysisContext conditionAnalysisContext)
        {
            //Debugger.Launch();

            foreach (var conditionAction in _conditionActions)
            {
                conditionAnalysisContext.BuildAnalyzer = conditionAction.Analyzer;
                conditionAnalysisContext.BuildAnalyzerConfiguration = conditionAction.Configuration;

                // todo: we need to properly crossmatch the scope of conditionAction.Configuration.EvaluationAnalysisScope and conditionAnalysisContext.ElementWitCondition
                bool isImported = !conditionAnalysisContext.ElementWitCondition.ContainingProject
                    .IsExplicitlyLoaded;

                bool shouldProcess =
                    conditionAction.Configuration.EvaluationAnalysisScope switch
                    {
                        EvaluationAnalysisScope.AnalyzedProjectOnly => !IsImported(conditionAnalysisContext.ElementWitCondition.Location.File),
                        EvaluationAnalysisScope.AnalyzedProjectWithImportsFromCurrentWorkTree => IsPartOfWorkTree(conditionAnalysisContext.ElementWitCondition.Location.File),
                        EvaluationAnalysisScope.AnalyzedProjectWithImportsWithoutSdks => !IsPartOfSdk(conditionAnalysisContext.ElementWitCondition.Location.File),
                        EvaluationAnalysisScope.AnalyzedProjectWithAllImports => true,
                        _ => throw new ArgumentOutOfRangeException(nameof(conditionAction.Configuration.EvaluationAnalysisScope), conditionAction.Configuration.EvaluationAnalysisScope, null),
                    };

                if (shouldProcess)
                {
                    conditionAction.ConditionAction(conditionAnalysisContext);
                }
            }
        }

        // TODO: implement those

        //hack
        internal static string? CurrentProjectPath;

        private bool IsImported(string path)
        {
            return !path.Equals(CurrentProjectPath, StringComparison.CurrentCultureIgnoreCase);
        }

        private bool IsPartOfWorkTree(string path)
        {
            return false;
        }

        private bool IsPartOfSdk(string path)
        {
            return true;
        }
    }

    public class BuildAnalyzerContext
    {
        private readonly BuildAnalyzer _analyzer;
        private readonly BuildAnalyzerConfiguration _configuration;
        private readonly CentralBuildAnalyzerContext _centralContext;

        internal BuildAnalyzerContext(BuildAnalyzer analyzer, BuildAnalyzerConfiguration configuration,
            CentralBuildAnalyzerContext centralContext)
        {
            _analyzer = analyzer;
            _configuration = configuration;
            _centralContext = centralContext;
        }

        // todo: other optional arguments to filter in/out what to analyze
        public void RegisterDocumentAction(DocumentAction documentAction)
        {
            if (_configuration.LifeTimeScope >= LifeTimeScope.PerBuild)
            {
                throw new InvalidOperationException(
                    "DocumentAction cannot be registered with LifeTimeScope >= PerBuild");
            }

            // tbd
            _centralContext.RegisterDocumentAction(new AnalyzerDocumentAction(documentAction, _analyzer, _configuration));
        }

        public void RegisterConditionAction(ConditionAction conditionAction)
        {
            if (_configuration.LifeTimeScope >= LifeTimeScope.PerBuild)
            {
                throw new InvalidOperationException(
                    "ConditionAction cannot be registered with LifeTimeScope >= PerBuild");
            }

            // tbd
            _centralContext.RegisterConditionAction(new AnalyzerConditionAction(conditionAction, _analyzer, _configuration));
        }

        public void RegisterEvaluatedPropertiesAction(EvaluatedPropertiesAction evaluatedPropertiesAction)
        {
            _centralContext.RegisterEvaluatedPropertiesAction(
                new AnalyzerEvaluatedPropertiesAction(evaluatedPropertiesAction, _analyzer, _configuration));
        }
    }

    public abstract class BuildAnalyzer
    {
        public abstract ImmutableArray<BuildAnalysisRule> SupportedRules { get; }
        public abstract void Initialize(ConfigurationContext configurationContext/*BuildAnalyzerConfiguration configuration*/);

        public abstract void RegisterActions(BuildAnalyzerContext context);
    }

    internal enum BuildAnalysisPhase
    {
        PreEvaluation,
        Evaluation,
        PostEvaluation,
    }

    internal enum DocumentType
    {
        DirectlyEvaluated,
        Imported,
    }

    internal static class ConfigurationProvider
    {
        private static Dictionary<string, BuildAnalyzerConfigurationFromUser> _userConfig = LoadConfiguration();

        private static Dictionary<string, BuildAnalyzerConfigurationFromUser> LoadConfiguration()
        {
            const string configFileName = "editorconfig.json";
            string configPath = configFileName;

            if (!File.Exists(configPath))
            {
                string? dir;
                if (!string.IsNullOrEmpty(dir = Path.GetDirectoryName(CentralBuildAnalyzerContext.CurrentProjectPath)))
                {
                    configPath =
                        Path.Combine(dir, configFileName);
                }

                if (!File.Exists(configPath))
                {
                    return new Dictionary<string, BuildAnalyzerConfigurationFromUser>();
                }
            }

            var json = File.ReadAllText(configPath);
            var DeserializationOptions = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
            return JsonSerializer.Deserialize<Dictionary<string, BuildAnalyzerConfigurationFromUser>>(json, DeserializationOptions) ??
                   new Dictionary<string, BuildAnalyzerConfigurationFromUser>();
        }

        public static BuildAnalyzerConfiguration GetMergedConfiguration(BuildAnalyzer analyzer)
        {
            if (!_userConfig.TryGetValue(analyzer.SupportedRules[0].Id, out BuildAnalyzerConfigurationFromUser? config))
            {
                config = BuildAnalyzerConfigurationFromUser.Null;
            }

            var defualtConfig = analyzer.SupportedRules[0].DefaultConfiguration;

            return new BuildAnalyzerConfiguration()
            {
                Concurency = config.Concurency ?? defualtConfig.Concurency,
                EvaluationAnalysisScope = config.EvaluationAnalysisScope ?? defualtConfig.EvaluationAnalysisScope,
                IsEnabled = config.IsEnabled ?? defualtConfig.IsEnabled,
                LifeTimeScope = config.LifeTimeScope ?? defualtConfig.LifeTimeScope,
                PerformanceWeightClass = config.PerformanceWeightClass ?? defualtConfig.PerformanceWeightClass,
                Severity = config.Severity ?? defualtConfig.Severity
            };

            //return analyzer.SupportedRules[0].Id switch
            //{
            //    "ABC123" => new BuildAnalyzerConfiguration
            //    {
            //        IsEnabled = true,
            //        Severity = BuildAnalysisResultSeverity.Warning,
            //    },
            //    "COND0543" => new BuildAnalyzerConfiguration
            //    {
            //        IsEnabled = true,
            //        Severity = BuildAnalysisResultSeverity.Error,
            //        EvaluationAnalysisScope = EvaluationAnalysisScope.AnalyzedProjectOnly,
            //    },
            //    _ => BuildAnalyzerConfiguration.Null,
            //};
        }
    }

    // TODO: register in some service provider to get rid of statics
    public class BuildAnalysisManager
    {
        private readonly List<BuildAnalyzer> _analyzers = new();
        internal LoggingContext? LoggingContext { get; set; }
        private readonly CentralBuildAnalyzerContext _centralContext = new();

        public void RegisterAnalyzer(BuildAnalyzer analyzer)
        {
            //TBD: fetch configuration per analyzer
            //TODO: single analyzer can have multiple rules - need to properly reflect that in config and merging
            // then merge it with analyzer.SupportedRules
            // following is just for illustration
            BuildAnalyzerConfiguration configuration = ConfigurationProvider.GetMergedConfiguration(analyzer);

            if (!configuration.IsEnabled)
            {
                return;
            }

            ConfigurationContext configurationContext = ConfigurationContext.Null;
            analyzer.Initialize(configurationContext);
            analyzer.RegisterActions(new BuildAnalyzerContext(analyzer, configuration, _centralContext));
            _analyzers.Add(analyzer);
        }

        // TODO: all this processing should be queued and done async. We might even want to run analyzers in parallel

        internal void ProcessPreEvaluationData(ProjectRootElement projectElement, bool isImported)
        {
            if (LoggingContext == null)
            {
                // error out
                return;
            }

            DocumentAnalysisContext context = new DocumentAnalysisContext(LoggingContext, projectElement, isImported);
            _centralContext.RunDocumentActions(context);
        }

        internal void ProcessEvaluationData(ProjectInstance evaluatedProject)
        {
            if (LoggingContext == null)
            {
                // error out
                return;
            }

            // TBD
        }

        internal void ProcessEvaluationCondition(ProjectElement elementWitCondition)
        {
            if (LoggingContext == null)
            {
                // error out
                return;
            }

            ConditionAnalysisContext context = new ConditionAnalysisContext(LoggingContext, elementWitCondition);
            _centralContext.RunConditionActions(context);
        }

        internal void ProcessEvaluationFinishedEventArgs(ProjectEvaluationFinishedEventArgs evaluationFinishedEventArgs)
        {
            if (LoggingContext == null)
            {
                // error out
                return;
            }

            Dictionary<string, string> propertiesLookup = new Dictionary<string, string>();
            Internal.Utilities.EnumerateProperties(evaluationFinishedEventArgs.Properties, propertiesLookup,
                static (dict, kvp) => dict.Add(kvp.Key, kvp.Value));

            EvaluatedPropertiesContext context = new EvaluatedPropertiesContext(LoggingContext,
                new ReadOnlyDictionary<string, string>(propertiesLookup),
                evaluationFinishedEventArgs.ProjectFile!);

            _centralContext.RunEvaluatedPropertiesActions(context);
        }

        internal static BuildAnalysisManager CreateBuildAnalysisManager()
        {
            var buildAnalysisManager = new BuildAnalysisManager();
            buildAnalysisManager.RegisterAnalyzer(new MySampleSyntaxAnalyzer());
            buildAnalysisManager.RegisterAnalyzer(new MyConditionAnalyzer());
            buildAnalysisManager.RegisterAnalyzer(new MyEvalFinishedAnalyzer());
            return buildAnalysisManager;
        }
    }

    public class MySampleSyntaxAnalyzer : BuildAnalyzer
    {

        public override void Initialize(ConfigurationContext configurationContext/*BuildAnalyzerConfiguration configuration*/)
        {
            // TBD
        }

        // TODO: should be an enumeration/list of rules
        public static BuildAnalysisRule SupportedRule = new BuildAnalysisRule("ABC123", "InapropriateFileName",
            "The filename contains forbidden token", "Naming", "The build file [{0}] contains forbidden token [{1}]",
            new BuildAnalyzerConfiguration() { Severity = BuildAnalysisResultSeverity.Warning, IsEnabled = true });

        public override ImmutableArray<BuildAnalysisRule> SupportedRules { get; } =[SupportedRule];
            //ImmutableArray<BuildAnalysisRule>.Create(SupportedRule);

        public override void RegisterActions(BuildAnalyzerContext context)
        {
            context.RegisterDocumentAction(DocumentAction);
        }

        private void DocumentAction(DocumentAnalysisContext context)
        {
            if (!context.IsImported && Path.GetFileName(context.ProjectRootElement.FullPath).Contains("Foo"))
            {
                //Debugger.Launch();

                context.ReportResult(BuildAnalysisResult.Create(
                    SupportedRule,
                    context.ProjectRootElement.Location,
                    context.ProjectRootElement.FullPath,
                    "Foo"));
            }
        }
    }

    public class MyConditionAnalyzer : BuildAnalyzer
    {
        public override void Initialize(ConfigurationContext configurationContext)
        {
            // TBD
        }

        // TODO: should be an enumeration/list of rules
        public static BuildAnalysisRule SupportedRule = new BuildAnalysisRule("COND0543", "WrongConditionQuotation",
            "The condition is not correctly enquoted", "Syntax",
            "The condition is not correctly enquoted:" + Environment.NewLine + "{0}",
            new BuildAnalyzerConfiguration() { Severity = BuildAnalysisResultSeverity.Warning, IsEnabled = true });

        public override ImmutableArray<BuildAnalysisRule> SupportedRules { get; } =[SupportedRule];

        public override void RegisterActions(BuildAnalyzerContext context)
        {
            context.RegisterConditionAction(ConditionAction);
        }

        private void ConditionAction(ConditionAnalysisContext context)
        {
            if (!context.ElementWitCondition.Condition.Contains('\''))
            {
                context.ReportResult(BuildAnalysisResult.Create(
                    SupportedRule,
                    context.ElementWitCondition.ConditionLocation ?? context.ElementWitCondition.Location,
                    context.ElementWitCondition.Condition));
            }
        }
    }

    public class MyEvalFinishedAnalyzer : BuildAnalyzer
    {
        public static BuildAnalysisRule SupportedRule = new BuildAnalysisRule("EVL0543", "ConflictingOutputPath",
            "Two projects should not share their OutputPath nor IntermediateOutputPath locations", "Configuration",
            "Projects {0} and {1} have conflicting output paths: {2}.",
            new BuildAnalyzerConfiguration() { Severity = BuildAnalysisResultSeverity.Error, IsEnabled = true });

        public override ImmutableArray<BuildAnalysisRule> SupportedRules { get; } =[SupportedRule];

        public override void Initialize(ConfigurationContext configurationContext)
        {
            // TBD
        }

        public override void RegisterActions(BuildAnalyzerContext context)
        {
            context.RegisterEvaluatedPropertiesAction(EvaluatedPropertiesAction);
        }

        private Dictionary<string, string> _projectsPerOutputPath = new(StringComparer.CurrentCultureIgnoreCase);
        private HashSet<string> _projects = new(StringComparer.CurrentCultureIgnoreCase);

        private void EvaluatedPropertiesAction(EvaluatedPropertiesContext context)
        {
            if (!_projects.Add(context.ProjectFilePath))
            {
                return;
            }

            string? binPath, objPath;

            context.EvaluatedProperties.TryGetValue("OutputPath", out binPath);
            context.EvaluatedProperties.TryGetValue("IntermediateOutputPath", out objPath);

            //string binPath = context.EvaluatedProperties["OutputPath"];
            //string objPath = context.EvaluatedProperties["IntermediateOutputPath"];

            string? absoluteBinPath = CheckAndAddFullOutputPath(binPath, context);
            if (
                !string.IsNullOrEmpty(objPath) && !string.IsNullOrEmpty(absoluteBinPath) &&
                !objPath.Equals(binPath, StringComparison.CurrentCultureIgnoreCase)
                && !objPath.Equals(absoluteBinPath, StringComparison.CurrentCultureIgnoreCase)
            )
            {
                CheckAndAddFullOutputPath(objPath, context);
            }
        }

        private string? CheckAndAddFullOutputPath(string? path, EvaluatedPropertiesContext context)
        {
            // Debugger.Launch();

            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            string projectPath = context.ProjectFilePath;

            if (!Path.IsPathRooted(path))
            {
                path = Path.Combine(Path.GetDirectoryName(projectPath)!, path);
            }

            if (_projectsPerOutputPath.TryGetValue(path!, out string? conflictingProject))
            {
                context.ReportResult(BuildAnalysisResult.Create(
                    SupportedRule,
                    ElementLocation.EmptyLocation,
                    Path.GetFileName(projectPath),
                    Path.GetFileName(conflictingProject),
                    path!));
            }
            else
            {
                _projectsPerOutputPath[path!] = projectPath;
            }

            return path;
        }
    }

#pragma warning restore SA1005 // Single line comments should begin with single space
}
