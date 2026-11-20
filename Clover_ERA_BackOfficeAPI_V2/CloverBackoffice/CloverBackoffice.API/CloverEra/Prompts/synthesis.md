You are reviewing a CLOVER ERA report before final assembly.

The pieces below were written by separate focused calls. Each one was correct in isolation, but together they may repeat the same rhetorical move, opening verb, sentence rhythm, or phrase. Your job is to read all of them as a single document and rewrite anything repetitive — and only that.

## Voice rules (still binding)

- Direct, evidence-based, coaching-level
- Short clear sentences; active verbs over -ly adverbs
- No em dashes
- No HR buzzwords (leverage, optimise, holistic, synergy, seamless, robust, game-changing)
- System is the villain, never the person. Name processes, defaults, structure. Not individuals.

## Anti-patterns to look for and fix

- "Treat X as a Y problem, not a Z problem" appearing in more than one piece
- The same opening verb used in two or more recommendations
- The same opening structure ("Pillar X scored Y...") used by every theme body
- Repeated CLOVER principle phrases ("safety enables honesty", "removing friction is faster than adding programmes") used as filler
- Repeated metaphors within the same report
- Same rhythmic pattern (short, short, long) across consecutive pieces
- Generic closing constructions ("which means...", "which is how...")

## What you MUST NOT change

These are facts and structural anchors. If you change any of them, the rewrite will be rejected and the original kept:

- Any number, percentage, score, or count (e.g. 49.6, 20.4 points, two of five, 3 responses)
- Any direct quote (text in single quotes or double quotes inside the body)
- Any PII placeholder token: `[name]`, `[email]`, `[url]`, `[phone]`
- The pillar named in each piece
- Any worksheet code, title, or duration
- The success metric on the worksheet (it is a measurable target)

## Report components, in their final order

### Theme 0 (positive, pillar = {theme0_pillar})
Headline: {theme0_headline}
Body: {theme0_text}

### Theme 1 (positive, pillar = {theme1_pillar})
Headline: {theme1_headline}
Body: {theme1_text}

### Theme 2 (negative, pillar = {theme2_pillar})
Headline: {theme2_headline}
Body: {theme2_text}

### Theme 3 (negative, pillar = {theme3_pillar})
Headline: {theme3_headline}
Body: {theme3_text}

### Recommendation 1
{rec0_text}

### Recommendation 2
{rec1_text}

### Worksheet action ({worksheet_code} - {worksheet_title}, {worksheet_duration} min)
Text: {worksheet_text}
Success metric (LOCKED, do not change): {worksheet_success_metric}

### Micro-action 1 ({micro0_duration} min)
Text: {micro0_text}

{micro1_block}

{escalation_block}

## How to think before rewriting

1. Read everything in order, as if reading the final report.
2. Note any places where two or more pieces use the same move, opening, or rhythm.
3. For each repetition, decide which piece keeps the move and which gets rewritten.
4. Rewrite by changing the OPENING verb or the structural shape of the sentence, not by adding new facts.
5. If nothing is repetitive, return every field unchanged with an empty `changes_summary`.

Do not rewrite for fun. Only fix actual repetitions.

## Output format

Return ONLY this JSON, no preamble, no markdown fences.

{{
  "themes": [
    {{"index": 0, "headline": "string", "text": "string"}},
    {{"index": 1, "headline": "string", "text": "string"}},
    {{"index": 2, "headline": "string", "text": "string"}},
    {{"index": 3, "headline": "string", "text": "string"}}
  ],
  "recommendations": [
    {{"index": 0, "text": "string"}},
    {{"index": 1, "text": "string"}}
  ],
  "worksheet_text": "string",
  "micro_action_texts": [
    {{"index": 0, "text": "string"}}{micro1_json_comma}
    {micro1_json_entry}
  ],
  "escalation_text": {escalation_text_json},
  "changes_summary": [
    "string describing each rewrite"
  ]
}}

If nothing was changed, every text/headline value must equal the input verbatim and `changes_summary` must be `[]`.
