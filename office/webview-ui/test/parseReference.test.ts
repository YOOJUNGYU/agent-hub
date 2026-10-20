import assert from 'node:assert/strict';

import { test } from 'vitest';

import { extractToolName, parseReference } from '../src/office/toolUtils.js';

test('reading-tool statuses become reference-card rows', () => {
  assert.deepEqual(parseReference('Fetching https://example.com/a'), {
    icon: '\u{1F310}',
    text: 'example.com/a',
    href: 'https://example.com/a',
  });
  assert.equal(parseReference('Fetching web content'), null);
  assert.equal(parseReference('Searching the web: pixel art')?.text, '"pixel art"');
  assert.equal(parseReference('Searching code: foo in ~/src')?.text, 'foo in ~/src');
  assert.equal(parseReference('Searching files: **/*.ts')?.text, '**/*.ts');
  assert.equal(parseReference('Reading ~/a/b.md')?.text, '~/a/b.md');
  assert.equal(parseReference('Running: ls'), null);
});

test('detailed statuses still map back to their tool', () => {
  assert.equal(extractToolName('Searching the web: x'), 'WebSearch');
  assert.equal(extractToolName('Searching files: *.ts'), 'Glob');
  assert.equal(extractToolName('Searching code: foo'), 'Grep');
  assert.equal(extractToolName('Fetching https://a.b'), 'WebFetch');
});
