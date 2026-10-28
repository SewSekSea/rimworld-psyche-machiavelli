using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Machiavelli
{
    [DefOf]
    public static class MAC_DefOf
    {
        public static TraitDef MAC_Machiavellian;
        public static ThoughtDef MAC_Confided;
        public static ThoughtDef MAC_Squabble;
        public static InteractionDef MAC_Confide;

        static MAC_DefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(MAC_DefOf));
        }
    }

    /// <summary>All the tuning numbers in one place. The ones players can change come from the settings menu.</summary>
    public static class Tuning
    {
        private static MachiavelliSettings S => MachiavelliMod.Settings;

        // How long a favour is remembered before it lapses.
        public static float FavourDays => S.favourDays;

        // How often the Confide interaction is chosen. Vanilla chit-chat is 1.00.
        public static float ConfideWeight => S.confideWeight;

        // Standing: the score above which they feel listened to, and below which they feel ignored.
        public static int StandingHigh => S.standingHigh;
        public static int StandingLow => S.standingLow;

        // Each favour held counts this much towards standing.
        public const int StandingPerFavour = 5;

        // Break skew for a Machiavellian: away from rampages, towards going after the dead weight.
        public const float RampageFactor = 0.3f;
        public const float TargetedInsultFactor = 3f;

        // Optional behaviours.
        public static bool FavourCashIn => S.favourCashIn;
        public static bool DivideAndRule => S.divideAndRule;
        public static bool RateUsefulness => S.rateUsefulness;
        public static bool Standing => S.standing;
    }

    [StaticConstructorOnStartup]
    public static class MAC
    {
        public const int DegreeCalculating = 1;
        public const int DegreeMachiavellian = 2;

        /// <summary>Services that put the recipient in the Machiavellian's debt.</summary>
        public static readonly HashSet<ThoughtDef> FavourThoughts = new HashSet<ThoughtDef>();

        /// <summary>Squad's "froze when we needed them", if that mod is installed. Written off on the spot.</summary>
        public static readonly ThoughtDef FrozeUnderFire;

        static MAC()
        {
            Fill(FavourThoughts, "RescuedMe", "RescuedMeByOfferingHelp", "RecruitedMe", "KindWords");
            FrozeUnderFire = DefDatabase<ThoughtDef>.GetNamedSilentFail("SQ_FrozeUnderFire");

            new Harmony("machiavelli.trait").PatchAll();
            Log.Message($"[Machiavelli] Loaded. Tracking {FavourThoughts.Count} favour thoughts.");
        }

        private static void Fill(HashSet<ThoughtDef> set, params string[] defNames)
        {
            foreach (string name in defNames)
            {
                ThoughtDef def = DefDatabase<ThoughtDef>.GetNamedSilentFail(name);
                if (def != null)
                {
                    set.Add(def);
                }
            }
        }

        /// <summary>0 = neither, 1 = Calculating, 2 = Machiavellian.</summary>
        public static int Degree(Pawn pawn)
        {
            Trait trait = pawn?.story?.traits?.GetTrait(MAC_DefOf.MAC_Machiavellian);
            return trait == null ? 0 : trait.Degree;
        }

        /// <summary>Either degree: confides in people, slow to brawl.</summary>
        public static bool IsCalculating(Pawn pawn)
        {
            return Degree(pawn) >= DegreeCalculating;
        }

        /// <summary>The full trait: favours, standing, usefulness, targeted breaks.</summary>
        public static bool IsMachiavellian(Pawn pawn)
        {
            return Degree(pawn) >= DegreeMachiavellian;
        }

        public static int Opinion(Pawn of, Pawn about)
        {
            if (of?.relations == null || about == null || of == about)
            {
                return 0;
            }
            return of.relations.OpinionOf(about);
        }

        /// <summary>
        /// Offers the base game merged into an existing memory rather than adding. Keyed by the
        /// offered object, so nested calls, which happen whenever one thought spawns another,
        /// cannot be mistaken for each other.
        /// </summary>
        private static readonly HashSet<Thought_Memory> mergedOffers = new HashSet<Thought_Memory>();

        public static void NoteMerged(Thought_Memory offered)
        {
            if (offered == null)
            {
                return;
            }
            // Nothing should accumulate here; each offer is claimed by the call that made it.
            if (mergedOffers.Count > 64)
            {
                mergedOffers.Clear();
            }
            mergedOffers.Add(offered);
        }

        /// <summary>Did the colonist genuinely take this memory on? Consumes the merge note.</summary>
        public static bool WasAccepted(MemoryThoughtHandler handler, Thought_Memory offered)
        {
            if (offered != null && mergedOffers.Remove(offered))
            {
                return true;
            }
            List<Thought_Memory> memories = handler?.Memories;
            return memories != null && offered != null && memories.Contains(offered);
        }

        public static bool HasMemoryAbout(Pawn pawn, ThoughtDef def, Pawn about)
        {
            if (def == null || about == null)
            {
                return false;
            }
            List<Thought_Memory> memories = pawn?.needs?.mood?.thoughts?.memories?.Memories;
            if (memories == null)
            {
                return false;
            }
            for (int i = 0; i < memories.Count; i++)
            {
                if (memories[i].def == def && memories[i].otherPawn == about)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>An adult colonist of the Machiavellian's own colony, alive and countable.</summary>
        public static bool IsPeer(Pawn schemer, Pawn other)
        {
            return other != null && other != schemer && !other.Dead &&
                   other.RaceProps != null && other.RaceProps.Humanlike &&
                   !other.DevelopmentalStage.Juvenile() &&
                   other.IsColonist && other.Faction == schemer.Faction;
        }

        /// <summary>
        /// The colonist this Machiavellian has most thoroughly written off, if any.
        /// Candidates match what a targeted insulting spree could reach: same colony, humanlike, reachable.
        /// </summary>
        public static Pawn FindDeadWeightTarget(Pawn schemer, bool requireReachable)
        {
            if (schemer == null || !schemer.Spawned || schemer.Map == null)
            {
                return null;
            }
            Pawn best = null;
            int bestOpinion = int.MaxValue;
            IReadOnlyList<Pawn> all = schemer.Map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < all.Count; i++)
            {
                Pawn other = all[i];
                if (!IsPeer(schemer, other) || !GameComponent_Machiavelli.IsDeadWeightTo(schemer, other))
                {
                    continue;
                }
                if (requireReachable && !schemer.CanReach(other, Verse.AI.PathEndMode.Touch, Danger.Deadly))
                {
                    continue;
                }
                if (other.CurJob != null && other.CurJob.exitMapOnArrival)
                {
                    continue;
                }
                int opinion = Opinion(schemer, other);
                if (opinion < bestOpinion)
                {
                    best = other;
                    bestOpinion = opinion;
                }
            }
            return best;
        }
    }
}
