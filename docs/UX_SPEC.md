# Native shell UX — Phase 0

Adapted from the inception UX specification and cashier/administration wireframes. The application is genuine WinUI 3 XAML. The wireframe's production statuses are replaced by explicit mock/unconnected labels.

Cashier opens first. Station, sample event and unconnected warning precede race selection context, player placeholder, bet type/selection examples, units, unavailable quote and disabled purchase. Nearby recent-ticket examples are fictional and explain both partial local history and the limits of DISPATCHED. At narrow effective widths (below 1000), recent tickets move below the editor; a vertical ScrollViewer preserves access without tiny text.

Administration previews Basics, Races, Runners, Pool rules and Review with a native keyboard-operable combo. Each step describes future local work. The compact roster accepts temporary text only; there is no import, persistence, real validation or create operation. Settings describes the absent connection and allows system/light/dark appearance.

Use Tab/Shift+Tab to traverse, arrows for native combos/navigation, Space for checks and Alt access keys C/A/P for Cashier/Administration/player input. Navigation focuses the destination heading; focus uses native cues. Controls have headers or automation names; headings have automation heading levels. Status uses words, not color alone. ThemeResource brushes and built-in controls support Windows themes. No custom financial shortcuts intercept Enter.

Required acceptance includes 1366x768 at actual 125%, 1920x1080, text scaling, keyboard-only use, Narrator and Windows high contrast. See VALIDATION.md for measured coverage and outstanding checks; layout design is not evidence of accessibility certification.

Future cashier flow: known event/player IDs, backend roster/account reads, explicit selection, authoritative server quote, review, durable exact intent, purchase acknowledgement or UNKNOWN recovery. No offline betting or local price authority. Future administration prepares the complete definition locally, reviews immutability, creates once, then publishes/starts as separate authorized commands.
