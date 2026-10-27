using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ExileCore2;
using ExileCore2.PoEMemory;
using ExileCore2.PoEMemory.Components;
using ExileCore2.PoEMemory.Elements.InventoryElements;
using ExileCore2.PoEMemory.MemoryObjects;
using ExileCore2.Shared.Enums;
using RectangleF = ExileCore2.Shared.RectangleF;

namespace CraftUntilHighlighted;

/// <summary>
/// Writes what the plugin can read about the open stash tab, the crafting item, the cursor and the stash's interface
/// elements to a text file.
///
/// It was written to answer three questions before any crafting was automated, each of which had to be read off the
/// client rather than assumed, and is kept as the way to report what the plugin sees:
///
/// 1. Where the crafting slot's item is: whether PoE2's currency tab lists it among the tab's items, in a
///    sub-inventory, or nowhere. The stash and items sections answer it.
/// 2. What the in-game search changes when it matches the item: Element.isHighlighted, measured. The element tree
///    prints ExileCore2's element properties, and the changes section lists what differs from the previous dump.
/// 3. What applying an orb changes: the Mods hash, the item's entity and element addresses, ItemVersion. A dump before
///    and after one orb showed ItemVersion is the one to watch.
///
/// Since then it also shows the cursor: which currency is selected and how it was named (SelectedCurrency), and the
/// cursor element's raw memory for repairing Offsets (RawCursorProbe).
///
/// Every read goes through Safe, and the dump catches its own throw, so a section that cannot be read says so and the
/// rest is still written.
/// </summary>
internal static class Dump
{
    /// <summary>
    /// Set by the settings button, answered by the tick that answers the dump key. A CustomNode is handed neither the
    /// plugin nor the GameController, so the button and the key share one path.
    /// </summary>
    public static bool AskedForInSettings;

    /// <summary>The last file written, for the row beside the button. Empty until one is taken.</summary>
    public static string LastWritten = "";

    /// <summary>Most lines the element tree may write, so a runaway walk cannot produce an unreadable file.</summary>
    private const int MostTreeLines = 8000;

    /// <summary>Most lines the changes section may write.</summary>
    private const int MostChangeLines = 400;

    /// <summary>
    /// Each element's description from the previous dump's tree, by its path of child indices from the stash element.
    /// Held in memory only, so the first dump after a reload has nothing to compare against.
    /// </summary>
    private static Dictionary<string, string> _previousTree;

    /// <summary>When the previous tree was read, for the changes section's heading.</summary>
    private static DateTime _previousTreeAt;

    public static void Write(BaseSettingsPlugin<CraftUntilHighlightedSettings> plugin, ExileInput2Client input)
    {
        var gc = plugin.GameController;
        var settings = plugin.Settings;
        var b = new StringBuilder();

        try
        {
            Describe(b, gc, settings, input);
        }
        catch (Exception ex)
        {
            b.AppendLine();
            b.AppendLine($"!! the dump itself threw: {ex}");
        }

        try
        {
            var directory = DumpFolder.Path;
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, $"craftuntilhighlighted_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(path, b.ToString());

            LastWritten = Path.GetFileName(path);
            DebugWindow.LogMsg($"[CraftUntilHighlighted] Wrote {path}", 10f);

            Prune(directory, settings.Debug.MaxDumps.Value);
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[CraftUntilHighlighted] Could not write the dump: {ex.Message}", 10f);
        }
    }

    /// <summary>Keeps the newest few timestamped dumps and deletes the rest. Failures are ignored: the dump is written.</summary>
    private static void Prune(string directory, int keep)
    {
        try
        {
            var files = new DirectoryInfo(directory).GetFiles("craftuntilhighlighted_*.txt")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            for (var i = Math.Max(1, keep); i < files.Count; i++)
                files[i].Delete();
        }
        catch
        {
            // Nothing depends on it.
        }
    }

    private static void Describe(StringBuilder b, GameController gc, CraftUntilHighlightedSettings settings,
        ExileInput2Client input)
    {
        b.AppendLine($"CraftUntilHighlighted dump - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        b.AppendLine($"plugin enabled: {settings.Enable.Value}");
        b.AppendLine($"area: {Safe.Read(gc, static g => g.Area.CurrentArea.DisplayName, "?")}");

        var item = CraftingSlot.Find(gc, out var why);

        DescribeInput(b, input);
        DescribeCursor(b, gc, item, settings);
        DescribeHover(b, gc);
        DescribeStash(b, gc);
        DescribeItems(b, gc);
        DescribeCraftingItem(b, gc, item, why);
        DescribeTree(b, gc, item, settings.Debug.DumpElementDepth.Value);
    }

    private static void DescribeInput(StringBuilder b, ExileInput2Client input)
    {
        b.AppendLine();
        b.AppendLine("== ExileInput2");

        if (input == null)
        {
            b.AppendLine("  no client - Initialise has not run");
            return;
        }

        b.AppendLine($"  available: {input.Available}, bridge version: {input.Version()} " +
                     $"(this plugin needs {CraftUntilHighlighted.ExileInput2VersionNeeded})");

        var stopped = input.Stopped();

        b.AppendLine($"  stopped: {(stopped.Length == 0 ? "no" : stopped)}");

        if (input.StopDetail() is { Length: > 0 } detail)
            b.AppendLine($"  last stop: {detail}");
    }

    private static void DescribeCursor(StringBuilder b, GameController gc, NormalInventoryItem item,
        CraftUntilHighlightedSettings settings)
    {
        b.AppendLine();
        b.AppendLine("== Cursor");

        var mouse = CraftingSlot.MouseOf(gc);

        b.AppendLine($"  game mouse position: ({mouse.X:0},{mouse.Y:0})");

        var cursor = Safe.Read(gc, static g => g.IngameState.IngameUi.Cursor, null);

        b.AppendLine(cursor == null
            ? "  IngameUi.Cursor: unreadable"
            : $"  IngameUi.Cursor: action {Safe.Read(cursor, static c => c.Action.ToString(), "?")}, " +
              $"action string \"{Safe.Read(cursor, static c => c.ActionString, "?")}\", " +
              $"clicks {Safe.Read(cursor, static c => c.Clicks, -1)}, text \"{Safe.Read(cursor, static c => c.Text, "")}\", " +
              $"action sound \"{Safe.Read(cursor, static c => c.ActionSound, "?")}\"");

        var selected = SelectedCurrency.ItemTypeIfSelected(gc);
        var hash = SelectedCurrency.ItemTypeHashOnCursor(gc);

        b.AppendLine($"  currency selected: {Safe.Read(selected, static t => t?.BaseName, null) ?? "none"} " +
                     $"({Safe.Read(selected, static t => t?.Metadata, null) ?? "-"}), on Allowed currencies: " +
                     $"{SelectedCurrency.IsAllowed(selected, settings)}; item type hash at " +
                     $"+0x{OffsetRepair.CursorSelectedItemTypeHashOffset:X} 0x{hash:X8} names " +
                     $"{Safe.Read(SelectedCurrency.ItemTypeOfHash(gc, hash), static t => t?.BaseName, null) ?? "nothing"}" +
                     (OffsetRepair.LastSearch.Length > 0 ? $"; offset repair: {OffsetRepair.LastSearch}" : ""));

        // The cursor element and what is under it, with each element's texture and entity. None of it names a right-clicked
        // currency (dumps 2026-10-27 10:49: no texture, no entity, the same for all three Chaos Orbs), which is why
        // SelectedCurrency reads the item type hash raw; kept because a picked-up item reads as a child here with its entity
        // (a tablet, AutoExpedition, 2026-10-13).
        b.AppendLine($"  IngameUi.Cursor element: {DescribeElement(cursor)}");
        DescribeChildren(b, cursor, "    ", 0, 4);

        // The server's cursor inventory holds an item picked up. It stays empty while a currency is right-clicked for use
        // (dumps 2026-10-26 and 2026-10-27).
        var held = Safe.Read(gc, static g => g.IngameState.ServerData.GetPlayerInventoryBySlot(InventorySlotE.Cursor1).Items,
            null);

        if (held == null)
            b.AppendLine("  server cursor inventory (Cursor1): unreadable");
        else if (held.Count == 0)
            b.AppendLine("  server cursor inventory (Cursor1): empty");
        else
        {
            foreach (var entity in held)
            {
                b.AppendLine($"  server cursor inventory (Cursor1): {Safe.Read(entity, static e => e.GetComponent<Base>()?.Name, "?")} " +
                             $"- {Safe.Read(entity, static e => e.Path, "?")}");
            }
        }

        var itemRect = Safe.Read(item, static i => i.GetClientRectCache, default(RectangleF));
        var square = CraftingSlot.CraftSquareOf(itemRect, settings.Display.CraftSquareSizePercent.Value);

        b.AppendLine(item == null
            ? "  craft square: none, no crafting item found"
            : $"  craft square: {Rect(square)} inside crafting item {Rect(itemRect)}; " +
              $"mouse inside the square: {CraftingSlot.Contains(square, mouse)}");

        RawCursorProbe.Describe(b, gc, cursor);
    }

    private static void DescribeHover(StringBuilder b, GameController gc)
    {
        b.AppendLine();
        b.AppendLine("== Hover");

        b.AppendLine($"  UIHover:        {DescribeElement(Safe.Read(gc, static g => g.IngameState.UIHover, null))}");
        b.AppendLine($"  UIHoverElement: {DescribeElement(Safe.Read(gc, static g => g.IngameState.UIHoverElement, null))}");

        var focused = Safe.Read(gc, static g => g.IngameState.FocusedInputElement, null);

        b.AppendLine($"  FocusedInputElement: {DescribeElement(focused)}");

        // Marked obsolete in ExileCore2 with no replacement named; AutoExpedition still reads it and it still answers.
#pragma warning disable CS0612
        var tooltip = Safe.Read(gc, static g => g.IngameState.UIHoverTooltip, null);
#pragma warning restore CS0612

        b.AppendLine($"  UIHoverTooltip: {DescribeElement(tooltip)}");

        var texts = new List<string>();
        TextsOf(tooltip, 0, texts);

        foreach (var text in texts)
            b.AppendLine($"    \"{text}\"");
    }

    private static void TextsOf(Element element, int depth, List<string> into)
    {
        if (element == null || depth > 10 || into.Count > 200)
            return;

        if (Safe.Read(element, static e => e.Text, null) is { Length: > 0 } text && !string.IsNullOrWhiteSpace(text))
            into.Add(OneLine(text));

        foreach (var child in Safe.ChildrenOf(element) ?? [])
            TextsOf(child, depth + 1, into);
    }

    private static void DescribeStash(StringBuilder b, GameController gc)
    {
        b.AppendLine();
        b.AppendLine("== Stash");

        var stashElement = Safe.Read(gc, static g => g.IngameState.IngameUi.StashElement, null);

        if (stashElement == null)
        {
            b.AppendLine("  StashElement: unreadable");
            return;
        }

        var visible = Safe.Read(stashElement, static s => s.IsVisible, false);
        var index = Safe.Read(stashElement, static s => s.IndexVisibleStash, -1);

        // GetStashName is obsolete in favour of Inventories; the name is only for recognising the tab in a dump.
#pragma warning disable CS0618
        b.AppendLine($"  StashElement: visible {visible}, visible tab index {index}, " +
                     $"name \"{Safe.Read((Stash: stashElement, Index: index), static x => x.Stash.GetStashName(x.Index), "?")}\", " +
                     $"total tabs {Safe.Read(stashElement, static s => s.TotalStashes, -1L)}");
#pragma warning restore CS0618

        if (!visible)
            return;

        var stash = CraftingSlot.VisibleStashOf(gc);

        if (stash == null)
        {
            b.AppendLine("  VisibleStash: unreadable");
            return;
        }

        DescribeInventory(b, stash, "  ");

        var subInventories = Safe.Read(stash, static s => s.SubInventories, null);

        b.AppendLine($"  sub-inventories: {(subInventories == null ? "unreadable" : subInventories.Count.ToString())}");

        for (var i = 0; i < (subInventories?.Count ?? 0); i++)
        {
            b.AppendLine($"  sub-inventory {i}:");
            DescribeInventory(b, subInventories[i], "    ");
        }
    }

    private static void DescribeInventory(StringBuilder b, Inventory inventory, string indent)
    {
        if (inventory == null)
        {
            b.AppendLine($"{indent}null");
            return;
        }

        b.AppendLine($"{indent}InvType {Safe.Read(inventory, static i => i.InvType.ToString(), "?")}, " +
                     $"nested {Safe.Read(inventory, static i => i.IsNestedInventory.ToString(), "?")}, " +
                     $"nested visible index {Safe.Read(inventory, static i => i.NestedVisibleInventoryIndex?.ToString() ?? "none", "?")}, " +
                     $"ItemCount {Safe.Read(inventory, static i => i.ItemCount.ToString(), "?")}, " +
                     $"size {Safe.Read(inventory, static i => i.InventorySize.ToString(), "?")}, " +
                     $"rect {Rect(Safe.Read(inventory, static i => i.GetClientRectCache, default(RectangleF)))}");
        b.AppendLine($"{indent}cursor over inventory {Safe.Read(inventory, static i => i.CursorHoverInventory.ToString(), "?")}, " +
                     $"hover state {Safe.Read(inventory, static i => i.ItemHoverState.ToString(), "?")}, " +
                     $"hover item {Safe.Read(inventory, static i => i.HoverItem?.Item?.Path ?? "none", "?")}");
        b.AppendLine($"{indent}server inventory: {Safe.Read(inventory, static i => $"{i.ServerInventory.InventType} {i.ServerInventory.InventSlot}, {i.ServerInventory.Columns}x{i.ServerInventory.Rows}, {i.ServerInventory.ItemCount} items", "unreadable")}");

        var highlights = Safe.Read(inventory, static i => i.Highlights, null);

        b.AppendLine($"{indent}Highlights: {(highlights == null ? "unreadable" : highlights.Count.ToString())}");

        foreach (var highlight in highlights?.Take(60) ?? [])
            b.AppendLine($"{indent}  cell {Safe.Read(highlight, static h => h.Position.ToString(), "?")} colour {Safe.Read(highlight, static h => h.Color.ToString(), "?")}");
    }

    private static void DescribeItems(StringBuilder b, GameController gc)
    {
        b.AppendLine();
        b.AppendLine("== Items listed by the visible tab");

        var items = CraftingSlot.ItemsOfVisibleStash(gc);

        if (items.Count == 0)
        {
            b.AppendLine("  none - the stash is closed, the tab is empty, or its items could not be read");
            return;
        }

        // Non-currency first: a currency tab lists every stack, and the item being crafted is the one that matters.
        var ordered = items.Select((x, i) => (x.Source, x.Item, Index: i))
            .OrderByDescending(x => CraftingSlot.IsCraftable(x.Item))
            .ToList();

        b.AppendLine($"  {items.Count} item element(s); craftable ones first, then currency with children omitted");

        foreach (var (source, item, index) in ordered)
        {
            var craftable = CraftingSlot.IsCraftable(item);

            b.AppendLine($"  [{index}] {source}{(craftable ? "  <== craftable" : "")}: " +
                         $"{Safe.Read(item, static i => i.Item.GetComponent<Base>()?.Name, "?")} " +
                         $"cells {Safe.Read(item, static i => $"{i.ItemWidth}x{i.ItemHeight}", "?")} " +
                         $"version {Safe.Read(item, static i => i.ItemVersion, -1)} " +
                         $"path {Safe.Read(item, static i => i.Item.Path, "?")}");
            b.AppendLine($"      element: {DescribeElement(item)}");

            if (craftable)
                DescribeChildren(b, item, "      ", 0, 3);
        }
    }

    private static void DescribeChildren(StringBuilder b, Element element, string indent, int depth, int mostDepth)
    {
        if (depth >= mostDepth)
            return;

        var children = Safe.ChildrenOf(element) ?? [];

        for (var i = 0; i < children.Count; i++)
        {
            b.AppendLine($"{indent}{new string(' ', depth * 2)}child {i}: {DescribeElement(children[i])}");
            DescribeChildren(b, children[i], indent, depth + 1, mostDepth);
        }
    }

    private static void DescribeCraftingItem(StringBuilder b, GameController gc, NormalInventoryItem item, string why)
    {
        b.AppendLine();
        b.AppendLine("== Crafting item");

        if (item == null)
        {
            b.AppendLine($"  none: {why}");
            return;
        }

        var entity = Safe.Read(item, static i => i.Item, null);
        var mods = Safe.Read(entity, static e => e.GetComponent<Mods>(), null);
        var itemBase = Safe.Read(entity, static e => e.GetComponent<Base>(), null);

        // The values an applied orb might change. Artisan (PoE1) found the entity and the element are both replaced by an
        // orb; a dump either side of one shows whether that holds here.
        b.AppendLine($"  {Safe.Read(itemBase, static x => x.Name, "?")} - {Safe.Read(entity, static e => e.Path, "?")}");
        b.AppendLine($"  element address 0x{Safe.Read(item, static i => i.Address, 0L):X}, " +
                     $"entity address 0x{Safe.Read(entity, static e => e.Address, 0L):X}, " +
                     $"entity id {Safe.Read(entity, static e => e.Id, 0u)}, " +
                     $"ItemVersion {Safe.Read(item, static i => i.ItemVersion, -1)}, " +
                     $"Mods hash {Safe.Read(mods, static m => m.Hash, 0L)}");
        b.AppendLine($"  search: \"{CraftingSlot.SearchTextOf(gc) ?? "unreadable"}\", item {CraftingSlot.SearchHighlightOf(item)}");

        if (mods == null)
        {
            b.AppendLine("  no Mods component");
            return;
        }

        b.AppendLine($"  rarity {Safe.Read(mods, static m => m.ItemRarity.ToString(), "?")}, " +
                     $"identified {Safe.Read(mods, static m => m.Identified.ToString(), "?")}, " +
                     $"item level {Safe.Read(mods, static m => m.ItemLevel, -1)}, " +
                     $"corrupted {Safe.Read(itemBase, static x => x.isCorrupted.ToString(), "?")}, " +
                     $"mirrored {Safe.Read(mods, static m => m.IsMirrored.ToString(), "?")}, " +
                     $"unique name \"{Safe.Read(mods, static m => m.UniqueName, "")}\"");

        Lines(b, "HumanStats (the tooltip's lines)", Safe.Read(mods, static m => m.HumanStats, null));
        Lines(b, "HumanImpStats", Safe.Read(mods, static m => m.HumanImpStats, null));
        Lines(b, "EnchantedStats", Safe.Read(mods, static m => m.EnchantedStats, null));
        Lines(b, "FracturedStats", Safe.Read(mods, static m => m.FracturedStats, null));
        ModList(b, "ImplicitMods", Safe.Read(mods, static m => m.ImplicitMods, null));
        ModList(b, "ExplicitMods", Safe.Read(mods, static m => m.ExplicitMods, null));
        ModList(b, "ItemMods", Safe.Read(mods, static m => m.ItemMods, null));
    }

    private static void Lines(StringBuilder b, string name, List<string> lines)
    {
        b.AppendLine($"  {name} ({(lines == null ? "unreadable" : lines.Count.ToString())}):");

        foreach (var line in lines ?? [])
            b.AppendLine($"    {OneLine(line)}");
    }

    private static void ModList(StringBuilder b, string name, List<ItemMod> list)
    {
        b.AppendLine($"  {name} ({(list == null ? "unreadable" : list.Count.ToString())}):");

        foreach (var mod in list ?? [])
        {
            var values = Safe.Read(mod, static m => m.Values, null);

            b.AppendLine($"    {Safe.Read(mod, static m => m.RawName, "?")}  name \"{Safe.Read(mod, static m => m.Name, "")}\"  " +
                         $"group \"{Safe.Read(mod, static m => m.Group, "")}\"  display \"{Safe.Read(mod, static m => m.DisplayName, "")}\"  " +
                         $"level {Safe.Read(mod, static m => m.Level, -1)}  values [{(values == null ? "" : string.Join(", ", values))}]");
            b.AppendLine($"      translation: {OneLine(Safe.Read(mod, static m => m.Translation, ""))}");
        }
    }

    /// <summary>
    /// The stash element's tree, every element on one line with its path of child indices, then what changed since the
    /// previous dump. Invisible elements are listed but not descended into, which keeps the other tabs' contents out.
    /// </summary>
    private static void DescribeTree(StringBuilder b, GameController gc, NormalInventoryItem craftingItem, int mostDepth)
    {
        b.AppendLine();
        b.AppendLine($"== Stash element tree (paths are child indices from StashElement, {mostDepth} levels at most)");

        var root = Safe.Read(gc, static g => g.IngameState.IngameUi.StashElement, null);

        if (root == null || !Safe.Read(root, static r => r.IsVisible, false))
        {
            b.AppendLine("  the stash is not open");
            return;
        }

        // Addresses worth marking where they turn up in the tree.
        var marks = new Dictionary<long, string>();

        void Mark(Element element, string what)
        {
            var address = Safe.Read(element, static e => e.Address, 0L);

            if (address != 0L)
                marks[address] = marks.TryGetValue(address, out var had) ? $"{had}, {what}" : what;
        }

        Mark(craftingItem, "crafting item");
        Mark(Safe.Read(gc, static g => g.IngameState.UIHover, null), "UIHover");
        Mark(Safe.Read(gc, static g => g.IngameState.UIHoverElement, null), "UIHoverElement");
        Mark(Safe.Read(gc, static g => g.IngameState.FocusedInputElement, null), "focused input");
        Mark(CraftingSlot.VisibleStashOf(gc), "VisibleStash");

        var tree = new Dictionary<string, string>();
        var lines = 0;

        void Walk(Element element, string path, int depth)
        {
            if (element == null || lines >= MostTreeLines)
                return;

            var description = DescribeElement(element);
            var address = Safe.Read(element, static e => e.Address, 0L);
            var mark = marks.TryGetValue(address, out var what) ? $"  <== {what}" : "";

            tree[path] = description;
            b.AppendLine($"  {new string(' ', Math.Min(depth, 30))}[{path}] {description}{mark}");
            lines++;

            if (depth >= mostDepth || !Safe.Read(element, static e => e.IsVisible, false))
                return;

            var children = Safe.ChildrenOf(element) ?? [];

            for (var i = 0; i < children.Count; i++)
                Walk(children[i], path.Length == 0 ? $"{i}" : $"{path},{i}", depth + 1);
        }

        Walk(root, "", 0);

        if (lines >= MostTreeLines)
            b.AppendLine($"  ... stopped at {MostTreeLines} lines");

        DescribeChanges(b, tree);

        _previousTree = tree;
        _previousTreeAt = DateTime.Now;
    }

    private static void DescribeChanges(StringBuilder b, Dictionary<string, string> tree)
    {
        b.AppendLine();

        if (_previousTree == null)
        {
            b.AppendLine("== Changes since the previous dump: none to compare - this is the first dump since the plugin loaded");
            return;
        }

        b.AppendLine($"== Changes since the previous dump at {_previousTreeAt:HH:mm:ss} (by path; a shifted child index shows " +
                     "as many changes)");

        var written = 0;

        void Line(string text)
        {
            if (written++ < MostChangeLines)
                b.AppendLine(text);
        }

        foreach (var (path, now) in tree)
        {
            if (!_previousTree.TryGetValue(path, out var before))
                Line($"  appeared [{path}] {now}");
            else if (before != now)
            {
                Line($"  changed  [{path}]");
                Line($"    before: {before}");
                Line($"    now:    {now}");
            }
        }

        foreach (var (path, before) in _previousTree)
        {
            if (!tree.ContainsKey(path))
                Line($"  gone     [{path}] {before}");
        }

        if (written == 0)
            b.AppendLine("  nothing changed");
        else if (written > MostChangeLines)
            b.AppendLine($"  ... {written - MostChangeLines} more lines not written");
    }

    /// <summary>
    /// One element on one line: everything that could carry a search highlight, and enough else to recognise it.
    /// Rectangles are in client pixels. Only ExileCore2's own properties are read, never Elem's raw fields, so a dump keeps
    /// working when an offset moves and ExileCore2 is updated for it.
    /// Deliberately has no address in it, because the changes section compares these strings and an address that
    /// changes on every read would mark everything changed.
    /// </summary>
    private static string DescribeElement(Element element)
    {
        if (element == null || Safe.Read(element, static e => e.Address, 0L) == 0L)
            return "none";

        var text = Safe.Read(element, static e => e.Text, null);
        var texture = Safe.Read(element, static e => e.TextureName, null);
        var entityPath = Safe.Read(element, static e => e.Entity?.Path, null);

        var parts = new StringBuilder();

        parts.Append(Rect(Safe.Read(element, static e => e.GetClientRectCache, default(RectangleF))));
        parts.Append(Safe.Read(element, static e => e.IsVisible, false) ? " visible" : " hidden");
        parts.Append($" kids {Safe.Read(element, static e => e.ChildCount, -1L)}");
        parts.Append($" isHighlighted {Safe.Read(element, static e => e.isHighlighted, false)}");
        parts.Append($" HasShinyHighlight {Safe.Read(element, static e => e.HasShinyHighlight, false)}");
        parts.Append($" IsSaturated {Safe.Read(element, static e => e.IsSaturated, false)}");
        parts.Append($" IsActive {Safe.Read(element, static e => e.IsActive, false)}");
        parts.Append($" IsVisibleLocal {Safe.Read(element, static e => e.IsVisibleLocal, false)}");
        parts.Append($" bg {Colour(Safe.Read(element, static e => e.BgColor, default(System.Drawing.Color)))}");
        parts.Append($" border {Colour(Safe.Read(element, static e => e.BordColor, default(System.Drawing.Color)))}");
        parts.Append($" scale {Safe.Read(element, static e => e.Scale, 0f):0.###}");

        if (!string.IsNullOrWhiteSpace(text))
            parts.Append($" text \"{OneLine(text)}\"");

        if (!string.IsNullOrEmpty(texture))
            parts.Append($" texture {texture}");

        if (!string.IsNullOrEmpty(entityPath))
            parts.Append($" entity {entityPath}");

        return parts.ToString();
    }

    private static string Rect(RectangleF r) => $"({r.X:0},{r.Y:0} {r.Width:0}x{r.Height:0})";

    private static string Colour(System.Drawing.Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    private static string OneLine(string text) => (text ?? "").Trim().Replace("\r", "").Replace("\n", " | ");
}
