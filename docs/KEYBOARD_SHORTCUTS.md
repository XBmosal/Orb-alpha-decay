# Keyboard shortcuts

Flow Terminal supports keyboard shortcuts for the most frequent actions. Single-key
shortcuts are **suppressed while typing in a text box** (e.g. the template name field),
so they never clobber text entry; `Ctrl` combinations always apply. Every shortcut maps
to an existing, wired control — there are no decorative bindings.

| Key | Action |
|---|---|
| `F` | Return the chart to the live (most recent) bar, keeping the current zoom |
| `R` | Reset the chart view (return to live + default time/price zoom) |
| `C` | Switch to the **Chart** workspace |
| `H` | Switch to the **Heatmap** workspace |
| `P` | Toggle the **Footprint** study on the chart |
| `I` | Open the **Indicators** menu |
| `Space` | Play / pause the replay (paced stream) |
| `Esc` | Close any open flyout (Indicators, Templates, Instrument, Contract) |
| `Ctrl + Z` | Undo the last chart drawing |
| `Ctrl + Y` | Redo the last chart drawing |

Shortcut hints are also shown in the tooltips of the controls they drive (workspace
tabs, playback, indicators, undo/redo).

## Notes

- Shortcuts are observational only — like the rest of Flow Terminal, they never place,
  modify, or cancel an order.
- `Space` toggles the same paced-stream pause as the transport ▶/❚❚ button; ingestion of
  canonical events continues regardless (only the visual/paced playback is paused).
- The footprint study toggle (`P`) and indicator menu (`I`) honour the current data and
  capability state, exactly as the on-screen controls do.
