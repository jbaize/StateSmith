#nullable enable

using StateSmith.SmGraph;
using System.Collections.Generic;
using System.Linq;

namespace StateSmith.Output;

/// <summary>
/// Holds the union of all events from multiple state machines in a single diagram file.
/// When active, the EnumBuilder uses this set instead of individual SM events,
/// ensuring all SMs in the file share the same EventId enum.
/// </summary>
public class SharedEventSet
{
    private readonly HashSet<string> _allEvents = new();

    /// <summary>
    /// True when events have been collected, indicating shared event mode is active.
    /// </summary>
    public bool IsActive => _allEvents.Count > 0;

    /// <summary>
    /// Adds all events from a state machine to the shared set.
    /// </summary>
    public void AddEventsFrom(StateMachine sm)
    {
        foreach (var evt in sm._events)
        {
            _allEvents.Add(evt);
        }
    }

    /// <summary>
    /// Returns a sorted list of all collected events.
    /// </summary>
    public List<string> GetSortedEventList()
    {
        var list = _allEvents.ToList();
        list.Sort();
        return list;
    }
}
