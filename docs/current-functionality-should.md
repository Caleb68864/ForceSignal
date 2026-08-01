# ForceSignal Current Functionality: Intended Behavior

This note describes how the current app should behave before the next UI cleanup pass. It is a baseline for small, focused changes.

## Product Scope

ForceSignal should behave as a tabletop helper for in-person space fleet games. It should support local/session-based matches, browser recovery exports, and shared table visualization without requiring PostgreSQL or bundled official rules content.

## Match Setup

- A player should be able to create or join a match with a display name and room code.
- The app should expose the room code, current phase, turn number, and table size clearly.
- Table width/depth should represent the physical play area and should constrain ship positions on the Play Map.
- The app should show the unofficial companion disclaimer and link outward to official rules.

## Fleet And Ship Management

- Players should be able to create fleets, assign fleet color/accent, and add ships.
- Ships should support name, class, icon, thrust, velocity, course, starting position, hull, armor, screens, weapons, and fighter-specific settings.
- Ship class presets should speed up data entry but remain editable.
- Ships should carry a points value (NPV) taken from the player's own design sheet, and fleets should total it.
- A player should be able to keep a device-local library of prebuilt fleets and bring one into any match.
- The owner should be able to set an agreed points ceiling per player; an over-strength fleet should be blocked
  from readying until it is trimmed or the owner changes the limit.
- Ship cards should track damage, disabled drive/fire-control/weapon hits, screens, destroyed state, current movement, and weapon/ammo state.
- Editing should feel like a ship profile workflow, not a crowded control dump.

## Export, Import, And Recovery

- JSON/CSV fleet export should include ship icon, fleet color, weapons, ammo, fighter endurance/range/status, and starting position.
- JSON/CSV fleet import should tolerate reasonable column aliases and normalize missing/older fields.
- Match snapshot export should capture the whole current match state.
- The join screen should allow exporting the last device backup for recovery.
- After-action exports should summarize table size, fleets, ships, ordnance, and battle log entries.
- A snapshot backup should restore a full match, with each player claiming their original seat.
- Restoring a snapshot taken during order entry should reopen order entry and say that locked orders could not be recovered.

## Turn Flow

- The intended flow is: setup, order entry, reveal, movement, firing, advance turn.
- Players should commit/reveal movement orders without exposing hidden order details too early.
- Movement should respect current thrust limits, including multiple turns in one order.
- The match log should record meaningful battle context: orders, movement, firing, damage, ordnance, fighter ops, table state, and turn transitions.
- End-of-turn checklist should warn about unresolved fire, active ordnance, fighter endurance, carrier ops, crippled ships, disabled drives, and out-of-bounds ships.

## Play Map

- The Play Map should be the main table view, not an overloaded second control panel.
- The map should support pan, zoom, fit, center selected, next contact, measurement, hover/tap status, and mobile/tablet gestures.
- Mouse wheel or trackpad zoom on the map should not also scroll the page.
- Ship markers should use the selected ship class icon and fleet color.
- Damage overlays should show screens active, disabled drive, fire-control hit, and destroyed state.
- Movement trails should show resolved movement; movement preview should show only the selected ship's planned path.
- Range rings and firing arcs should show only for the currently selected ship.
- Fighter max operating range should center on the assigned carrier; fighter endurance range should center on the fighter group.
- Ordnance markers should show launched missiles/salvos and allow owner-only updates/removal.

## Map Interaction Should Change Next

- Selecting a ship on the map should make that ship the only active planning subject.
- Right click, long press, or a clear touch action on map space should set movement/course for the selected friendly ship.
- Right clicking or long pressing a ship marker should select that ship first, then offer relevant actions.
- Course controls, firing controls, fighter ops, and ordnance controls should appear only for the selected ship.
- Non-selected ships should remain visible as contacts but should not add extra control panels.
- Opponent ships should be selectable for stats/range/targeting, but movement controls should remain read-only.

## Firing And Damage

- During firing phase, a player should select one friendly attacker, one target, one weapon, arc, and range.
- The firing UI should make range/arc decisions easier by using selected-ship map overlays.
- Limited-ammo weapons should show spent/available state and reject firing when empty.
- Damage should apply to armor first, then hull, and destroyed ships should be visibly marked.
- Firing results and match log should include attacker, target, weapon, arc, range, range band, screen/system reductions, damage, and ammo notes.

## Fighter And Carrier Operations

- Fighter groups should support launch, return/recover, endurance used/max, max operating range, and home carrier assignment.
- Carrier panels should summarize assigned, airborne, recovering, and damage-adjusted bay estimates.
- Fighter and carrier controls should appear only when the selected ship makes those controls relevant.

## Public Display And Print

- Public display mode should prioritize the table/map view and hide private command controls.
- Printed ship cards should be compact, readable, and focused on tabletop tracking.
- Tablet and phone layouts should keep controls touch-friendly without making the screen feel overstuffed.

## Docker Deployment

- Docker should run only the API and web services for the current tabletop-helper scope.
- Docker smoke should check API health, readiness, web health, web shell, match creation, and SignalR negotiation.
- Readiness should report in-memory persistence warnings until durable storage is intentionally added later.
