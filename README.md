# CraftUntilHighlighted

An ExileCore2 plugin for Path of Exile 2: a lightweight crafter that applies a currency to an item until the stash search highlights it.

## Install

Install using **PluginUpdater**:

1. Add > paste this plugin's URL: `https://github.com/bnc-anonymous/CraftUntilHighlighted` > Clone
2. Add > paste ExileInput2's URL: `https://github.com/bnc-anonymous/ExileInput2` > Clone

## Usage

1. Put an item in your currency tab's crafting slot.
2. Type a regex for the modifiers you want into the stash search.

   ![The stash search with a regex typed in](images/0.jpg)

3. Right-click the currency you want to use. It must be on the **Allowed currencies** list in the plugin's settings (Chaos Orb, Greater Chaos Orb and the quality currencies by default).
4. Move the cursor into the square at the bottom-left of the item.

The plugin will use the currency with a randomised delay until the item is highlighted. You can then click again yourself to roll past a match. Move the cursor out of the square to stop.

| Ready | Crafting | Highlighted |
| --- | --- | --- |
| ![The square on the item, before a run](images/1.jpg) | ![The square while crafting](images/2.jpg) | ![The square once the search highlights the item](images/3.jpg) |

## License

MIT
