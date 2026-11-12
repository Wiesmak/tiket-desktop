# Native shell UX — Phase 1

Cashier opens first, unconnected. All fixture statuses are explicitly MOCK; all unavailable backend actions remain disabled. The native title bar carries the app identity; a TitleTextBlockStyle heading shares the scrolling workspace container. There is no text pane footer. Auto navigation uses the native 640/1008 effective-pixel thresholds, a 220-pixel open pane and native compact/minimal menu behavior. Settings remains in the native footer.

The workspace fills available width with 24-pixel horizontal gutters (12 in minimal mode). There is no global width cap. Title, mock InfoBar, context and major work surfaces share this container. The top gutter is 16 pixels in compact/expanded modes; minimal mode reserves the native 48-pixel menu band before the title, without a duplicate NavigationView.Header or negative margins.

At **900 effective content pixels**, after navigation and padding, two equally weighted columns use a 24-pixel gap. Primary columns stop growing at 680 pixels for cashier inputs and 760 for the builder; supporting panels use the remaining width. Below 900, full-width sections stack. XAML visual states hold these setters; the existing view-only size event measures actual workspace width.

Cashier always shows a non-interactive most-recent fictional failed-print summary above the entry editor. The full fictional local list is beside the editor in columns and below it when stacked. Pending, dispatched and failed have distinct words; dispatched explicitly does not confirm paper delivery. This is neither a POS-wide query nor an actual print queue. Player, bet type, selection mode, illustrative runner checks, units and unavailable quote follow in order.

Administration always shows the immutable server DRAFT warning before the builder. Basics shows temporary event text/context, Races shows temporary race title/order, Runners shows a labelled string roster, Pool rules describes the supported catalogue without pricing, and Review explains future checks with creation disabled. Step changes only reveal existing controls; temporary input survives navigation and step changes until exit. The secondary fixture summary is explicitly not validation of those inputs. Nothing is saved or submitted.

Appearance is System by default and lasts only for the session. The page uses a theme-resource background so a forced Light theme remains readable over an OS-dark Mica window. MainWindow title bar and system controls retain their native behavior.

Tab/Shift+Tab traverse native controls. Space opens the focused menu button, Escape dismisses it and restores native menu focus. With the pane open, Alt+C/Alt+A select Cashier/Administration. Alt+P targets the cashier player input when visible. Enter/Space use native combo/check behavior; no financial shortcuts are intercepted. A destination change focuses its header after overlay-pane dismissal and resets content scrolling; theme changes retain the appearance control. Settings is keyboard reachable through the pane.

Measured Phase 1 coverage is in [VALIDATION_PHASE_1.md](VALIDATION_PHASE_1.md). Narrator, high contrast, full text scaling and multi-DPI certification are deferred to Phase 8. Native labels, heading semantics, automation names and focus cues remain; no accessibility certification or operational readiness is claimed.

Future cashier composition uses authoritative server quotes; purchases require durable exact intents and recovery. Administration prepares a complete local definition before one-shot creation. These features remain outside Phase 1.

## Corrective continuation — 2026-11-12

Initial destination selection must not close the pane before Loaded: the initial DisplayMode is not yet the measured window mode. Overlay dismissal now applies only to loaded navigation. Auto keeps its native thresholds and user-collapse policy: manual collapse survives wide resizing, navigation and theme changes; explicit manual collapse also survives a narrow→wide cycle; without explicit collapse Auto expands again on returning wide. No resize handler writes IsPaneOpen. Latest print remains before cashier inputs; the compact global mock message avoids repeating the same disclaimer. The immutable-DRAFT warning belongs to the builder column and precedes its selector.

Workspace and builder-step changes use a native fade-in and a small vertical reposition on existing controls. Rapid switching stops the previous transition. Initial construction, resizing and theme selection do not trigger this animation. Windows Animation effects preference is checked on each change; disabled animations switch immediately. Controls, temporary values and focus are preserved throughout.
