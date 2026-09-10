/**
 * No hardcoded colours outside the theme.
 *
 * This exists because of a real bug, not a hypothetical one. The first shell
 * hardcoded `backgroundColor: '#ffffff'` while letting Text take RNW 0.84's
 * theme-aware default foreground. In dark mode that is near-white text on a
 * white ground: the window rendered, and looked completely blank.
 *
 * The same mistake defeats Windows high contrast themes, which GitApp treats
 * as first class (docs/ARCHITECTURE.md section 5). A hex literal cannot
 * respond to a theme; a PlatformColor can.
 *
 * src/theme is the one place allowed to name colours, and even there they are
 * platform colours rather than literals.
 */

import fs from 'fs';
import path from 'path';

const SRC = path.join(__dirname, '..', '..', 'src');

/** #rgb, #rrggbb, #rrggbbaa. */
const HEX = /#[0-9a-fA-F]{3,8}\b/g;
/** rgb(), rgba(), hsl(), hsla(). */
const FUNCTIONAL = /\b(?:rgba?|hsla?)\s*\(/g;

function sourceFiles(dir: string): string[] {
  return fs.readdirSync(dir, {withFileTypes: true}).flatMap(entry => {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      return sourceFiles(full);
    }
    return /\.tsx?$/.test(entry.name) ? [full] : [];
  });
}

/**
 * Strip comments before scanning. The theme module documents colour values in
 * prose, and a doc comment explaining why a literal is wrong should not itself
 * trip the check.
 */
function stripComments(source: string): string {
  return source
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/(^|[^:])\/\/.*$/gm, '$1');
}

describe('theme discipline', () => {
  const files = sourceFiles(SRC).filter(
    f => !f.includes(path.join('src', 'theme')),
  );

  it('finds source files to check', () => {
    expect(files.length).toBeGreaterThan(0);
  });

  it.each(files.map(f => [path.relative(SRC, f), f]))(
    '%s uses theme tokens rather than colour literals',
    (_name, file) => {
      const code = stripComments(fs.readFileSync(file, 'utf8'));

      const offenders = [
        ...(code.match(HEX) ?? []),
        ...(code.match(FUNCTIONAL) ?? []),
      ];

      expect(offenders).toEqual([]);
    },
  );

  it('defines every token in all three palettes', () => {
    // A token added to light but forgotten in dark or high contrast is
    // undefined at runtime, which renders as transparent: invisible text,
    // and invisible only in the theme the developer was not using.
    const {light, dark, highContrast} = require('../../src/theme/palette');

    const keys = Object.keys(light).sort();
    expect(Object.keys(dark).sort()).toEqual(keys);
    expect(Object.keys(highContrast).sort()).toEqual(keys);

    for (const palette of [light, dark, highContrast]) {
      for (const key of keys) {
        expect(palette[key]).toBeDefined();
      }
    }
  });

  it('builds the high contrast palette entirely from system colours', () => {
    // Under a high contrast theme the user has chosen these colours and the
    // app must obey them. A literal here would override that choice.
    const {highContrast} = require('../../src/theme/palette');

    const literals = Object.entries(highContrast)
      .filter(([, value]) => typeof value === 'string')
      .map(([key]) => key);

    expect(literals).toEqual([]);
  });
});
