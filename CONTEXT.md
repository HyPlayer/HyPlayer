# HyPlayer Lyrics

This context describes how timed lyrics become independently targetable visual text while a line is focused.

## Language

**Word**:
A semantic lyric token supplied by timed lyrics or inferred by a language-aware word segmenter. A Word contains one or more GlyphUnits.
_Avoid_: Character, glyph

**Inferred Word**:
A Word boundary inferred for an untimed lyric line when simulated word scanning is enabled. It has allocated timing but is explicitly distinguishable from a provider-supplied Word.
_Avoid_: Synthetic glyph, fake character

**GlyphUnit**:
An indivisible shaped glyph cluster used as the smallest animated visual unit. Combining characters, emoji sequences, and ligatures remain inside one GlyphUnit.
_Avoid_: UTF-16 character, Word

**Reveal Progress**:
The normalized coverage of the highlighted contribution. It does not describe position, lift, or any other motion.
_Avoid_: Animation progress

**Motion Progress**:
The normalized progress used by spatial GlyphUnit effects such as lift. It is independent from Reveal Progress.
_Avoid_: Highlight progress

**Target State**:
One independently selectable visual contribution of a lyric text layer: highlighted, current highlighted, current pending, unhighlighted, or translation.
_Avoid_: Text style
