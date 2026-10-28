using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Machiavelli
{
    /// <summary>
    /// Confide. Shares something personal, true or invented, and the other person feels trusted.
    /// Both degrees use it. For a Machiavellian it also puts the listener quietly in their debt.
    /// </summary>
    public class InteractionWorker_Confide : InteractionWorker
    {
        public override float RandomSelectionWeight(Pawn initiator, Pawn recipient)
        {
            if (!MAC.IsCalculating(initiator))
            {
                return 0f;
            }
            if (recipient?.RaceProps == null || !recipient.RaceProps.Humanlike ||
                recipient.DevelopmentalStage.Juvenile())
            {
                return 0f;
            }
            // No point confiding in someone who already despises them.
            if (MAC.Opinion(initiator, recipient) <= -20)
            {
                return 0f;
            }
            return Tuning.ConfideWeight;
        }

        public override void Interacted(Pawn initiator, Pawn recipient, List<RulePackDef> extraSentencePacks,
            out string letterText, out string letterLabel, out LetterDef letterDef, out LookTargets lookTargets)
        {
            letterText = null;
            letterLabel = null;
            letterDef = null;
            lookTargets = null;
            GameComponent_Machiavelli.Instance?.AddFavour(initiator, recipient);
        }
    }

    /// <summary>Mood from holding favours over people.</summary>
    public class ThoughtWorker_FavoursOwed : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            GameComponent_Machiavelli ledger = GameComponent_Machiavelli.Instance;
            if (ledger == null || !MAC.IsMachiavellian(p))
            {
                return ThoughtState.Inactive;
            }
            int count = ledger.FavourCount(p);
            if (count >= 5)
            {
                return ThoughtState.ActiveAtStage(2);
            }
            if (count >= 3)
            {
                return ThoughtState.ActiveAtStage(1);
            }
            if (count >= 1)
            {
                return ThoughtState.ActiveAtStage(0);
            }
            return ThoughtState.Inactive;
        }
    }

    /// <summary>
    /// Standing. How well the Machiavellian believes they are regarded: what the colony thinks of them,
    /// plus credit for every favour outstanding.
    /// </summary>
    public class ThoughtWorker_Standing : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!Tuning.Standing || !MAC.IsMachiavellian(p) || p.Map == null || !p.Spawned)
            {
                return ThoughtState.Inactive;
            }
            IReadOnlyList<Pawn> colonists = p.Map.mapPawns.FreeColonistsSpawned;
            int total = 0;
            int peers = 0;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn other = colonists[i];
                if (!MAC.IsPeer(p, other) || other.relations == null)
                {
                    continue;
                }
                total += other.relations.OpinionOf(p);
                peers++;
            }
            // Alone or nearly alone, there is no audience to be overlooked by.
            if (peers < 3)
            {
                return ThoughtState.Inactive;
            }
            GameComponent_Machiavelli ledger = GameComponent_Machiavelli.Instance;
            int favours = ledger == null ? 0 : ledger.FavourCount(p);
            int score = (total / peers) + (favours * Tuning.StandingPerFavour);

            if (score >= Tuning.StandingHigh)
            {
                return ThoughtState.ActiveAtStage(0);
            }
            if (score <= Tuning.StandingLow)
            {
                return ThoughtState.ActiveAtStage(2);
            }
            if (score < Tuning.StandingHigh / 3)
            {
                return ThoughtState.ActiveAtStage(1);
            }
            return ThoughtState.Inactive;
        }
    }

    /// <summary>Worth keeping close: the Machiavellian courts whoever carries the colony.</summary>
    public class ThoughtWorker_Useful : ThoughtWorker
    {
        protected override ThoughtState CurrentSocialStateInternal(Pawn p, Pawn other)
        {
            if (!MAC.IsMachiavellian(p))
            {
                return ThoughtState.Inactive;
            }
            return GameComponent_Machiavelli.IsUsefulTo(p, other) ? ThoughtState.ActiveAtStage(0) : ThoughtState.Inactive;
        }
    }

    /// <summary>Dead weight: whoever the Machiavellian has decided is costing more than they bring.</summary>
    public class ThoughtWorker_DeadWeight : ThoughtWorker
    {
        protected override ThoughtState CurrentSocialStateInternal(Pawn p, Pawn other)
        {
            if (!MAC.IsMachiavellian(p))
            {
                return ThoughtState.Inactive;
            }
            return GameComponent_Machiavelli.IsDeadWeightTo(p, other) ? ThoughtState.ActiveAtStage(0) : ThoughtState.Inactive;
        }
    }
}
