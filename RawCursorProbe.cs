using System;
using System.Text;
using ExileCore2;
using ExileCore2.PoEMemory;
using ExileCore2.Shared.Interfaces;

namespace CraftUntilHighlighted;

/// <summary>
/// The dump's raw view of the cursor element, for repairing Offsets after a patch: every 4 bytes of its first ProbeBytes
/// tried as a BaseItemTypes hash, with the ones that name an item type listed, then the bytes 8 at a time, each split
/// into two ints, with any readable text a pointer leads to. Reads only, and only when a dump is written. See Offsets for
/// how to use it.
/// </summary>
internal static class RawCursorProbe
{
    private const int ProbeBytes = 0x600;
    private const int SecondStepBytes = 0x100;
    private const int MinimumTextLength = 5;

    public static void Describe(StringBuilder b, GameController gc, Element cursor)
    {
        var memory = Safe.Read(gc, static g => g.Memory, null);
        var address = Safe.Read(cursor, static c => c.Address, 0L);

        b.AppendLine();
        b.AppendLine("== Raw cursor memory (for repairing Offsets)");

        if (memory == null || address == 0L)
        {
            b.AppendLine("  memory or cursor unreadable");
            return;
        }

        b.AppendLine($"  IngameUi.Cursor at 0x{address:X}");

        var bytes = Safe.Read(() => memory.ReadBytes(address, ProbeBytes), null);

        if (bytes == null || bytes.Length < ProbeBytes)
        {
            b.AppendLine("    unreadable");
            return;
        }

        // Every 4 bytes tried as a BaseItemTypes hash: the offset whose line names the selected currency is
        // Offsets.CursorSelectedItemTypeHash. A dictionary lookup each, so the whole element costs little.
        b.AppendLine($"  4-byte values that name an item type (Offsets.CursorSelectedItemTypeHash is +0x{Offsets.CursorSelectedItemTypeHash:X}):");

        var named = 0;

        for (var offset = 0; offset < ProbeBytes; offset += 4)
        {
            var value = BitConverter.ToUInt32(bytes, offset);

            if (SelectedCurrency.ItemTypeOfHash(gc, value) is not { } itemType)
                continue;

            named++;
            b.AppendLine($"    +0x{offset:X3} 0x{value:X8}: {Safe.Read(itemType, static t => t.BaseName, "?")} - " +
                         $"{Safe.Read(itemType, static t => t.Metadata, "?")}");
        }

        if (named == 0)
            b.AppendLine("    none");

        b.AppendLine("  8 bytes at a time:");

        for (var offset = 0; offset < ProbeBytes; offset += 8)
        {
            var value = BitConverter.ToInt64(bytes, offset);
            var line = $"    +0x{offset:X3}: 0x{value:X16}  ({BitConverter.ToInt32(bytes, offset)}, {BitConverter.ToInt32(bytes, offset + 4)})";

            if (LooksLikePointer(value) && TextReachableFrom(memory, value) is { Length: > 0 } found)
                line += "  -> " + found;

            b.AppendLine(line);
        }
    }

    /// <summary>Readable text at the pointer, and at each pointer in the SecondStepBytes it points to.</summary>
    private static string TextReachableFrom(IMemory memory, long pointer)
    {
        var found = new StringBuilder();

        if (TextAt(memory, pointer) is { } direct)
            found.Append($"\"{direct}\"");

        var block = Safe.Read(() => memory.ReadBytes(pointer, SecondStepBytes), null);

        if (block == null || block.Length < SecondStepBytes)
            return found.ToString();

        for (var offset = 0; offset < SecondStepBytes; offset += 8)
        {
            var next = BitConverter.ToInt64(block, offset);

            if (LooksLikePointer(next) && TextAt(memory, next) is { } text)
                found.Append(found.Length > 0 ? " | " : "").Append($"[+0x{offset:X2}] \"{text}\"");
        }

        return found.ToString();
    }

    /// <summary>UTF-16 or ASCII text of printable characters at the address, or null.</summary>
    private static string TextAt(IMemory memory, long address) =>
        Printable(Safe.Read(() => memory.ReadStringU(address, 256, true), null)) ??
        Printable(Safe.Read(() => memory.ReadString(address, 128, true), null));

    private static string Printable(string text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        var end = text.IndexOf('\0');

        if (end >= 0)
            text = text[..end];

        if (text.Length < MinimumTextLength)
            return null;

        foreach (var c in text)
        {
            if (c < 0x20 || c > 0x7E)
                return null;
        }

        return text;
    }

    private static bool LooksLikePointer(long value) => value is > 0x10000 and < 0x7FFF_FFFF_FFFF && (value & 0x7) == 0;
}
