// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Build.Analyzers;
using Microsoft.Build.BackEnd.Logging;
using Microsoft.Build.Framework;

namespace Microsoft.Build.Experimental
{
    internal class AnalyzersConnectorLogger : ILogger
    {
        private class AnalyzerLoggingContext : LoggingContext
        {
            public AnalyzerLoggingContext(ILoggingService loggingService, BuildEventContext eventContext) : base(
                loggingService, eventContext)
            {
                IsValid = true;
            }

            public AnalyzerLoggingContext(LoggingContext baseContext) : base(baseContext)
            {
                IsValid = true;
            }
        }

        private static readonly BuildAnalysisManager s_buildAnalysisManager = BuildAnalysisManager.CreateBuildAnalysisManager();

        public LoggerVerbosity Verbosity { get; set; }
        public string? Parameters { get; set; }

        // TODO: This is hack!
        public static ILoggingService? LoggingService { get; set; }

        public void Initialize(IEventSource eventSource)
        {
            // Debugger.Launch();

            eventSource.AnyEventRaised += EventSource_AnyEventRaised;
        }

        private void EventSource_AnyEventRaised(object sender, BuildEventArgs e)
        {
            // Debugger.Launch();

            if (e is ProjectEvaluationFinishedEventArgs projectEvaluationFinishedEventArgs)
            {
                // Debugger.Launch();

                // TODO: This is hack!
                s_buildAnalysisManager.LoggingContext =
                    new AnalyzerLoggingContext(LoggingService!, e.BuildEventContext!);

                s_buildAnalysisManager.ProcessEvaluationFinishedEventArgs(projectEvaluationFinishedEventArgs);
            }
        }

        public void Shutdown()
        { }
    }
}
