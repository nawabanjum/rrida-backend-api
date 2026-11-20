You are writing one theme for a CLOVER ERA engagement report. Your job is to write one positive theme about {pillar}, naming what is genuinely working.

## Voice Rules

- Direct, evidence-based, coaching-level
- Short clear sentences
- Active verbs over -ly adverbs
- No em dashes
- No HR buzzwords (leverage, optimise, holistic, synergy, seamless, robust, game-changing)
- Never quote a FreeText response verbatim. Paraphrase the signal.
- If you see a `[name]`, `[email]`, `[url]` or `[phone]` token in the FreeText block, that is redacted PII. Do not speculate what it referred to and do not include the token in your output.

## What you are writing

You are writing exactly ONE theme. Output only:
1. A headline of 5 to 8 words that makes a CLAIM about what is happening (e.g. "A core of the team has internalised safety", "Learning holds through practical work"). Do NOT use descriptive titles like "{pillar} scores strongly" or "Reflection climbs into healthy range".
2. A body of 3 to 5 sentences that leads with the FINDING, then supports it with numbers.

## How to open the body

Lead with what you notice — the behaviour, the pattern, the specific observation. The numbers come in sentences 2 or 3 to ground the claim. Do NOT open with a score readout.

**Bad opening (score readout, avoid this):**
- "{pillar} scored 83.2 corrected this period..."
- "{pillar} sits at 67.3 with a gap of 2.7 points..."
- "Across 5 responses, {pillar} returned 4 Happy and 1 Indifferent..."

**Good opening (finding first):**
- "One respondent described reflecting as the situation demands, which suggests the habit is becoming self-directed."
- "Three respondents independently pointed to skill practice as the core of their development."
- "A core of the team has internalised the habit visibly."
- "Something shifted in Reflection this period."

The pillar name must appear somewhere in the FIRST TWO sentences, and at least one specific number (corrected score, count, percentage, or responses) must appear somewhere in the FIRST THREE sentences. But the opening sentence itself should carry the insight, not the statistics.

{framework_context_block}

## Pillar to theme: {pillar}

Why this pillar was chosen for a positive theme:
{rationale}

## Pillar data

- Corrected score: {corrected_score}
- Response count: {response_count}
- Sentiment breakdown: Happy {happy_count}, Indifferent {indifferent_count}, Sad {sad_count}, Angry {angry_count}
- Status: {status}
- Gap from 70 target: {gap_from_target}

## ReasonTexts selected (with counts)

{reason_text_block}

## FreeText responses (PII-redacted, paraphrase only, never quote)

{free_text_block}

## Prior period context

{prior_period_context}

## How to think before writing

Before writing the theme, notice what is genuinely distinctive about THIS period:

- Is this a stable strength across periods, or a new surge?
- Is a specific respondent voice the load-bearing evidence, or is it the quantitative pattern?
- Does the positive sit next to a negative on the same question (proof it is possible)?
- Is the behaviour peer-owned now, or still leadership-driven?

Pick the framing that fits the actual signal. Avoid opening with "Treat X as Y" or "The team is doing well at X" patterns. The examples below show the range of moves available.

## Examples of good positive themes

Each example below is a different rhetorical move: a stable-strength statement, a single-voice anchor, a surge-from-prior-period, a proof-of-possibility observation. Pick the move that matches your data; do NOT copy wording or sentence structure.

{examples_block}

## Output format

Return ONLY this JSON, no preamble, no markdown fences:

{{
  "headline": "string, 5 to 8 words, names a pattern",
  "text": "string, 3 to 5 sentences, pillar name and a number in first two/three sentences"
}}

Do NOT describe a problem in this theme. This is a positive theme. If you find yourself writing about a problem, you have the wrong template.
