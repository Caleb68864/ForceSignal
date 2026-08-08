// The confidence ladder moved down into the shared ground-combat layer, where both games test
// against it. Importing it globally keeps every existing StarGrunt test file byte-for-byte as it
// was, which is the point of them: they are the guard that the move changed no behaviour, and a
// guard you had to edit to make the change compile has stopped guarding anything.
global using ForceSignal.Modules.GroundCombat.Morale;
