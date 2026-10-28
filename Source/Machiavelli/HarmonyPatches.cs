using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

// The trait sits on the same emotion seams the Sociopath mod uses: memory intake,
// break choice, break targeting and the social interaction pipeline.

namespace Machiavelli
{
    // Memory intake. Whenever someone is rescued, recruited or treated kindly by a Machiavellian,
    // the Machiavellian files it away as a debt.
    //
    // A debt is only real if the kindness actually registered. A memory can be taken on by being
    // added, or by merging into an existing one, which discards the offered object. It can also be
    // refused outright, either by the base game or by another mod cancelling the call, and a
    // postfix still runs in that case. All three have to be told apart, or a Machiavellian ends up
    // holding favours over people who were never shown any.
    [HarmonyPatch(typeof(MemoryThoughtHandler), nameof(MemoryThoughtHandler.TryGainMemory),
        new[] { typeof(Thought_Memory), typeof(Pawn) })]
    public static class Patch_TryGainMemory
    {
        /// <summary>False when an earlier prefix cancelled the call, because then this never runs.</summary>
        public static void Prefix(out bool __state)
        {
            __state = true;
        }

        public static void Postfix(MemoryThoughtHandler __instance, Thought_Memory newThought,
            Pawn otherPawn, bool __state)
        {
            if (!__state || newThought?.def == null || !MAC.FavourThoughts.Contains(newThought.def))
            {
                return;
            }
            Pawn benefactor = otherPawn ?? newThought.otherPawn;
            if (!MAC.IsMachiavellian(benefactor))
            {
                return;
            }
            if (!MAC.WasAccepted(__instance, newThought))
            {
                return;
            }
            GameComponent_Machiavelli.Instance?.AddFavour(benefactor, __instance.pawn);
        }
    }

    /// <summary>
    /// Watches for a memory being merged into an existing one rather than added. That path
    /// discards the offered object, so it cannot be detected by inspecting the list afterwards.
    /// </summary>
    [HarmonyPatch(typeof(Thought_Memory), nameof(Thought_Memory.TryMergeWithExistingMemory))]
    public static class Patch_TryMergeWithExistingMemory
    {
        public static void Postfix(Thought_Memory __instance, bool __result)
        {
            if (__result && __instance?.def != null && MAC.FavourThoughts.Contains(__instance.def))
            {
                MAC.NoteMerged(__instance);
            }
        }
    }

    // Cashing in. Someone who owes a Machiavellian cannot bring themselves to insult them.
    // The favour is spent doing that work, and the interaction never happens.
    [HarmonyPatch(typeof(Pawn_InteractionsTracker), nameof(Pawn_InteractionsTracker.TryInteractWith))]
    public static class Patch_TryInteractWith
    {
        public static bool Prefix(Pawn ___pawn, Pawn recipient, InteractionDef intDef, ref bool __result)
        {
            if (!Tuning.FavourCashIn || intDef == null || !MAC.IsMachiavellian(recipient))
            {
                return true;
            }
            if (intDef != InteractionDefOf.Insult && intDef.defName != "Slight")
            {
                return true;
            }
            GameComponent_Machiavelli ledger = GameComponent_Machiavelli.Instance;
            if (ledger == null || !ledger.Owes(___pawn, recipient))
            {
                return true;
            }
            ledger.Spend(___pawn, recipient);
            if (PawnUtility.ShouldSendNotificationAbout(recipient))
            {
                Messages.Message(
                    $"{___pawn.LabelShort} thought better of insulting {recipient.LabelShort}. That favour is spent.",
                    recipient, MessageTypeDefOf.SilentInput);
            }
            __result = false;
            return false;
        }
    }

    // Divide and rule. Two other colonists falling out is quietly good news.
    [HarmonyPatch(typeof(MentalStateHandler), nameof(MentalStateHandler.TryStartMentalState))]
    public static class Patch_TryStartMentalState
    {
        public static void Postfix(MentalStateHandler __instance, bool __result, MentalStateDef stateDef)
        {
            if (!__result || !Tuning.DivideAndRule || stateDef != MentalStateDefOf.SocialFighting)
            {
                return;
            }
            if (!(__instance.CurState is MentalState_SocialFighting fight))
            {
                return;
            }
            Pawn one = fight.pawn;
            Pawn two = fight.otherPawn;
            if (one == null || two == null || one.Map == null)
            {
                return;
            }
            // A social fight starts a state on both sides. Only act on one of them, or it counts twice.
            if (one.thingIDNumber > two.thingIDNumber)
            {
                return;
            }
            System.Collections.Generic.IReadOnlyList<Pawn> colonists = one.Map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn schemer = colonists[i];
                if (schemer == one || schemer == two || !MAC.IsMachiavellian(schemer) || schemer.needs?.mood == null)
                {
                    continue;
                }
                schemer.needs.mood.thoughts.memories.TryGainMemory(MAC_DefOf.MAC_Squabble);
            }
        }
    }

    // Which break is chosen. A Machiavellian does not rampage; when they go, they go after
    // whoever they had already written off.
    [HarmonyPatch(typeof(MentalBreakWorker), nameof(MentalBreakWorker.CommonalityFor))]
    public static class Patch_CommonalityFor
    {
        public static void Postfix(MentalBreakWorker __instance, Pawn pawn, ref float __result)
        {
            if (__result <= 0f || !MAC.IsMachiavellian(pawn))
            {
                return;
            }
            string name = __instance.def.defName;
            if (name == "Berserk" || name == "MurderousRage" || name == "RunWild")
            {
                __result *= Tuning.RampageFactor;
                return;
            }
            if (name == "TargetedInsultingSpree" && MAC.FindDeadWeightTarget(pawn, requireReachable: false) != null)
            {
                __result *= Tuning.TargetedInsultFactor;
            }
        }
    }

    // Break targeting: aim the spree at the colonist they had written off, not a random one.
    [HarmonyPatch(typeof(MentalState_TargetedInsultingSpree), nameof(MentalState_TargetedInsultingSpree.PreStart))]
    public static class Patch_TargetedInsultingSpree_PreStart
    {
        private static readonly System.Collections.Generic.List<Pawn> candidates =
            new System.Collections.Generic.List<Pawn>();

        public static void Postfix(MentalState_TargetedInsultingSpree __instance)
        {
            Pawn pawn = __instance.pawn;
            if (!MAC.IsMachiavellian(pawn))
            {
                return;
            }
            Pawn target = MAC.FindDeadWeightTarget(pawn, requireReachable: true);
            if (target == null)
            {
                return;
            }
            InsultingSpreeMentalStateUtility.GetInsultCandidatesFor(pawn, candidates, allowPrisoners: false);
            if (candidates.Contains(target))
            {
                __instance.target = target;
            }
            candidates.Clear();
        }
    }
}
