using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Machiavelli
{
    /// <summary>One debt: someone the Machiavellian did a service for, and when it lapses.</summary>
    public class Favour : IExposable
    {
        public Pawn schemer;
        public Pawn debtor;
        public int expireTick;

        public Favour() { }

        public Favour(Pawn schemer, Pawn debtor, int expireTick)
        {
            this.schemer = schemer;
            this.debtor = debtor;
            this.expireTick = expireTick;
        }

        public bool Valid => schemer != null && debtor != null && !schemer.Dead && !debtor.Dead;

        public void ExposeData()
        {
            Scribe_References.Look(ref schemer, "schemer");
            Scribe_References.Look(ref debtor, "debtor");
            Scribe_Values.Look(ref expireTick, "expireTick", 0);
        }
    }

    /// <summary>
    /// The favour ledger, plus the colony's rough pecking order by what each colonist contributes.
    /// The ledger is saved; the pecking order is recalculated as the game runs.
    /// </summary>
    public class GameComponent_Machiavelli : GameComponent
    {
        private const int TickInterval = 2500;

        private List<Favour> favours = new List<Favour>();

        private static readonly HashSet<Pawn> useful = new HashSet<Pawn>();
        private static readonly HashSet<Pawn> deadWeight = new HashSet<Pawn>();
        private static readonly List<Pawn> ranked = new List<Pawn>();

        public GameComponent_Machiavelli(Game game)
        {
        }

        public static GameComponent_Machiavelli Instance => Current.Game?.GetComponent<GameComponent_Machiavelli>();

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref favours, "favours", LookMode.Deep);
            if (favours == null)
            {
                favours = new List<Favour>();
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // A debt whose people no longer exist in the save comes back with null references.
                // Drop those here rather than letting them sit in the ledger.
                favours.RemoveAll(f => f == null || f.schemer == null || f.debtor == null);
            }
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % TickInterval != 0)
            {
                return;
            }
            Prune();
            Rank();
        }

        private void Prune()
        {
            int now = Find.TickManager.TicksGame;
            for (int i = favours.Count - 1; i >= 0; i--)
            {
                Favour f = favours[i];
                if (f == null || !f.Valid || now >= f.expireTick)
                {
                    favours.RemoveAt(i);
                }
            }
        }

        // ---- the ledger -------------------------------------------------

        public void AddFavour(Pawn schemer, Pawn debtor)
        {
            if (schemer == null || debtor == null || schemer == debtor || !MAC.IsMachiavellian(schemer))
            {
                return;
            }
            if (!debtor.RaceProps.Humanlike || debtor.Dead)
            {
                return;
            }
            // Only people who are actually part of this colony's life can be held to a debt.
            // Without this, rescuing passing visitors or raiders would inflate the Machiavellian's standing.
            bool ours = debtor.IsColonist ||
                        (debtor.IsPrisoner && debtor.HostFaction == schemer.Faction);
            if (!ours)
            {
                return;
            }
            int expire = Find.TickManager.TicksGame + (int)(Tuning.FavourDays * GenDate.TicksPerDay);
            for (int i = 0; i < favours.Count; i++)
            {
                Favour f = favours[i];
                if (f != null && f.schemer == schemer && f.debtor == debtor)
                {
                    // Already owed. Doing them another service just resets the clock.
                    f.expireTick = expire;
                    return;
                }
            }
            favours.Add(new Favour(schemer, debtor, expire));
        }

        public bool Owes(Pawn debtor, Pawn schemer)
        {
            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < favours.Count; i++)
            {
                Favour f = favours[i];
                if (f != null && f.schemer == schemer && f.debtor == debtor && now < f.expireTick && f.Valid)
                {
                    return true;
                }
            }
            return false;
        }

        public void Spend(Pawn debtor, Pawn schemer)
        {
            for (int i = favours.Count - 1; i >= 0; i--)
            {
                Favour f = favours[i];
                if (f != null && f.schemer == schemer && f.debtor == debtor)
                {
                    favours.RemoveAt(i);
                    return;
                }
            }
        }

        public int FavourCount(Pawn schemer)
        {
            int now = Find.TickManager.TicksGame;
            int count = 0;
            for (int i = 0; i < favours.Count; i++)
            {
                Favour f = favours[i];
                if (f != null && f.schemer == schemer && now < f.expireTick && f.Valid)
                {
                    count++;
                }
            }
            return count;
        }

        // ---- the pecking order ------------------------------------------

        /// <summary>Total of every skill the colonist can actually use. A rough stand-in for "what they bring".</summary>
        private static int Contribution(Pawn p)
        {
            List<SkillRecord> skills = p?.skills?.skills;
            if (skills == null)
            {
                return 0;
            }
            int total = 0;
            for (int i = 0; i < skills.Count; i++)
            {
                if (!skills[i].TotallyDisabled)
                {
                    total += skills[i].Level;
                }
            }
            return total;
        }

        /// <summary>Recalculated as the game runs. Public so the development self-test can drive it directly.</summary>
        public static void Rank()
        {
            useful.Clear();
            deadWeight.Clear();
            ranked.Clear();

            List<Pawn> colonists = PawnsFinder.AllMaps_FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (p != null && !p.Dead && !p.DevelopmentalStage.Juvenile())
                {
                    ranked.Add(p);
                }
            }
            if (ranked.Count < 4)
            {
                return;
            }
            ranked.Sort((a, b) => Contribution(b).CompareTo(Contribution(a)));

            int third = ranked.Count / 3;
            if (third < 1)
            {
                third = 1;
            }
            for (int i = 0; i < third; i++)
            {
                useful.Add(ranked[i]);
            }
            // The colony has to be big enough that someone can be spare before anyone is written off.
            if (ranked.Count >= 5)
            {
                for (int i = ranked.Count - third; i < ranked.Count; i++)
                {
                    deadWeight.Add(ranked[i]);
                }
            }
        }

        public static bool IsUsefulTo(Pawn schemer, Pawn other)
        {
            return Tuning.RateUsefulness && useful.Contains(other) && MAC.IsPeer(schemer, other);
        }

        public static bool IsDeadWeightTo(Pawn schemer, Pawn other)
        {
            if (!Tuning.RateUsefulness || !MAC.IsPeer(schemer, other) || useful.Contains(other))
            {
                return false;
            }
            // Squad's "froze when we needed them" writes someone off on its own, whatever their skills.
            return deadWeight.Contains(other) || MAC.HasMemoryAbout(schemer, MAC.FrozeUnderFire, other);
        }
    }
}
