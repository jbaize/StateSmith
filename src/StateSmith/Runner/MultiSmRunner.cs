#nullable enable

using StateSmith.Output;
using StateSmith.SmGraph;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StateSmith.Runner;

/// <summary>
/// Orchestrates processing of multiple state machines from a single diagram file.
/// Each SM gets its own code generation output, but they all share a unified EventId enum
/// containing the union of all events from all SMs in the file.
/// </summary>
public class MultiSmRunner
{
    /// <summary>
    /// Discovers all state machine names in a diagram file without running transformation or code gen.
    /// </summary>
    public static List<string> DiscoverStateMachineNames(string diagramPath)
    {
        var builder = new InputSmBuilder();
        builder.ConvertDiagramFileToSmVertices(diagramPath);
        return builder.GetAllStateMachineNames();
    }

    /// <summary>
    /// Counts the number of state machines in a diagram file.
    /// </summary>
    public static int CountStateMachines(string diagramPath)
    {
        return DiscoverStateMachineNames(diagramPath).Count;
    }

    /// <summary>
    /// Runs code generation for all state machines in a diagram file with a shared event enum.
    /// Phase A: Discovers SMs, reads pre-diagram settings (transpilerId from toml), collects all events.
    /// Phase B: Runs full SmRunner for each SM with the SharedEventSet injected.
    /// </summary>
    public static void Run(string diagramPath, TranspilerId transpilerId, string callerFilePath,
        bool propagateExceptions = false, bool dumpErrorsToFile = false, bool enableSimGen = true,
        Action<SmRunner>? configureRunner = null)
    {
        // Phase A: Discover SM names
        var smNames = DiscoverStateMachineNames(diagramPath);

        if (smNames.Count == 0)
            throw new InvalidOperationException($"No state machines found in diagram file: {diagramPath}");

        if (smNames.Count != smNames.Distinct().Count())
            throw new InvalidOperationException($"Duplicate state machine names found in diagram file: {diagramPath}. Each state machine must have a unique name.");

        // Resolve transpilerId from diagram's $CONFIG : toml if not set via CLI.
        // We target the first SM so PreDiagramSettingsReader can use FindStateMachineByName()
        // instead of FindSingleStateMachine() (which throws with multiple SMs).
        var resolvedTranspilerId = ResolveTranspilerId(diagramPath, callerFilePath, smNames[0], transpilerId);

        if (resolvedTranspilerId == TranspilerId.NotYetSet)
            throw new ArgumentException($"No language specified via --lang and no transpilerId found in diagram's $CONFIG : toml. Diagram: {diagramPath}");

        var sharedEvents = CollectAllEvents(diagramPath, smNames);

        // Phase B: Generate code for each SM with shared events
        foreach (var smName in smNames)
        {
            var settings = new RunnerSettings(diagramFile: diagramPath, transpilerId: resolvedTranspilerId);
            settings.stateMachineName = smName;
            settings.simulation.enableGeneration = enableSimGen;
            settings.propagateExceptions = propagateExceptions;
            settings.dumpErrorsToFile = dumpErrorsToFile;

            var runner = new SmRunner(settings, renderConfig: null, callerFilePath: callerFilePath);

            // Inject shared event set before DI is finalized
            runner.GetExperimentalAccess().DiServiceProvider.AddSingletonT<SharedEventSet>(sharedEvents);

            configureRunner?.Invoke(runner);

            runner.Run();
        }
    }

    /// <summary>
    /// Reads pre-diagram settings (transpilerId, etc.) from the diagram by creating a
    /// temporary SmRunner targeting a specific SM. The SmRunner constructor runs
    /// PreDiagramSettingsReader which parses $CONFIG : toml and updates RunnerSettings.
    /// </summary>
    private static TranspilerId ResolveTranspilerId(string diagramPath, string callerFilePath,
        string firstSmName, TranspilerId initialTranspilerId)
    {
        var settings = new RunnerSettings(diagramFile: diagramPath, transpilerId: initialTranspilerId);
        settings.stateMachineName = firstSmName;

        // SmRunner constructor runs PreDiagramSettingsReader which reads $CONFIG : toml
        var tempRunner = new SmRunner(settings, renderConfig: null, callerFilePath: callerFilePath);

        // Check if pre-diagram settings reading failed
        tempRunner.PrintAndThrowIfPreDiagramSettingsException();

        return settings.transpilerId;
    }

    /// <summary>
    /// Transforms each SM independently and collects all events into a shared set.
    /// Uses the internal InputSmBuilder constructor (test-style) for lightweight transformation
    /// without full SmRunner overhead.
    /// </summary>
    private static SharedEventSet CollectAllEvents(string diagramPath, List<string> smNames)
    {
        var sharedEvents = new SharedEventSet();

        foreach (var smName in smNames)
        {
            var builder = new InputSmBuilder();
            builder.ConvertDiagramFileToSmVertices(diagramPath);
            builder.FindStateMachineByName(smName);
            builder.FinishRunning();
            var sm = builder.GetStateMachine();
            sharedEvents.AddEventsFrom(sm);
        }

        return sharedEvents;
    }
}
