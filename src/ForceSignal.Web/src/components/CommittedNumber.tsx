import { useState } from 'react';
import { wholeNumberFrom } from '../lib/format.ts';

/**
 * A whole-number field that reports a value only when editing finishes - on blur or Enter - and
 * clamps it into range on the way out. Between edits it tracks the value it was given.
 */
export function CommittedNumber({ value, min, max, disabled, onCommit }: {
  value: number;
  min: number;
  max: number;
  disabled?: boolean;
  onCommit: (value: number) => void;
}) {
  const [text, setText] = useState(String(value));
  const [editing, setEditing] = useState(false);

  function commit() {
    setEditing(false);
    const next = wholeNumberFrom(text, value, min, max);
    setText(String(next));
    if (next !== value) {
      onCommit(next);
    }
  }

  return (
    <input
      type="number"
      min={min}
      max={max}
      disabled={disabled}
      value={editing ? text : String(value)}
      onFocus={() => {
        setText(String(value));
        setEditing(true);
      }}
      onChange={(event) => setText(event.target.value)}
      onBlur={commit}
      onKeyDown={(event) => {
        if (event.key === 'Enter') {
          event.preventDefault();
          event.currentTarget.blur();
        }
      }}
    />
  );
}
