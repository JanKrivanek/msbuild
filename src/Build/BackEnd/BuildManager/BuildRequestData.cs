﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Build.Collections;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Experimental.BuildCheck;
using Microsoft.Build.Shared;

namespace Microsoft.Build.Execution
{
    /// <summary>
    /// Flags providing additional control over the build request
    /// </summary>
    [Flags]
    public enum BuildRequestDataFlags
    {
        /// <summary>
        /// No flags.
        /// </summary>
        None = 0,

        /// <summary>
        /// When this flag is present, the existing ProjectInstance in the build will be replaced by this one.
        /// </summary>
        ReplaceExistingProjectInstance = 1 << 0,

        /// <summary>
        /// When this flag is present, the <see cref="BuildResult"/> issued in response to this request will
        /// include <see cref="BuildResult.ProjectStateAfterBuild"/>.
        /// </summary>
        ProvideProjectStateAfterBuild = 1 << 1,

        /// <summary>
        /// When this flag is present and the project has previously been built on a node whose affinity is
        /// incompatible with the affinity this request requires, we will ignore the project state (but not
        /// target results) that were previously generated.
        /// </summary>
        /// <remarks>
        /// This usually is not desired behavior.  It is only provided for those cases where the client
        /// knows that the new build request does not depend on project state generated by a previous request.  Setting
        /// this flag can provide a performance boost in the case of incompatible node affinities, as MSBuild would
        /// otherwise have to serialize the project state from one node to another, which may be
        /// expensive depending on how much data the project previously generated.
        ///
        /// This flag has no effect on target results, so if a previous request already built a target, the new
        /// request will not re-build that target (nor will any of the project state mutations which previously
        /// occurred as a consequence of building that target be re-applied.)
        /// </remarks>
        IgnoreExistingProjectState = 1 << 2,

        /// <summary>
        /// When this flag is present, caches including the <see cref="ProjectRootElementCacheBase"/> will be cleared
        /// after the build request completes.  This is used when the build request is known to modify a lot of
        /// state such as restoring packages or generating parts of the import graph.
        /// </summary>
        ClearCachesAfterBuild = 1 << 3,

        /// <summary>
        /// When this flag is present, the top level target(s) in the build request will be skipped if those targets
        /// are not defined in the Project to build. This only applies to this build request (if another target calls
        /// the "missing target" at any other point this will still result in an error).
        /// </summary>
        SkipNonexistentTargets = 1 << 4,

        /// <summary>
        /// When this flag is present, the <see cref="BuildResult"/> issued in response to this request will
        /// include a <see cref="BuildResult.ProjectStateAfterBuild"/> that includes ONLY the
        /// explicitly-requested properties, items, and metadata.
        /// </summary>
        ProvideSubsetOfStateAfterBuild = 1 << 5,

        /// <summary>
        /// When this flag is present, projects loaded during build will ignore missing imports (<see cref="ProjectLoadSettings.IgnoreMissingImports"/> and <see cref="ProjectLoadSettings.IgnoreInvalidImports"/>).
        /// This is especially useful during a restore since some imports might come from packages that haven't been restored yet.
        /// </summary>
        IgnoreMissingEmptyAndInvalidImports = 1 << 6,

        /// <summary>
        /// When this flag is present, an unresolved MSBuild project SDK will fail the build.  This flag is used to
        /// change the <see cref="IgnoreMissingEmptyAndInvalidImports" /> behavior to still fail when an SDK is missing
        /// because those are more fatal.
        /// </summary>
        FailOnUnresolvedSdk = 1 << 7,
    }

    /// <summary>
    /// BuildRequestData encapsulates all the data needed to submit a build request.
    /// </summary>
    public sealed class BuildRequestData : BuildRequestData<BuildRequestData, BuildResult>
    {
        /// <summary>
        /// Constructs a BuildRequestData for build requests based on project instances.
        /// </summary>
        /// <param name="projectInstance">The instance to build.</param>
        /// <param name="targetsToBuild">The targets to build.</param>
        public BuildRequestData(ProjectInstance projectInstance, string[] targetsToBuild)
            : this(projectInstance, targetsToBuild, null, BuildRequestDataFlags.None)
        {
        }

        /// <summary>
        /// Constructs a BuildRequestData for build requests based on project instances.
        /// </summary>
        /// <param name="projectInstance">The instance to build.</param>
        /// <param name="targetsToBuild">The targets to build.</param>
        /// <param name="hostServices">The host services to use, if any.  May be null.</param>
        public BuildRequestData(ProjectInstance projectInstance, string[] targetsToBuild, HostServices hostServices)
            : this(projectInstance, targetsToBuild, hostServices, BuildRequestDataFlags.None)
        {
        }

        /// <summary>
        /// Constructs a BuildRequestData for build requests based on project instances.
        /// </summary>
        /// <param name="projectInstance">The instance to build.</param>
        /// <param name="targetsToBuild">The targets to build.</param>
        /// <param name="hostServices">The host services to use, if any.  May be null.</param>
        /// <param name="flags">Flags controlling this build request.</param>
        public BuildRequestData(ProjectInstance projectInstance, string[] targetsToBuild, HostServices? hostServices, BuildRequestDataFlags flags)
            : this(projectInstance, targetsToBuild, hostServices, flags, null)
        {
        }

        /// <summary>
        /// Constructs a BuildRequestData for build requests based on project instances.
        /// </summary>
        /// <param name="projectInstance">The instance to build.</param>
        /// <param name="targetsToBuild">The targets to build.</param>
        /// <param name="hostServices">The host services to use, if any.  May be null.</param>
        /// <param name="flags">Flags controlling this build request.</param>
        /// <param name="propertiesToTransfer">The list of properties whose values should be transferred from the project to any out-of-proc node.</param>
        public BuildRequestData(ProjectInstance projectInstance, string[] targetsToBuild, HostServices? hostServices, BuildRequestDataFlags flags, IEnumerable<string>? propertiesToTransfer)
            : this(targetsToBuild, hostServices, flags, projectInstance.FullPath)
        {
            ErrorUtilities.VerifyThrowArgumentNull(projectInstance, nameof(projectInstance));

            foreach (string targetName in targetsToBuild)
            {
                ErrorUtilities.VerifyThrowArgumentNull(targetName, "target");
            }

            ProjectInstance = projectInstance;

            GlobalPropertiesDictionary = projectInstance.GlobalPropertiesDictionary;
            ExplicitlySpecifiedToolsVersion = projectInstance.ExplicitToolsVersion;
            if (propertiesToTransfer != null)
            {
                PropertiesToTransfer = new List<string>(propertiesToTransfer);
            }
        }

        /// <summary>
        /// Constructs a BuildRequestData for build requests based on project instances.
        /// </summary>
        /// <param name="projectInstance">The instance to build.</param>
        /// <param name="targetsToBuild">The targets to build.</param>
        /// <param name="hostServices">The host services to use, if any.  May be null.</param>
        /// <param name="flags">Flags controlling this build request.</param>
        /// <param name="propertiesToTransfer">The list of properties whose values should be transferred from the project to any out-of-proc node.</param>
        /// <param name="requestedProjectState">A <see cref="Execution.RequestedProjectState"/> describing properties, items, and metadata that should be returned. Requires setting <see cref="BuildRequestDataFlags.ProvideSubsetOfStateAfterBuild"/>.</param>
        public BuildRequestData(ProjectInstance projectInstance, string[] targetsToBuild, HostServices? hostServices, BuildRequestDataFlags flags, IEnumerable<string>? propertiesToTransfer, RequestedProjectState requestedProjectState)
            : this(projectInstance, targetsToBuild, hostServices, flags, propertiesToTransfer)
        {
            ErrorUtilities.VerifyThrowArgumentNull(requestedProjectState, nameof(requestedProjectState));

            RequestedProjectState = requestedProjectState;
        }


        /// <summary>
        /// Constructs a BuildRequestData for build requests based on project files.
        /// </summary>
        /// <param name="projectFullPath">The full path to the project file.</param>
        /// <param name="globalProperties">The global properties which should be used during evaluation of the project.  Cannot be null.</param>
        /// <param name="toolsVersion">The tools version to use for the build.  May be null.</param>
        /// <param name="targetsToBuild">The targets to build.</param>
        /// <param name="hostServices">The host services to use.  May be null.</param>
        public BuildRequestData(string projectFullPath, IDictionary<string, string?> globalProperties, string? toolsVersion, string[] targetsToBuild, HostServices? hostServices)
            : this(projectFullPath, globalProperties, toolsVersion, targetsToBuild, hostServices, BuildRequestDataFlags.None)
        {
        }

        /// <summary>
        /// Constructs a BuildRequestData for build requests based on project files.
        /// </summary>
        /// <param name="projectFullPath">The full path to the project file.</param>
        /// <param name="globalProperties">The global properties which should be used during evaluation of the project.  Cannot be null.</param>
        /// <param name="toolsVersion">The tools version to use for the build.  May be null.</param>
        /// <param name="targetsToBuild">The targets to build.</param>
        /// <param name="hostServices">The host services to use.  May be null.</param>
        /// <param name="flags">The <see cref="BuildRequestDataFlags"/> to use.</param>
        /// <param name="requestedProjectState">A <see cref="Execution.RequestedProjectState"/> describing properties, items, and metadata that should be returned. Requires setting <see cref="BuildRequestDataFlags.ProvideSubsetOfStateAfterBuild"/>.</param>
        public BuildRequestData(string projectFullPath, IDictionary<string, string?> globalProperties,
            string? toolsVersion, string[] targetsToBuild, HostServices? hostServices, BuildRequestDataFlags flags,
            RequestedProjectState requestedProjectState)
            : this(projectFullPath, globalProperties, toolsVersion, targetsToBuild, hostServices, flags)
        {
            ErrorUtilities.VerifyThrowArgumentNull(requestedProjectState, nameof(requestedProjectState));

            RequestedProjectState = requestedProjectState;
        }

        /// <summary>
        /// Constructs a BuildRequestData for build requests based on project files.
        /// </summary>
        /// <param name="projectFullPath">The full path to the project file.</param>
        /// <param name="globalProperties">The global properties which should be used during evaluation of the project.  Cannot be null.</param>
        /// <param name="toolsVersion">The tools version to use for the build.  May be null.</param>
        /// <param name="targetsToBuild">The targets to build.</param>
        /// <param name="hostServices">The host services to use.  May be null.</param>
        /// <param name="flags">The <see cref="BuildRequestDataFlags"/> to use.</param>
        public BuildRequestData(string projectFullPath, IDictionary<string, string?> globalProperties, string? toolsVersion, string[] targetsToBuild, HostServices? hostServices, BuildRequestDataFlags flags)
            : this(targetsToBuild, hostServices, flags, FileUtilities.NormalizePath(projectFullPath)!)
        {
            ErrorUtilities.VerifyThrowArgumentLength(projectFullPath, nameof(projectFullPath));
            ErrorUtilities.VerifyThrowArgumentNull(globalProperties, nameof(globalProperties));

            GlobalPropertiesDictionary = new PropertyDictionary<ProjectPropertyInstance>(globalProperties.Count);
            foreach (KeyValuePair<string, string?> propertyPair in globalProperties)
            {
                GlobalPropertiesDictionary.Set(ProjectPropertyInstance.Create(propertyPair.Key, propertyPair.Value));
            }

            ExplicitlySpecifiedToolsVersion = toolsVersion;
        }

        /// <summary>
        /// Common constructor.
        /// </summary>
        private BuildRequestData(string[] targetsToBuild, HostServices? hostServices, BuildRequestDataFlags flags, string projectFullPath)
            : base(targetsToBuild, flags, hostServices)
        {
            ProjectFullPath = projectFullPath;
        }

        /// <summary>
        /// The actual project, in the case where the project doesn't come from disk.
        /// May be null.
        /// </summary>
        /// <value>The project instance.</value>
        public ProjectInstance? ProjectInstance
        {
            get;
        }

        /// <summary>The project file.</summary>
        /// <value>The project file to be built.</value>
        public string ProjectFullPath { get; internal set; }

        internal override BuildSubmission<BuildRequestData, BuildResult> CreateSubmission(BuildManager buildManager,
            int submissionId, BuildRequestData requestData,
            bool legacyThreadingSemantics) =>
            new BuildSubmission(buildManager, submissionId, requestData, legacyThreadingSemantics);

        public override IEnumerable<string> EntryProjectsFullPath => ProjectFullPath.AsSingleItemEnumerable();

        /// <summary>
        /// The global properties to use.
        /// </summary>
        /// <value>The set of global properties to be used to build this request.</value>
        public ICollection<ProjectPropertyInstance> GlobalProperties => (GlobalPropertiesDictionary == null) ?
            (ICollection<ProjectPropertyInstance>)ReadOnlyEmptyCollection<ProjectPropertyInstance>.Instance :
            new ReadOnlyCollection<ProjectPropertyInstance>(GlobalPropertiesDictionary);

        public override bool IsGraphRequest => false;

        /// <summary>
        /// The explicitly requested tools version to use.
        /// </summary>
        public string? ExplicitlySpecifiedToolsVersion { get; }

        /// <summary>
        /// Returns a list of properties to transfer out of proc for the build.
        /// </summary>
        public IEnumerable<string>? PropertiesToTransfer { get; }

        /// <summary>
        /// Returns the properties, items, and metadata that will be returned
        /// by this build.
        /// </summary>
        public RequestedProjectState? RequestedProjectState { get; }

        /// <summary>
        /// Whether the tools version used originated from an explicit specification,
        /// for example from an MSBuild task or /tv switch.
        /// </summary>
        internal bool ExplicitToolsVersionSpecified => ExplicitlySpecifiedToolsVersion != null;

        /// <summary>
        /// Returns the global properties as a dictionary.
        /// </summary>
        internal PropertyDictionary<ProjectPropertyInstance>? GlobalPropertiesDictionary { get; }

        private IReadOnlyDictionary<string, string?>? _globalPropertiesLookup;

        /// <inheritdoc cref="BuildRequestDataBase"/>
        public override IReadOnlyDictionary<string, string?> GlobalPropertiesLookup => _globalPropertiesLookup ??=
            Execution.GlobalPropertiesLookup.ToGlobalPropertiesLookup(GlobalPropertiesDictionary);
    }
}
