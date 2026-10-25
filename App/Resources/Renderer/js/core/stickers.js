// When each emoji sticker for a line should appear.

const UNPLACED_OBJECT_DELAY = 0.4;
/** The mood emoji is a quick accent, not a fixture: it leaves after this long. */
const MOOD_LIFETIME = 2.2;
const CONFETTI_COUNT = 3;
const CONFETTI_STAGGER = 0.12;

/**
 * @param {{emoji?: string|null, burst?: boolean,
 *          objects?: {word: string, emoji?: string|null, wordIndex?: number|null}[]}|null} insight
 * @param {{s: number, w: {s: number}[]}} line
 * @returns {{key: string, role: 'emotion'|'confetti'|'object', emoji: string, word?: string,
 *            wordIndex?: number|null, appearAt: number, lifetime?: number}[]}
 */
export function stickerCues(insight, line) {
  if (!insight) return [];
  const firstWord = line.w[0]?.s ?? line.s;
  const cues = [];
  if (insight.emoji) {
    cues.push({ key: 'emotion', role: 'emotion', emoji: insight.emoji, appearAt: firstWord, lifetime: MOOD_LIFETIME });
    if (insight.burst) {
      for (let i = 0; i < CONFETTI_COUNT; i++) {
        cues.push({ key: `confetti:${i}`, role: 'confetti', emoji: insight.emoji, appearAt: firstWord + i * CONFETTI_STAGGER });
      }
    }
  }
  (insight.objects ?? []).forEach((object) => {
    if (!object.emoji) return;
    const word = Number.isInteger(object.wordIndex) ? line.w[object.wordIndex] : null;
    cues.push({
      key: `object:${object.word}`,
      role: 'object',
      emoji: object.emoji,
      word: object.word,
      wordIndex: word ? object.wordIndex : null,
      appearAt: word ? word.s : line.s + UNPLACED_OBJECT_DELAY,
    });
  });
  return cues;
}
