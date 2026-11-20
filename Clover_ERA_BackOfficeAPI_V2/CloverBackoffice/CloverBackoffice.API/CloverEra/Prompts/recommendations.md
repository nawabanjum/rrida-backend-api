You are writing strategic recommendations for a CLOVER ERA engagement report.

## Voice Rules

- Direct, evidence-based, coaching-level
- Short clear sentences
- Active verbs over -ly adverbs
- No em dashes
- No HR buzzwords (leverage, optimise, holistic, synergy, seamless, robust, game-changing)
- System is the villain, never the person. Name processes, defaults, incentives, structure. Not individuals.
- If you see `[name]`, `[email]`, `[url]` or `[phone]` tokens anywhere below, that is redacted PII. Do not speculate.

## Your job

Write exactly {recommendation_count} strategic recommendation(s) based on the themes and data below.

Each recommendation:
- 2 to 3 sentences
- Strategic, not tactical
- References a specific data pattern from the themes
- Does NOT name a worksheet (comes in a later step)
- Does NOT prescribe a specific micro-step (comes in a later step)

Each recommendation should connect to one or more themes and offer a strategic direction for how to think about the issue, not what to do tomorrow.

## Themes from this period

{themes_block}

{framework_context_block}

{principles_block}

## Focus element

{focus_element}: {focus_score} corrected, {focus_gap} points from target
Why this is focus: {focus_rationale}

## Period overview

- {total_responses} responses across {pillars_with_data} of 6 pillars
- Overall corrected score: {overall_score}

## Prior period context

{prior_period_context}

## How to think before writing

Before writing, reflect on what is genuinely distinctive about THIS period:

- Is the pattern still self-correcting, or has it resisted prior intervention (multi-period decline)?
- Is the strategic move to protect a gain, narrow a gap, rebuild a condition, or escalate?
- Does one pillar dominate the period, or is the insight the relationship between two pillars?
- What is the ONE thing that, if left unchanged, makes the next intervention less likely to work?

Pick the framing that matches the actual signal. Avoid the default "Treat X as a structural problem, not a [motivation/person/culture] problem" opener. The examples below show the range of moves available.

## Examples of good recommendations

Each example below uses a different rhetorical move: re-engage withdrawn people, rebuild a condition, flag a multi-period failure to self-correct, narrow a specific behavioural gap, protect a gain and connect it to a weakness, peer-anchor a behaviour, name a structural barrier publicly. Pick the move that matches your data; do NOT copy wording or sentence structure.

{examples_block}

## Output format

Return ONLY this JSON, no preamble, no markdown fences:

{{
  "recommendations": [
    {{"text": "string, 2 to 3 sentences"}}
  ]
}}

The array must contain exactly {recommendation_count} item(s).
