using System;
using System.Collections.Generic;
using ExileCore2.PoEMemory;

namespace CraftUntilHighlighted;

/// <summary>
/// One read of remote memory, or the fallback.
///
/// Every memory object in this HUD can throw at arbitrary times - zoning, a tab change, a hot reload - and a throw
/// inside Render takes the whole overlay down, not just this plugin. So nothing reads the game without going through
/// here. Taken from AutoExpedition's Safe, less its read counters.
/// </summary>
internal static class Safe
{
    public static T Read<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>
    /// The same read, with the thing being read passed in rather than captured. Written as
    /// <c>Safe.Read(item, static i =&gt; i.Item, null)</c> the lambda captures nothing and the call allocates nothing,
    /// which matters for reads made every frame.
    /// </summary>
    public static TValue Read<TState, TValue>(TState state, Func<TState, TValue> read, TValue fallback)
    {
        try
        {
            return read(state);
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>For a read whose only purpose is its side effect, such as appending to a dump.</summary>
    public static void Try(Action act)
    {
        try
        {
            act();
        }
        catch
        {
            // A field that cannot be read is a field the dump does not mention.
        }
    }

    /// <summary>
    /// An element's children through Element.Children, or null when there are none or they cannot be read.
    ///
    /// AutoExpedition reads the raw child pointer array instead, because it found Element.Children returning fewer
    /// children than the array holds on the passive tree's container. That is not done here: this plugin reads only what
    /// ExileCore2 exposes, so that a patch moving an offset is fixed by ExileCore2's update rather than by this plugin.
    /// If a dump ever shows a stash element with ChildCount above the number of children listed, this is where to look.
    /// </summary>
    public static IList<Element> ChildrenOf(Element element)
    {
        var children = Read(element, static e => e?.Children, null);

        return children is { Count: > 0 } ? children : null;
    }
}
