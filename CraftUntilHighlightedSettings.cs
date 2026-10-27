using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using ExileCore2.Shared.Attributes;
using ExileCore2.Shared.Interfaces;
using ExileCore2.Shared.Nodes;
using ImGuiNET;
using Newtonsoft.Json;

namespace CraftUntilHighlighted;

/// <summary>
/// A heading inside a settings section, drawn as `--- Dump ------------`. Declared as a CustomNode property because that
/// is the only way to put something of a plugin's own into the attribute menu, and marked JsonIgnore because a heading has
/// nothing to save. The menu draws properties in declaration order, so a heading is the property above the first setting
/// it covers. The same pattern as AutoExpedition's Section.
/// </summary>
internal static class Section
{
    public static CustomNode Of(string title) => new(() => ImGui.SeparatorText(title));
}

public class CraftUntilHighlightedSettings : ISettings
{
    public ToggleNode Enable { get; set; } = new(false);

    /// <summary>
    /// Which built-in changes this saved file has had applied. A saved file holds a value for every setting, so a changed
    /// default never reaches it on its own; a change that must reach saved files raises this and adds a step to
    /// CraftUntilHighlighted.ApplySettingsChanges. The same pattern as AutoExpedition's ConfigVersion.
    /// </summary>
    [IgnoreMenu]
    public int ConfigVersion { get; set; }

    [Submenu(CollapsedByDefault = true)]
    public CraftingSettings Crafting { get; set; } = new();

    /// <summary>
    /// The currencies the craft square appears for. Each line is a regular expression, ignoring case, that must match the
    /// whole of a currency's base name or the whole of its metadata path. A currency right-clicked for use that no line
    /// matches gets no square, so no run. See SelectedCurrency.IsAllowed. The defaults are the Chaos Orb and Greater Chaos
    /// Orb, named as on poe2db, and every quality currency by path (see AllowedCurrencyDefaults).
    /// </summary>
    [Menu("Allowed currencies",
        "The craft square only appears while a currency matching one of these is right-clicked for use.\n" +
        "\n" +
        "Each line is a regular expression, ignoring case, that must match the whole\n" +
        "name or the whole metadata path:\n" +
        "  Chaos Orb                                    only the Chaos Orb\n" +
        "  .* Catalyst                                  every catalyst\n" +
        "  Metadata/Items/Currency/CurrencyRerollRare.* the Chaos Orbs by path\n" +
        "\n" +
        "A dump shows the selected currency's name and metadata path.",
        CollapsedByDefault = true)]
    public ContentNode<TextNode> AllowedCurrencies { get; set; } = new()
    {
        Content = AllowedCurrencyDefaults.Select(name => new TextNode(name)).ToList(),
        EnableControls = true,
        ItemFactory = () => new TextNode(""),
        UseFlatItems = true,
    };

    /// <summary>
    /// The Allowed currencies a new install starts with. The quality pattern matches the six quality currencies' paths
    /// read off dumps (CurrencyMagicQuality, CurrencyArmourQuality, CurrencyWeaponQuality, CurrencyFlaskQuality,
    /// CurrencyGemQuality, CurrencyJewelleryQualityCaster; 2026-10-27), and any other currency with Quality in its path.
    /// </summary>
    public static readonly string[] AllowedCurrencyDefaults =
        ["Chaos Orb", "Greater Chaos Orb", "Metadata/Items/Currency/Currency.*Quality.*"];

    [Submenu(CollapsedByDefault = true)]
    public DisplaySettings Display { get; set; } = new();

    [Submenu(CollapsedByDefault = true)]
    public DebugSettings Debug { get; set; } = new();
}

public class DisplaySettings
{
    /// <summary>
    /// How long a message under the cursor stays up, in seconds; it fades over its last second. AutoExpedition's cursor
    /// warnings default to 4 as well. See CursorMessage.
    /// </summary>
    [Menu("Cursor message duration (s)")]
    public RangeNode<int> CursorMessageSeconds { get; set; } = new(4, 1, 20);

    /// <summary>
    /// The craft square's side as a percentage of the crafting item's width, so it scales with the client's resolution.
    /// It sits inset from the item's bottom-left corner and is shrunk to fit inside the item. See CraftingSlot.CraftSquareOf.
    /// </summary>
    [Menu("Craft square size (% of item width)")]
    public RangeNode<int> CraftSquareSizePercent { get; set; } = new(45, 10, 100);

    /// <summary>
    /// The craft square while the search does not highlight the item and the cursor is outside it. AutoExpedition's
    /// yellow, its later explosives and rare chests colour.
    /// </summary>
    [Menu("Craft square colour")]
    public ColorNode CraftSquareColour { get; set; } = new(Color.FromArgb(255, 255, 210, 80));

    /// <summary>
    /// The craft square while the cursor is inside it and the search does not highlight the item. AutoExpedition's
    /// green, its next explosive and pick colour.
    /// </summary>
    [Menu("Craft square colour under the cursor")]
    public ColorNode CraftSquareHoveredColour { get; set; } = new(Color.FromArgb(255, 90, 255, 120));

    /// <summary>
    /// The craft square while the search highlights the item, wherever the cursor is. AutoExpedition's purple, its
    /// reward and high tier colour.
    /// </summary>
    [Menu("Craft square colour when highlighted")]
    public ColorNode CraftSquareHighlightedColour { get; set; } = new(Color.FromArgb(255, 190, 120, 255));
}

public class DebugSettings
{
    /// <summary>
    /// Whether the debug drawing is on: a frame round the item taken to be in the crafting slot, a frame round the
    /// element the game reports under the cursor, and a status line. It does not gate the dump, which is taken with it
    /// off as well.
    /// </summary>
    [Menu("Enable debug mode",
        "Frames the crafting item and the element under the cursor,\n" +
        "with a status line above the cursor.")]
    public ToggleNode ShowOverlay { get; set; } = new(false);

    /// <summary>
    /// Whether a run starts with the first-use warning, the countdown CraftLoop shows before it. On for a new install,
    /// and turned off when the countdown first runs to its end, so the warning is shown once; turning it on again shows
    /// it before the next run. See CraftLoop.TickFirstUseCountdown.
    /// </summary>
    [Menu("Enable first time use warning")]
    public ToggleNode EnableFirstUseWarning { get; set; } = new(true);

    [JsonIgnore]
    public CustomNode DumpGroup { get; set; } = Section.Of("Dump");

    /// <summary>
    /// Writes what the plugin can read about the stash, the crafting item, the cursor and the stash's interface elements
    /// to a file. Unbound by default. Works whether or not the plugin is enabled. See Dump.
    /// </summary>
    [Menu("Dump key",
        "Writes what the plugin can read about the stash, the crafting\n" +
        "item and the cursor to a file, with what changed since the last dump.")]
    public HotkeyNodeV2 DumpHotkey { get; set; } = new(Keys.None);

    /// <summary>
    /// Takes a dump now, for when the key is not bound. It raises a flag rather than writing anything, because a
    /// CustomNode is handed neither the plugin nor the GameController; the tick that answers the key answers this too.
    /// </summary>
    [JsonIgnore]
    public CustomNode DumpNowUi { get; set; } = new(() =>
    {
        if (ImGui.Button("Write a dump now###cuhDumpNow"))
            Dump.AskedForInSettings = true;

        if (Dump.LastWritten.Length == 0)
            return;

        ImGui.SameLine();
        ImGui.TextDisabled($"last: {Dump.LastWritten}");
    });

    [JsonIgnore]
    public CustomNode DumpFolderUi { get; set; } = new(() =>
    {
        if (ImGui.Button("Open dump folder###cuhOpenDumps"))
            DumpFolder.Open();
    });

    /// <summary>How many timestamped dumps to keep, oldest deleted first.</summary>
    [Menu("Max dump files")]
    public RangeNode<int> MaxDumps { get; set; } = new(10, 1, 100);

    /// <summary>
    /// How many levels below the stash element the element tree in a dump goes. The crafting slot's item sits some way
    /// down, and a tree cut short of it shows nothing about it.
    /// </summary>
    [Menu("Dump element depth")]
    public RangeNode<int> DumpElementDepth { get; set; } = new(16, 4, 30);
}

/// <summary>
/// A named item changed delay range. The values were chosen, not measured: each range is 67 ms wide and follows on from
/// the one before, Fast below Normal below Slow.
/// </summary>
public sealed record TimingPresetRanges(string Name, int ItemChangedMinDelayMs, int ItemChangedMaxDelayMs)
{
    public const string Custom = "Custom";

    public static readonly TimingPresetRanges Fast = new("Fast", 99, 166);
    public static readonly TimingPresetRanges Normal = new("Normal", 165, 232);
    public static readonly TimingPresetRanges Slow = new("Slow", 231, 298);

    public static readonly IReadOnlyList<TimingPresetRanges> All = [Fast, Normal, Slow];

    /// <summary>The dropdown's choices: the presets, then Custom, which the dropdown shows once a range is edited by hand.</summary>
    public static List<string> Names => [Fast.Name, Normal.Name, Slow.Name, Custom];

    public static TimingPresetRanges Named(string name)
    {
        foreach (var preset in All)
        {
            if (preset.Name == name)
                return preset;
        }

        return null;
    }

    public bool Matches(CraftingSettings settings) =>
        settings.ItemChangedMinDelayMs.Value == ItemChangedMinDelayMs &&
        settings.ItemChangedMaxDelayMs.Value == ItemChangedMaxDelayMs;

    public void ApplyTo(CraftingSettings settings)
    {
        settings.ItemChangedMinDelayMs.Value = ItemChangedMinDelayMs;
        settings.ItemChangedMaxDelayMs.Value = ItemChangedMaxDelayMs;
    }
}

public class CraftingSettings
{
    /// <summary>How many orbs one run may apply before it stops. 0 is unlimited. See CraftLoop.</summary>
    [Menu("Maximum orbs per run", "0 = unlimited.")]
    public RangeNode<int> MaximumOrbsPerRun { get; set; } = new(0, 0, 1000);

    [JsonIgnore]
    public CustomNode TimingGroup { get; set; } = Section.Of("Timing");

    /// <summary>
    /// How long after a click the item may take to change before the run stops. An orb on an empty stack stays selected
    /// and does nothing when clicked, so this is also how a run ends when the stack runs out. See CraftLoop.
    /// </summary>
    [Menu("Item changed timeout (ms)",
        "Stops the run if a click does not change the item in this time,\n" +
        "such as when the orbs run out.")]
    public RangeNode<int> ItemChangedTimeoutMs { get; set; } = new(1000, 500, 2000);

    /// <summary>
    /// Sets the two delays below to a preset. Shows Custom once any of them is changed by hand. The choices are supplied
    /// on every start (ListNode does not save them), and selecting one applies it; see CraftUntilHighlighted.Initialise.
    /// </summary>
    [Menu("Timing preset", "Sets the delays below.")]
    public ListNode TimingPreset { get; set; } = new() { Value = TimingPresetRanges.Fast.Name };

    /// <summary>
    /// Shortest time between the item changing and the next click. Each delay draws a value between the min and the max.
    /// </summary>
    [Menu("Item changed min delay (ms)", "Time between the item changing and the next click.")]
    public RangeNode<int> ItemChangedMinDelayMs { get; set; } = new(TimingPresetRanges.Fast.ItemChangedMinDelayMs, 33, 957);

    [Menu("Item changed max delay (ms)")]
    public RangeNode<int> ItemChangedMaxDelayMs { get; set; } = new(TimingPresetRanges.Fast.ItemChangedMaxDelayMs, 66, 990);
}
