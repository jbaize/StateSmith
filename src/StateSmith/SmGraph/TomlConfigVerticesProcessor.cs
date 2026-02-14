#nullable enable

using StateSmith.Input.Settings;
using StateSmith.Output.UserConfig.AutoVars;
using StateSmith.Runner;
using StateSmith.SmGraph.Validation;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StateSmith.SmGraph;

/// <summary>
/// https://github.com/StateSmith/StateSmith/issues/335
/// </summary>
public class TomlConfigVerticesProcessor
{
    TomlReader tomlReader;
    readonly IDiagramVerticesProvider diagramVerticesProvider;

    public TomlConfigVerticesProcessor(RenderConfigAllVars renderConfigAllVars, RunnerSettings smRunnerSettings, IDiagramVerticesProvider diagramVerticesProvider)
    {
        tomlReader = new TomlReader(renderConfigAllVars, smRunnerSettings);
        this.diagramVerticesProvider = diagramVerticesProvider;
    }

    public void Process(StateMachine sm)
    {
        // Process toml configs from the selected SM (these are read and applied)
        ProcessTomlConfigsInVertex(sm, readValues: true);

        // Also remove toml configs from all other root-level SMs so that
        // RenderConfigVerticesProcessor doesn't choke on them when it visits all root vertices.
        // This is needed for multi-SM diagrams where each SM has its own $CONFIG : toml.
        foreach (var rootVertex in diagramVerticesProvider.GetRootVertices())
        {
            if (rootVertex is StateMachine otherSm && otherSm != sm)
            {
                ProcessTomlConfigsInVertex(otherSm, readValues: false);
            }
        }
    }

    private void ProcessTomlConfigsInVertex(Vertex root, bool readValues)
    {
        // we gather into a list first because we are modifying the graph
        List<ConfigOptionVertex> toProcess = new();

        root.VisitTypeRecursively<ConfigOptionVertex>(v =>
        {
            if (v.name.Equals("toml", StringComparison.OrdinalIgnoreCase))
            {
                toProcess.Add(v);
            }
        });

        foreach (var configOptionVertex in toProcess)
        {
            // these vertices are not allowed to have children
            if (configOptionVertex.Children.Any())
            {
                throw new VertexValidationException(configOptionVertex, "toml config vertices cannot have children");
            }

            if (readValues)
            {
                tomlReader.Read(configOptionVertex.value);
            }

            configOptionVertex.RemoveChildrenAndSelf();
        }
    }
}
