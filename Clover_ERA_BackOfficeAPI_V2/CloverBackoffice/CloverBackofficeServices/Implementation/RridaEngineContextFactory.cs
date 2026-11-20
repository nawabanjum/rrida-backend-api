using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Rrida.Engine;
using Rrida.State;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// Builds the deterministic layer's two injected roots once per process: the engine
    /// context (the kit's data bank, rotation and engine config) and the state config (the
    /// resolver's thresholds).
    ///
    /// Both are immutable and expensive enough to load that doing it per request would be
    /// waste, so this is registered as a singleton. The RESOLVER is not: it carries per-team
    /// running state and belongs to one run. See VENDORED.md and the developer guide.
    ///
    /// Nothing here carries a default threshold. Every number comes from the kit's
    /// thresholds.json, which is provisional and is never retyped into our own config.
    /// </summary>
    public sealed class RridaEngineContextFactory : IRridaEngineContextFactory
    {
        // Where the kit lands in the build output. The API csproj copies
        // ..\external\RRIDA-Engine\kit\data to this folder; the setting exists so a
        // deployment can point somewhere else without a rebuild.
        private const string DefaultKitPath = "CloverEra/Data/rrida-kit";

        // D34. The production rotation, upstream data/rotation-90/rotation.json, copied by the API
        // csproj from ..\external\RRIDA-Engine\rotations. Every team is asked on it. The kit's own
        // rotation/individual.json is the demo's six (upstream data/rotation-demo-6day.json) and is
        // never used to ask a team.
        private const string DefaultProductionRotationPath = "CloverEra/Data/rrida-rotations/rotation-90.json";

        private readonly Lazy<EngineContext> _context;
        private readonly Lazy<StateConfig> _stateConfig;
        private readonly Lazy<CalendarConfig> _calendar;
        private readonly ILogger<RridaEngineContextFactory> _logger;

        public RridaEngineContextFactory(IConfiguration configuration, ILogger<RridaEngineContextFactory> logger)
        {
            _logger = logger;

            var configured = configuration["Rrida:KitDataPath"];
            KitDataPath = Path.IsPathRooted(configured ?? string.Empty)
                ? configured!
                : Path.Combine(AppContext.BaseDirectory, configured ?? DefaultKitPath);

            // Lazy so that a missing kit fails on first use with a clear message rather
            // than taking the whole application down at startup, and so the cost is paid
            // only by a process that actually runs the engine.
            var rotation = configuration["Rrida:ProductionRotationPath"];
            ProductionRotationPath = Path.IsPathRooted(rotation ?? string.Empty)
                ? rotation!
                : Path.Combine(AppContext.BaseDirectory, rotation ?? DefaultProductionRotationPath);

            _context = new Lazy<EngineContext>(BuildContext, isThreadSafe: true);
            _stateConfig = new Lazy<StateConfig>(BuildStateConfig, isThreadSafe: true);
            _calendar = new Lazy<CalendarConfig>(BuildCalendar, isThreadSafe: true);
        }

        public string KitDataPath { get; }

        /// <summary>Where the production (ninety-question) rotation was loaded from.</summary>
        public string ProductionRotationPath { get; }

        public EngineContext Context => _context.Value;

        public StateConfig StateConfig => _stateConfig.Value;

        public CalendarConfig Calendar => _calendar.Value;

        /// <summary>
        /// D34. The product: all ninety individual questions and all ninety team questions, one of
        /// each per asked day, looping. Built the way the reference's buildContext is with
        /// ROTATIONS.production: the kit's data, calendar, config and tracks, with the ninety-question
        /// individual rotation named. FromKit always loads the kit's six-day file, so the context is
        /// assembled here rather than by editing the vendored engine.
        /// </summary>
        private EngineContext BuildContext()
        {
            EnsureKitPresent();

            // Delivery tracks are left at the engine's own default, which is both tracks
            // (D32, the reference's DELIVERY_TRACKS): the team question votes as well as the
            // individual one. The external-locus route it waited on is D31 (a cause outside
            // the team votes in the outward bucket, which nothing selects from), and spec 7.1b
            // is settled as shared tallies with no extra tie-break.
            var kit = EngineContext.FromKit(KitDataPath);
            if (!File.Exists(ProductionRotationPath))
            {
                throw new InvalidOperationException(
                    $"The production rotation was not found at '{ProductionRotationPath}'. It is vendored at "
                    + "external/RRIDA-Engine/rotations/rotation-90.json and copied to the build output by the "
                    + "API project. Set Rrida:ProductionRotationPath to point elsewhere.");
            }

            var ctx = new EngineContext
            {
                Data = kit.Data,
                Rotation = new Rotation(
                    kit.Data,
                    ProductionRotationPath,
                    Path.Combine(KitDataPath, "rotation", "team.json"),
                    Path.Combine(KitDataPath, "schedule.json")),
                Calendar = kit.Calendar,
                Config = kit.Config,
                DeliveryTracks = kit.DeliveryTracks,
            };

            _logger.LogInformation(
                "RRIDA kit loaded from {Path}, rotation {Rotation} ({Length} asked days, looping). Tracks asked: {Asked}. Delivering: {Delivering}.",
                KitDataPath, ProductionRotationPath, ctx.Rotation.CycleLength,
                string.Join(", ", ctx.Rotation.TracksAsked), string.Join(", ", ctx.DeliveryTracks));

            return ctx;
        }

        private StateConfig BuildStateConfig()
        {
            EnsureKitPresent();
            return StateConfig.Load(Path.Combine(KitDataPath, "thresholds.json"));
        }

        private CalendarConfig BuildCalendar()
        {
            EnsureKitPresent();
            return CalendarConfig.Load(Path.Combine(KitDataPath, "calendar.json"));
        }

        private void EnsureKitPresent()
        {
            var thresholds = Path.Combine(KitDataPath, "thresholds.json");
            if (!File.Exists(thresholds))
            {
                throw new InvalidOperationException(
                    $"The RRIDA kit was not found at '{KitDataPath}'. It is vendored at "
                    + "external/RRIDA-Engine/kit/data and copied to the build output by the API "
                    + "project. Set Rrida:KitDataPath to point elsewhere.");
            }
        }
    }
}
