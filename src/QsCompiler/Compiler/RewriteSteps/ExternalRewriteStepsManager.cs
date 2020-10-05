﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Quantum.QsCompiler.ReservedKeywords;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace Microsoft.Quantum.QsCompiler
{
    internal class ExternalRewriteStepsManager
    {
        private ImmutableArray<IRewriteStepsLoader> rewriteStepsLoaders;

        public ExternalRewriteStepsManager(Action<Diagnostic>? onDiagnostic = null, Action<Exception>? onException = null)
        {
            this.rewriteStepsLoaders = ImmutableArray.Create<IRewriteStepsLoader>(
                new InstanceRewriteStepsLoader(onDiagnostic, onException),
                new TypeRewriteStepsLoader(onDiagnostic, onException),
                new AssemblyRewriteStepsLoader(onDiagnostic, onException));
        }

        /// <summary>
        /// Loads all dlls listed as containing rewrite steps to include in the compilation process in the given configuration.
        /// Generates suitable diagnostics if a listed file can't be found or loaded.
        /// Finds all types implementing the IRewriteStep interface and loads the corresponding rewrite steps
        /// according to the specified priority, where a steps with a higher priority will be listed first in the returned array.
        /// If the function onDiagnostic is specified and not null, calls it on all generated diagnostics,
        /// and calls onException on all caught exceptions if it is specified and not null.
        /// Returns an empty array if the rewrite steps in the given configurations are set to null.
        /// </summary>
        internal ImmutableArray<LoadedStep> Load(CompilationLoader.Configuration config)
        {
            var loadedSteps = new List<LoadedStep>();

            foreach (var loader in this.rewriteStepsLoaders)
            {
                loadedSteps.AddRange(loader.GetLoadedSteps(config));
            }

            foreach (var loaded in loadedSteps.Where(loadedStep => loadedStep != LoadedStep.Empty))
            {
                var assemblyConstants = loaded.AssemblyConstants;
                if (assemblyConstants == null)
                {
                    continue;
                }

                foreach (var kvPair in config.AssemblyConstants ?? Enumerable.Empty<KeyValuePair<string, string>>())
                {
                    assemblyConstants[kvPair.Key] = kvPair.Value;
                }

                // We don't overwrite assembly properties specified by configuration.
                var defaultOutput = assemblyConstants.TryGetValue(AssemblyConstants.OutputPath, out var path) ? path : null;
                assemblyConstants.TryAdd(AssemblyConstants.OutputPath, loaded.OutputFolder ?? defaultOutput ?? config.BuildOutputFolder);
                assemblyConstants.TryAdd(AssemblyConstants.AssemblyName, config.ProjectNameWithoutExtension);
            }

            CompilationLoader.SortRewriteSteps(loadedSteps, step => step.Priority);
            return loadedSteps.ToImmutableArray();
        }
    }
}
