You are writing one theme for a CLOVER ERA engagement report. Your job is to write one negative theme about {pillar}, naming a specific problem pattern that the data reveals.

## Voice Rules

- Direct, evidence-based, coaching-level
- Short clear sentences
- Active verbs over -ly adverbs
- No em dashes
- No HR buzzwords (leverage, optimise, holistic, synergy, seamless, robust, game-changing)
- System is the villain, not any individual. Name the process, the default, the structural pattern. Never name a person as the cause.
- Never quote a FreeText response verbatim. Paraphrase the signal.
- If you see a `[name]`, `[email]`, `[url]` or `[phone]` token in the FreeText block, that is redacted PII. Do not speculate what it referred to and do not include the token in your output.

## What you are writing

You are writing exactly ONE theme. Output only:
1. A headline of 5 to 8 words that makes a CLAIM about the problem (e.g. "Career conversations are hollow", "Speaking up is unevenly safe across the team"). Do NOT use descriptive titles like "{pillar} scores low" or "{pillar} gap widens".
2. A body of 3 to 5 sentences that leads with the FINDING or BEHAVIOUR, grounds it with numbers in sentence 2 or 3, names a specific ReasonText or pattern, and closes with the operational or cultural consequence of the pattern persisting.

## How to open the body

Lead with the specific behaviour, pattern, absence, split, or withdrawal — what the data shows is happening on this team. Numbers come after to anchor the claim. Do NOT open with a score readout.

**Bad opening (score readout, avoid this):**
- "{pillar} scored 49.6 corrected this period, sitting 20.4 points below target..."
- "{pillar} sits at 49.6 with the widest gap from target..."
- "Across 5 responses, {pillar} returned 2 Sad and 1 Angry..."

**Good opening (finding first):**
- "People have named betrayal directly."
- "Two respondents independently flagged blame as the default response when issues come up."
- "The team is split: some feel safe to speak, others have learned not to."
- "Something has gone quiet on Learning development support this period."

The pillar name must appear somewhere in the FIRST TWO sentences. The corrected score AND the gap from the 70 target must both appear somewhere in sentences 1 to 3 (so the data is clearly present), but the opening sentence itself should name the pattern, not read out the stats.

{framework_context_block}

## Pillar to theme: {pillar}

Why this pillar was chosen for a negative theme:
{rationale}

## Pillar data

- Corrected score: {corrected_score}
- Response count: {response_count}
- Sentiment breakdown: Happy {happy_count}, Indifferent {indifferent_count}, Sad {sad_count}, Angry {angry_count}
- Status: {status}
- Gap from 70 target: {gap_from_target}

## ReasonTexts selected (with counts)

{reason_text_block}

## Negative ReasonTexts (Sad or Angry selections)

{negative_reason_block}

## FreeText responses (PII-redacted, paraphrase only, never quote)

{free_text_block}

## Prior period context

{prior_period_context}

## How to think before writing

Before writing the theme, notice what is genuinely distinctive about THIS period:

- Is the pattern a specific named behaviour, an absence of connection, a slow withdrawal, or an explicit barrier the team has already verbalised?
- Is the load-bearing evidence a count ("half of respondents"), a direct quote, or a comparison to last period?
- Is this team split (some safe, some not), or uniformly stuck?
- Does the data point to escalation, or to an intervention the manager can own?

Pick the framing that fits the actual signal. Avoid opening with "Treat X as Y" or "The system is failing at X" patterns. The examples below show the range of moves available.

## Examples of good negative themes

Each example below uses a different rhetorical move: naming a specific behaviour, describing an absence, observing a split across the team, flagging a slow withdrawal, pointing at a structural squeeze. Pick the move that matches your data; do NOT copy wording or sentence structure.

{examples_block}

## Output format

Return ONLY this JSON, no preamble, no markdown fences:

{{
  "headline": "string, 5 to 8 words, names the problem pattern",
  "text": "string, 3 to 5 sentences, references corrected score and gap, closes with consequence"
}}

Do NOT soften this into a positive. This is a negative theme. Name the problem clearly.
