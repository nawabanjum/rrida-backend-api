namespace CloverBackoffice.Infrastructure.Models
{
    /// <summary>
    /// Scoping for the shared DocumentEmbeddings table (its "EmbeddingType" column).
    ///
    /// Important: EmbeddingType is NOT a clean namespace across the whole table. The document
    /// upload flow lets the caller supply any value (a document category), and rows created
    /// before it existed are NULL. So the main knowledge base is defined by exclusion —
    /// "everything that does not belong to a dedicated corpus" — while each dedicated corpus
    /// (e.g. the ERAN Coach) owns one reserved value and is matched positively.
    /// </summary>
    public static class EmbeddingTypes
    {
        /// <summary>
        /// Scope sentinel for the main knowledge base: uploaded documents, report generation,
        /// worksheet prediction and the other LLM features. Not a value that gets stored —
        /// it selects every row except those owned by a dedicated corpus.
        /// </summary>
        public const string CloverKnowledgeBase = "clover_kb";

        /// <summary>
        /// ERAN Coach corpus (CLOVER worksheets, micro-actions, book chunks). A reserved,
        /// stored value. Read only by the coach's retrieval tools.
        /// </summary>
        public const string EranCoachCorpus = "eran_coach_corpus";

        /// <summary>
        /// Corpora that are isolated from the main knowledge base. Add a reserved value here
        /// when introducing another corpus and every existing search excludes it automatically.
        /// </summary>
        public static readonly string[] IsolatedCorpora = { EranCoachCorpus };
    }
}
