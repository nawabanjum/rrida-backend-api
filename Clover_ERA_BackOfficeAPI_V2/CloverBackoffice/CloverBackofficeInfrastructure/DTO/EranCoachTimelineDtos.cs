using System;
using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO
{
    /// <summary>
    /// One team's Eran thread: this user's chat with Eran about the team and the engine's
    /// messages to the team's managers, in one continuous timeline, oldest first. The MVP's
    /// Eran column (public/app.js renderThread), served for the main project's front end.
    /// </summary>
    public class EranCoachTimelineDto
    {
        public uint UnitId { get; set; }

        /// <summary>
        /// The conversation new chat turns go into, so the thread stays one thread. Null until
        /// the user's first message about this team; POST chat without one starts it.
        /// </summary>
        public int? ConversationId { get; set; }

        /// <summary>Oldest first.</summary>
        public List<EranCoachTimelineItemDto> Items { get; set; } = new();

        /// <summary>Pass back as <c>before</c> for the page older than this one. Null when there is none.</summary>
        public DateTime? NextBefore { get; set; }

        /// <summary>The team's reading today. Null when the team is not on the engine.</summary>
        public EngineTeamStateDto? State { get; set; }

        /// <summary>The move in front of the manager, for the action card and its buttons. Null when nothing is running.</summary>
        public EngineActiveActionDto? ActiveAction { get; set; }

        /// <summary>
        /// The suggested questions under the thread, in the order shown: the MVP's, then ones from
        /// the team's data today. At most four; empty when the team is not on the engine.
        /// </summary>
        public List<string> Prompts { get; set; } = new();
    }

    public class EranCoachTimelineItemDto
    {
        /// <summary>"chat" or "engine".</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Where the item sits in the thread. A chat message's own time; an engine message's
        /// date, in the order the engine wrote that day's messages.
        /// </summary>
        public DateTime At { get; set; }

        // ── chat ──
        public long? MessageId { get; set; }
        public int? ConversationId { get; set; }
        /// <summary>"user" or "assistant".</summary>
        public string? Role { get; set; }
        public string? Content { get; set; }

        // ── engine ──
        public string? FeedKey { get; set; }
        /// <summary>daily, weekly, outcome, note, escalation, monthly.</summary>
        public string? Kind { get; set; }
        /// <summary>The MVP's label in the thread; none for a daily message.</summary>
        public string? Label { get; set; }
        /// <summary>The engine's day number: day 1 is the team's start date.</summary>
        public int? Day { get; set; }
        public DateOnly? Date { get; set; }
        /// <summary>For a daily message: calibrating, action_first_day, action_reminder, action_confirmed, nothing_cleared, between_cycles.</summary>
        public string? Situation { get; set; }
        public string? CycleKey { get; set; }
        public int? Rung { get; set; }
        /// <summary>For a team note: Change, Learned or Null.</summary>
        public string? NoteType { get; set; }
        /// <summary>"ready"; "pending" while Eran has not yet written it; "failed" once Eran has given up on it.</summary>
        public string? Status { get; set; }
        /// <summary>Eran's wording, for every kind except daily.</summary>
        public string? Text { get; set; }
        /// <summary>Eran's wording of a daily message, in its six parts.</summary>
        public EranCoachDailyMessageDto? Daily { get; set; }
    }

    public class EranCoachDailyMessageDto
    {
        public string Headline { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;

        /// <summary>
        /// D36 (MVP c36935d): the condition whose cause the team places outside the team, and the
        /// move to raise it. Shown as its own block under the body, with no button. Empty when
        /// there is none, and on a message written before the field existed.
        /// </summary>
        public string Outward { get; set; } = string.Empty;

        public string OneThing { get; set; } = string.Empty;
        public string SayThis { get; set; } = string.Empty;
        public string WhatTellsYou { get; set; } = string.Empty;
    }
}
