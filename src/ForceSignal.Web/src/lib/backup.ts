/**
 * Getting the last device backup off this browser as a file.
 *
 * Lives outside the app so the error boundary can offer it too: a render that has thrown is
 * exactly the moment a player most wants the last known state of the match in their hands.
 */

import { snapshotBackupKey } from '../constants.ts';
import { downloadText, slugify } from './format.ts';
import { readJson } from './api.ts';
import type { MatchSnapshot } from '../types.ts';

/** Downloads the stored backup, if there is one, and says what happened. */
export function exportLastDeviceBackup(): string {
  const backup = localStorage.getItem(snapshotBackupKey);
  if (!backup) {
    return 'No local snapshot backup found on this device.';
  }

  const parsedBackup = readJson<{ savedAt?: string; snapshot?: Partial<MatchSnapshot> }>(snapshotBackupKey);
  const matchName = typeof parsedBackup?.snapshot?.name === 'string' && parsedBackup.snapshot.name
    ? slugify(parsedBackup.snapshot.name)
    : 'forcesignal';
  const turnNumber = typeof parsedBackup?.snapshot?.turnNumber === 'number' ? parsedBackup.snapshot.turnNumber : 'last';
  downloadText(`${matchName}-turn-${turnNumber}-device-backup.json`, 'application/json', `${backup}\n`);
  return 'Last local device snapshot exported.';
}
