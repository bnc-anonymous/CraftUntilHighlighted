using System;
using System.Collections.Generic;
using System.Numerics;
using ExileCore2;
using ExileCore2.PoEMemory;
using ExileCore2.PoEMemory.Elements.InventoryElements;
using ExileCore2.PoEMemory.MemoryObjects;
using ExileCore2.Shared.Enums;
using ModsComponent = ExileCore2.PoEMemory.Components.Mods;
using RectangleF = ExileCore2.Shared.RectangleF;

namespace CraftUntilHighlighted;

/// <summary>
/// The item in the currency stash tab's crafting slot, and the craft square drawn at its bottom-left.
///
/// The item is read at a fixed path of child indices below StashElement, CraftingItemPath, rather than found by
/// searching the tab. The path was read off dumps of 2026-10-26 and DevTree agrees with it. It is checked on every read:
/// the visible tab must be a currency tab, and the element at the path must hold an item with Mods outside
/// Metadata/Items/Currency/ (the rule Artisan, a PoE1 plugin, used to find the crafting slot by search). A patch that
/// reorders the stash's elements fails that check and reports itself rather than crafting on the wrong element.
///
/// The item element is read afresh on every call and never kept. Artisan measured in PoE1 that applying an orb replaces
/// both the item's entity and the element around it, so a remembered element would report the item as it was before the
/// orb. CraftingSlotCache keeps only values copied out of it, for drawing.
/// </summary>
internal static class CraftingSlot
{
    private const string CurrencyPath = "Metadata/Items/Currency/";

    /// <summary>
    /// Child indices from StashElement to the crafting slot's item: [2,0,0,0,1,1,0,0] is the visible tab (VisibleStash),
    /// its child 4 the crafting slot, and that slot's child 0 the item. Read off the element tree in the dumps of
    /// 2026-10-26 00:52 to 01:02 (a currency tab, 1440p client) and seen in DevTree.
    /// </summary>
    private static readonly int[] CraftingItemPath = [2, 0, 0, 0, 1, 1, 0, 0, 4, 0];

    /// <summary>Child indices from StashElement to the search bar: the "Highlight Items" label and the box beside it.</summary>
    private static readonly int[] SearchBarPath = [3];

    /// <summary>Child indices from StashElement to the search box's text field, read off the same dumps.</summary>
    private static readonly int[] SearchFieldPath = [3, 3, 0];

    /// <summary>
    /// The element at a fixed child-index path below root, or null when any step is missing. Does what
    /// Element.GetChildFromIndices does without its log line on a miss: an empty crafting slot is a miss on every read
    /// and filled the HUD log with "Element with index: 0 not found". Each step checks Element.ChildCount and reads the
    /// one child through Element.GetChildAtIndex, which (read in ExileCore2's decompiled Element) bounds-checks and reads a
    /// single pointer, rather than listing every child.
    /// </summary>
    private static Element ChildAtPath(Element root, int[] path)
    {
        var element = root;

        foreach (var index in path)
        {
            if (element == null || index >= element.ChildCount)
                return null;

            element = element.GetChildAtIndex(index);
        }

        return element;
    }

    /// <summary>The stash search bar's element address, or 0 when it cannot be read. See CraftLoop.TryStart.</summary>
    public static long SearchBarAddressOf(GameController gc) =>
        Safe.Read(gc, static g => ChildAtPath(g.IngameState.IngameUi.StashElement, SearchBarPath)?.Address ?? 0L, 0L);

    /// <summary>The visible stash tab, or null when the stash is closed or unreadable.</summary>
    public static Inventory VisibleStashOf(GameController gc) =>
        Safe.Read(gc, static g =>
        {
            var stash = g.IngameState.IngameUi.StashElement;

            return stash is { IsVisible: true } ? stash.VisibleStash : null;
        }, null);


    /// <summary>
    /// Every item element the visible tab lists: its own, then each sub-inventory's, labelled with where it came from.
    /// Empty when nothing can be read.
    /// For the dump only; the crafting item is read at its path. See Find.
    /// </summary>
    public static List<(string Source, NormalInventoryItem Item)> ItemsOfVisibleStash(GameController gc)
    {
        var found = new List<(string, NormalInventoryItem)>();
        var stash = VisibleStashOf(gc);

        if (stash == null)
            return found;

        foreach (var item in Safe.Read(stash, static s => s.VisibleInventoryItems, null) ?? [])
        {
            if (item != null)
                found.Add(("tab", item));
        }

        var subInventories = Safe.Read(stash, static s => s.SubInventories, null) ?? [];

        for (var i = 0; i < subInventories.Count; i++)
        {
            foreach (var item in Safe.Read(subInventories[i], static s => s?.VisibleInventoryItems, null) ?? [])
            {
                if (item != null)
                    found.Add(($"sub-inventory {i}", item));
            }
        }

        return found;
    }

    /// <summary>Whether an item element holds something an orb can be applied to, by the rule in the class summary.</summary>
    public static bool IsCraftable(NormalInventoryItem item) =>
        Safe.Read(item, static i =>
        {
            var entity = i.Item;

            if (entity == null || !entity.IsValid)
                return false;

            var path = entity.Path;

            return !string.IsNullOrEmpty(path) &&
                   !path.StartsWith(CurrencyPath, StringComparison.Ordinal) &&
                   entity.HasComponent<ModsComponent>();
        }, false);

    /// <summary>
    /// The item in the crafting slot, or null. Stops reading at the first thing missing: a closed stash costs the
    /// visibility read, another tab the tab type.
    /// </summary>
    public static NormalInventoryItem Find(GameController gc)
    {
        var stash = VisibleStashOf(gc);

        if (stash == null || Safe.Read(stash, static s => s.InvType, InventoryType.InvalidInventory) != InventoryType.CurrencyStash)
            return null;

        var element = Safe.Read(gc, static g => ChildAtPath(g.IngameState.IngameUi.StashElement, CraftingItemPath), null);
        var item = Safe.Read(element, static e => e?.AsObject<NormalInventoryItem>(), null);

        return IsCraftable(item) ? item : null;
    }

    /// <summary>
    /// The item in the crafting slot, or null with the reason. Builds the reason as text, so it is for the dump and debug
    /// mode, not for every frame.
    /// </summary>
    public static NormalInventoryItem Find(GameController gc, out string why)
    {
        var stash = VisibleStashOf(gc);

        if (stash == null)
        {
            why = "the stash is not open";
            return null;
        }

        if (Safe.Read(stash, static s => s.InvType, InventoryType.InvalidInventory) != InventoryType.CurrencyStash)
        {
            why = $"the open tab is {Safe.Read(stash, static s => s.InvType.ToString(), "unreadable")}, not a currency tab";
            return null;
        }

        var element = Safe.Read(gc, static g => ChildAtPath(g.IngameState.IngameUi.StashElement, CraftingItemPath), null);

        if (element == null)
        {
            why = "nothing at the crafting slot's path - the slot is empty, or the stash's layout has changed";
            return null;
        }

        var item = Safe.Read(element, static e => e.AsObject<NormalInventoryItem>(), null);

        if (!IsCraftable(item))
        {
            why = $"the element at the crafting slot's path holds {Safe.Read(element, static e => e.Entity?.Path, null) ?? "no item"}, " +
                  "not a craftable item - the stash's layout may have changed";
            return null;
        }

        why = "";
        return item;
    }

    /// <summary>
    /// The craft square for an item's rectangle: a side of percentOfWidth of the item's width, inset from the item's
    /// bottom-left corner so a click inside it lands on the item. Shrunk to fit inside the item.
    /// </summary>
    public static RectangleF CraftSquareOf(RectangleF item, int percentOfWidth)
    {
        const float inset = 4f;

        var side = Math.Min(item.Width * percentOfWidth / 100f, Math.Min(item.Width, item.Height) - 2f * inset);

        if (side <= 0f)
            return default;

        return new RectangleF(item.Left + inset, item.Bottom - inset - side, side, side);
    }

    /// <summary>
    /// What the stash search is doing to one item element. Highlighted is Element.isHighlighted, which is what decides.
    ///
    /// Read off dumps of 2026-10-26 00:55, one ring and three searches, and seen in DevTree: isHighlighted was true while
    /// the search matched the ring and false with no search or a search that did not match. Its raw byte read 120 and 0.
    /// The border is kept for the readout only: a match also set the ring's BordColor to #FFE7B478. Whether every item
    /// class behaves the same is not yet known.
    /// </summary>
    public readonly record struct SearchHighlight(bool Highlighted, System.Drawing.Color Border)
    {
        public override string ToString() =>
            $"isHighlighted {Highlighted}, border #{Border.A:X2}{Border.R:X2}{Border.G:X2}{Border.B:X2}";
    }

    public static SearchHighlight SearchHighlightOf(ExileCore2.PoEMemory.Element item) =>
        new(Safe.Read(item, static e => e.isHighlighted, false),
            Safe.Read(item, static e => e.BordColor, default(System.Drawing.Color)));

    /// <summary>
    /// The text in the stash search box, or null when it cannot be read. Read at SearchFieldPath; in the dumps of
    /// 2026-10-26 00:55 its text followed the search typed.
    /// </summary>
    public static string SearchTextOf(GameController gc)
    {
        var field = Safe.Read(gc, static g => ChildAtPath(g.IngameState.IngameUi.StashElement, SearchFieldPath), null);

        return field == null ? null : Safe.Read(field, static f => f.Text ?? "", null);
    }

    public static bool Contains(RectangleF rect, Vector2 point) =>
        rect.Width > 0f && point.X >= rect.Left && point.X <= rect.Right && point.Y >= rect.Top && point.Y <= rect.Bottom;

    /// <summary>The game's own record of where its cursor is, in client space.</summary>
    public static Vector2 MouseOf(GameController gc) =>
        Safe.Read(gc, static g => new Vector2(g.IngameState.MousePosX, g.IngameState.MousePosY), new Vector2(-1f, -1f));
}
