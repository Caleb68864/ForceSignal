/**
 * Pays the cold start of a DOM test file before its first test, under a budget of its own.
 *
 * Each test file gets fresh modules, so the first test in every jsdom file used to pay for React's
 * first render, testing-library's first role query (aria-query and the accessible-name code warm up
 * on first use: measured at ~300 ms cold against ~25 ms warm) and jsdom's first selector work - on
 * top of whatever it was testing. Unloaded that put the first test of a file at 1-3 s while the rest
 * ran in a few hundred ms; on a loaded machine the same first test ran past vitest's 5 s and failed
 * ("launches an assault ..." in DirtsideAssault.test.tsx, the first test of StarGruntView.test.tsx,
 * DirtsideView.test.tsx and others). The cost is the libraries', not the test's, so it is paid here
 * once per file, and no test's timeout is touched.
 *
 * Node-environment files have no document and skip it.
 */
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { beforeAll } from 'vitest';

function Warm() {
  return (
    <form aria-label="warm">
      <label>
        Warm field
        <input defaultValue="" />
      </label>
      <label>
        Warm choice
        <select defaultValue="a">
          <option value="a">a</option>
          <option value="b">b</option>
        </select>
      </label>
      <label>
        <input type="checkbox" />
        Warm tick
      </label>
      <button type="button">Warm button</button>
    </form>
  );
}

beforeAll(() => {
  if (typeof document === 'undefined') {
    return;
  }
  render(<Warm />);
  fireEvent.change(screen.getByLabelText('Warm field'), { target: { value: '1' } });
  fireEvent.change(screen.getByLabelText('Warm choice'), { target: { value: 'b' } });
  fireEvent.click(screen.getByRole('checkbox', { name: 'Warm tick' }));
  screen.getByRole('button', { name: 'Warm button' });
  screen.getAllByRole('combobox');
  screen.getByText('Warm button');
  cleanup();
}, 120_000);
