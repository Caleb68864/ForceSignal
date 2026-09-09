import type { OrderPreview } from '../types.ts';

/**
 * What the server already decided about a draft order, said out loud.
 *
 * The preview round trip returns `isValid` and `errors` precisely so a player can find out why a
 * plot will not lock before they press Lock and are refused. Both were fetched and thrown away: the
 * only part of the answer anything read was the path, drawn on the map. A player editing a plot that
 * had gone wrong got no signal at all until the moment they committed it.
 *
 * Its own component so it can be tested without standing up the whole map, and so the map is left
 * saying where the ship goes rather than arguing about it.
 */
export function OrderPreviewNotice({ preview }: { preview: OrderPreview | null }) {
  if (!preview) {
    return null;
  }

  if (!preview.isValid) {
    return (
      <div role="status" className="privacy">
        <strong>This plot will not lock.</strong>
        {/* Keyed on the text because the server sends reasons, not identifiers, and a reason is
            distinct from every other reason in the same list. */}
        <ul>
          {preview.errors.map((error) => <li key={error}>{error}</li>)}
        </ul>
      </div>
    );
  }

  // A legal plot that leaves the table is a decision rather than a mistake - a ship may withdraw -
  // so it is said once and does not claim the plot is broken.
  return preview.runsOffTable
    ? <p role="status" className="privacy">This plot carries the ship off the table.</p>
    : null;
}
