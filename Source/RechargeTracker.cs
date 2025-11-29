using System.Collections.Generic;
using RimWorld;
using Verse;
using HarmonyLib;
using System.Linq;

namespace MechShieldReminder
{
    /// <summary>
    /// Static tracker that periodically checks mechanoid/projectile interceptor shields on all maps,
    /// and notifies the player when a shield goes down (charging or cooldown) or comes back up.
    /// This class patches TickManager.DoSingleTick to run periodic checks.
    /// </summary>
    [StaticConstructorOnStartup]
    public class RechargeTracker
    {
        #region config

        /// <summary>
        /// Number of ticks between shield checks (default: 250).
        /// </summary>
        private static readonly int _mechShieldCheckIdleTime = 250;
        
        /// <summary>
        /// Number of ticks between cleaning the notified-shields hash (default: 10000).
        /// </summary>
        private static readonly int _cleanHashIdleTime = 10000;
        
        /// <summary>
        /// Harmony package ID for patching. Use your mod's unique identifier.
        /// </summary>
        private static readonly string _patchPackageName = "maxxsom.mechshieldreminder";
        
        #endregion

        #region class variables
        /// <summary>
        /// Whether shield tasks should run. Set to false during static initialization if no shield defs found.
        /// </summary>
        private static readonly bool _runShieldTasks = true;
        
        /// <summary>
        /// Tick counter used to determine when to run shield checks.
        /// </summary>
        private static int _tickCheckShieldsIdleCount = _mechShieldCheckIdleTime;
        
        /// <summary>
        /// Tick counter used to determine when to run hash cleanup.
        /// </summary>
        private static int _tickCleanHashIdleCount = _cleanHashIdleTime;
        
        /// <summary>
        /// True while the tracker is performing its first check; used to suppress initial notification spam.
        /// </summary>
        private static bool _isFirstCheck = true;
        
        /// <summary>
        /// Set of thingIDNumbers representing shields that have been notified as "down".
        /// Storing IDs (int) avoids retaining Thing references and helps garbage collection.
        /// </summary>
        private static readonly HashSet<int> _shieldsDownNotified = new();
        
        /// <summary>
        /// Cached list of ThingDef objects that represent shielded things (have CompProjectileInterceptor).
        /// </summary>
        private static readonly List<ThingDef> _shieldDefList;
        
        /// <summary>
        /// Harmony instance used for patching. Stored so patches can be undone if needed.
        /// </summary>
        private static readonly Harmony _harmony = new(_patchPackageName);

        #endregion

        #region initialization
        /// <summary>
        /// Static constructor: locates shield defs and installs Harmony patch.
        /// </summary>
        static RechargeTracker() {
            Patch();

            // Find Defs of Listed Shields
            _shieldDefList = DefDatabase<ThingDef>.AllDefs
                .Where(td => td.HasComp<CompProjectileInterceptor>())
                #pragma warning disable IDE0305
                .ToList(); 
                #pragma warning restore IDE0305 
            
            if (_shieldDefList.Count == 0) {
                Log.Error($"Could not find any defs for shields! Disabling shield checks :(");
                _runShieldTasks = false;
            }
        }

        /// <summary>
        /// Attempt to patch TickManager.DoSingleTick with a postfix that calls PostTickManagerTick.
        /// Keeps a reference to Harmony instance and logs any errors.
        /// </summary>        
        private static void Patch() {
            var original = AccessTools.Method(typeof(TickManager), 
                nameof(TickManager.DoSingleTick));
            var postFix = AccessTools.Method(typeof(RechargeTracker), 
                nameof(RechargeTracker.PostTickManagerTick));

            if (original == null || postFix == null) {
                Log.Error($"Could not access original ({original}) or postFix ({postFix}) method!");
            } else {
                _harmony.Patch(original, null, new HarmonyMethod(postFix));
            }
        }

        #endregion

        /// <summary>
        /// Postfix method invoked every TickManager.DoSingleTick call (once per game tick).
        /// Responsible for incrementing internal counters and running periodic tasks.
        /// </summary>
        public static void PostTickManagerTick()
        {
            if (!_runShieldTasks) {
                return;
            }

            // Increment Idle Counters
            _tickCheckShieldsIdleCount++;
            _tickCleanHashIdleCount++;

            // Run Periodic CleanShieldHash task
            if (_tickCleanHashIdleCount >= _cleanHashIdleTime) {
                _tickCleanHashIdleCount = 0;
                CleanShieldHash();
            }

            // Run Periodic CheckMechShields task
            if (_tickCheckShieldsIdleCount >= _mechShieldCheckIdleTime) {
                _tickCheckShieldsIdleCount = 0;
                CheckMechShields();
            }
            
        }

        /// <summary>
        /// Finds all shield Things across all loaded maps and checks each shield for charging/cooldown.
        /// If a shield is down and not previously notified, sends a one-time letter. When the shield comes
        /// back up and was previously notified, sends a "back up" letter and clears the notified state.
        /// </summary>
        private static void CheckMechShields()
        {
            var shieldsList = FindAllShields();

            foreach (var shieldThing in shieldsList) {
                // Checks if the shield is down for charging or on cooldown
                bool isDown = IsShieldDown(shieldThing);
                int thingId = shieldThing.thingIDNumber;

                // Determines the user needs to be notified on the status of the shield
                if (isDown && !_shieldsDownNotified.Contains(thingId)) {
                    // Notify once per down-event
                    if (!_isFirstCheck) {
                        Find.LetterStack.ReceiveLetter(
                        $"{shieldThing.LabelCap} is down",
                        $"The {shieldThing.Label.ToLower()} is down to recharge. This would be the best time to strike!",
                        LetterDefOf.NeutralEvent,
                        new LookTargets(shieldThing) 
                        );
                    }
                    
                    _shieldsDownNotified.Add(thingId);
                } else if (!isDown && _shieldsDownNotified.Contains(thingId)) {
                    // Shield back up — remove from notified set so future recharges will notify again
                    if (!_isFirstCheck) {
                        Find.LetterStack.ReceiveLetter(
                            $"{shieldThing.LabelCap} is back up",
                            $"The {shieldThing.Label.ToLower()} is back up. You might want to wait until the next recharge time...",
                            LetterDefOf.NeutralEvent,
                            new LookTargets(shieldThing) 
                        );
                    }
                    
                    _shieldsDownNotified.Remove(thingId);
                }
            }

            _isFirstCheck = false;
        }

        /// <summary>
        /// Removes thingIDs from the notified set that are no longer present in any loaded map.
        /// This prevents the set from growing indefinitely when Things are destroyed.
        /// </summary>
        private static void CleanShieldHash()
        {
            Log.Message("Starting Shield Check Cleanup...");

            var shieldsList = FindAllShields();

            // Create a list of old shields still in the hash and remove them
            var oldShields = _shieldsDownNotified
                .Except(shieldsList.Select(t => t.thingIDNumber))
                .ToList();
            foreach(var shieldThing in oldShields) {
                _shieldsDownNotified.Remove(shieldThing);
            }

            Log.Message($"Removed {oldShields.Count()} old ShieldThings from HashList");
        }

        /// <summary>
        /// Returns a list of all active shield Things across all loaded maps, based on cached shield defs.
        /// </summary>
        /// <returns>List of Thing instances that match any shield ThingDef.</returns>
        private static List<Thing> FindAllShields()
        {
            // Generate a list of all active shields
            return Find.Maps
                .SelectMany(map => _shieldDefList.SelectMany(def => map.listerThings.ThingsOfDef(def)))
                #pragma warning disable IDE0305
                .ToList();
                #pragma warning restore IDE0305
        }

        /// <summary>
        /// Returns true if the given Thing (shield) is currently down — either Charging or OnCooldown.
        /// </summary>
        /// <param name="shieldThing">Thing to inspect for a CompProjectileInterceptor.</param>
        /// <returns>True if the shield is currently down (charging or on cooldown); otherwise false.</returns>
        private static bool IsShieldDown(Thing shieldThing)
        {
            var comp = shieldThing.TryGetComp<CompProjectileInterceptor>();
            if (comp != null) {
                if (comp.Charging || comp.OnCooldown) {
                    return true;
                }
            }
            return false;
        }
    }
}
