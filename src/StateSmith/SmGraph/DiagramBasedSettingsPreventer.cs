#nullable enable

using StateSmith.Output.UserConfig;
using StateSmith.Output.UserConfig.AutoVars;
using StateSmith.Runner;
using System;
using System.Collections.Generic;

namespace StateSmith.SmGraph;

/// <summary>
/// Sometimes we need to prevent the diagram from setting the settings.
/// Useb by the simulator and also to prevent the settings from being applied twice.
/// </summary>
public class DiagramBasedSettingsPreventer
{
    /// <summary>
    /// Simple IDiagramVerticesProvider that returns a single SM as the only root vertex.
    /// Used when processing toml configs outside of the normal DI pipeline.
    /// </summary>
    private class SingleSmDiagramVerticesProvider : IDiagramVerticesProvider
    {
        private readonly StateMachine _sm;
        public SingleSmDiagramVerticesProvider(StateMachine sm) => _sm = sm;
        public List<Vertex> GetRootVertices() => new() { _sm };
    }

    public static void Process(SmTransformer transformer, Action<RenderConfigAllVars, RunnerSettings>? action = null)
    {
        transformer.InsertBeforeFirstMatch(StandardSmTransformer.TransformationId.Standard_TomlConfig, (sm) =>
        {
            // create temp settings/config objects that may get modified by special diagram nodes
            RenderConfigAllVars tempRenderConfigAllVars = new();
            RunnerSettings tempSmRunnerSettings = new();
            // Create a simple provider that returns just this SM as the only root vertex.
            // DiagramBasedSettingsPreventer only processes one SM, so no cross-SM cleanup is needed.
            var simpleProvider = new SingleSmDiagramVerticesProvider(sm);
            var tomlConfigVerticesProcessor = new TomlConfigVerticesProcessor(tempRenderConfigAllVars, tempSmRunnerSettings, simpleProvider);
            tomlConfigVerticesProcessor.Process(sm);
            var renderConfigVerticesProcessor = new RenderConfigVerticesProcessor(tempRenderConfigAllVars, sm);
            renderConfigVerticesProcessor.Process();

            action?.Invoke(tempRenderConfigAllVars, tempSmRunnerSettings);
        });

        // these transformations are no longer needed for the simulation
        transformer.Remove(StandardSmTransformer.TransformationId.Standard_TomlConfig);
        transformer.Remove(StandardSmTransformer.TransformationId.Standard_SupportRenderConfigVerticesAndRemove);
    }
}
