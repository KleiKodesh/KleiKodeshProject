# Redirect audit spec — find redirects that SHOULD NOT be there

You are auditing spelling-redirect entries already committed to a Hebrew/Aramaic Talmudic-study dictionary. Each redirect makes typing a `variant` jump to a `TARGET` entry (whose definition is shown as `[def: ...]`). Your job: **flag redirects where the variant is NOT actually the same word as the TARGET.** This is an adversarial correctness pass — assume some bad ones slipped in.

## Input format
```
TARGET: בהמה   [def: בעל חיים ...]
  variants: בבהמה  הבהמות  בהמתו  ...
```
The variant should be an inflection/spelling (Hebrew OR Aramaic) of the SAME WORD as TARGET, whose meaning matches `[def]`.

## FLAG a variant when (these are BAD, should be removed):
1. **Wrong word / different meaning** — variant belongs to a different word that merely shares letters. E.g. `משא`(burden) under target meaning "which-is-not-so"; `הרים`(mountains) that is really "he raised"(רום); a variant whose real meaning contradicts `[def]`.
2. **Different-root collision** — variant is a real hiphil/piel/inflection of a DIFFERENT root than TARGET. E.g. `האשים`(accused, root אשם) under target `אש`(fire); `הבריח`(smuggled, root ברח) under target `בריח`(bolt).
3. **Garbage / merged token** — letters mashed together, triple-repeated letters, truncated fragments, or a concatenation of two words (e.g. בתוהנערה, וקרןישעי, שתקיתושותקים).
4. **Particle/pronoun** pointing at a content word it doesn't mean.

## DO NOT flag (these are CORRECT, leave them):
- Aramaic forms of the target word — ד/א prefixes, suffixes ייהו/יה/ין/נן/הו (this is a Talmud app; Aramaic is wanted). E.g. `מדוכתא`, `אבהתא`, `שמעתתייהו`.
- Normal Hebrew proclitic+inflection: ו/ה/ש/כ/ל/ב/מ prefixes, pronominal suffixes.
- ל-infinitives of the target verb (להתחנן, להמטיר — correct even if they look odd).
- Same-word homographs / cross-strata senses (one word, multiple senses).
- Defective/plene spelling variants (שרין for שריון).

## When unsure
If you cannot tell whether it's the same word, DO NOT flag it (bias toward leaving — these are already committed; only flag clear errors). Note genuine uncertainty separately.

## Output (STRICT)
Write a JSON file to the scratchpad named `audit_flags_<N>.json` (N = your slice number) containing ONLY:
```
{ "flags": [ {"target":"אש","variant":"האשים","reason":"='he accused' root אשם, not fire"}, ... ] }
```
Empty flags array if the slice is clean. Copy target/variant strings verbatim. Do NOT touch any database or git — analysis + writing that one JSON file only.

Reply with a one-line count: "slice N: flagged X of Y variants".
