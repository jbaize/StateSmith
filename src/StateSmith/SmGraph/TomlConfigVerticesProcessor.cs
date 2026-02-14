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
        // we gather into a list first because we are modifying the graph
        List<ConfigOptionVertex> toProcess = new();

        foreach (var root in diagramVerticesProvider.GetRootVertices())
        {
            root.VisitTypeRecursively<ConfigOptionVertex>(v =>
            {
                if (!v.name.Equals("toml", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var containingSm = FindContainingStateMachine(v);

                // Include global toml config (outside a state machine) and toml config inside the selected state machine.
                if (containingSm == null || containingSm == sm)
                {
                    toProcess.Add(v);
                }
            });
        }

        foreach (var configOptionVertex in toProcess)
        {
            // these vertices are not allowed to have children
            if (configOptionVertex.Children.Any())
            {
                throw new VertexValidationException(configOptionVertex, "toml config vertices cannot have children");
            }

            tomlReader.Read(configOptionVertex.value);
            configOptionVertex.RemoveChildrenAndSelf();
        }
    }

    private static StateMachine? FindContainingStateMachine(Vertex vertex)
    {
        var current = vertex.Parent;

        while (current != null)
        {
            if (current is StateMachine sm)
            {
                return sm;
            }

            current = current.Parent;
        }

        return null;
    }
}
