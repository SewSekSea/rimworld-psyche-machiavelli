using UnityEngine;
using Verse;

namespace Machiavelli
{
    public class MachiavelliSettings : ModSettings
    {
        public float favourDays = 30f;
        public float confideWeight = 0.25f;
        public int standingHigh = 25;
        public int standingLow = -10;

        public bool favourCashIn = true;
        public bool divideAndRule = true;
        public bool rateUsefulness = true;
        public bool standing = true;

        public override void ExposeData()
        {
            MachiavelliSettings d = new MachiavelliSettings();
            Scribe_Values.Look(ref favourDays, "favourDays", d.favourDays);
            Scribe_Values.Look(ref confideWeight, "confideWeight", d.confideWeight);
            Scribe_Values.Look(ref standingHigh, "standingHigh", d.standingHigh);
            Scribe_Values.Look(ref standingLow, "standingLow", d.standingLow);
            Scribe_Values.Look(ref favourCashIn, "favourCashIn", true);
            Scribe_Values.Look(ref divideAndRule, "divideAndRule", true);
            Scribe_Values.Look(ref rateUsefulness, "rateUsefulness", true);
            Scribe_Values.Look(ref standing, "standing", true);
        }

        public void Reset()
        {
            MachiavelliSettings d = new MachiavelliSettings();
            favourDays = d.favourDays;
            confideWeight = d.confideWeight;
            standingHigh = d.standingHigh;
            standingLow = d.standingLow;
            favourCashIn = divideAndRule = rateUsefulness = standing = true;
        }
    }

    public class MachiavelliMod : Mod
    {
        public static MachiavelliSettings Settings = new MachiavelliSettings();

        public MachiavelliMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<MachiavelliSettings>();
        }

        public override string SettingsCategory() => "Machiavelli";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            MachiavelliSettings s = Settings;
            Listing_Standard list = new Listing_Standard { ColumnWidth = (inRect.width - 34f) / 2f };
            list.Begin(inRect);

            list.Label("Favours and standing");
            s.favourDays = list.SliderLabeled($"A favour lasts {s.favourDays:0} days",
                s.favourDays, 5f, 90f, 0.6f,
                "How long the Machiavellian holds a service over someone before the debt lapses.");
            s.confideWeight = list.SliderLabeled($"Confide frequency {s.confideWeight:0.00}",
                s.confideWeight, 0f, 1f, 0.6f,
                "How often the Confide interaction is chosen. Vanilla chit-chat is 1.00. Set to 0 to turn it off.");
            s.standingHigh = Mathf.RoundToInt(list.SliderLabeled($"Feels listened to at standing {s.standingHigh}",
                s.standingHigh, 5f, 60f, 0.6f,
                "Standing is the colony's average opinion of them, plus 5 for every favour outstanding."));
            s.standingLow = Mathf.RoundToInt(list.SliderLabeled($"Feels disrespected at standing {s.standingLow}",
                s.standingLow, -50f, 0f, 0.6f));

            list.NewColumn();
            list.Label("Optional behaviours");
            list.CheckboxLabeled("Cashing in favours", ref s.favourCashIn,
                "Someone who owes a Machiavellian a favour cannot bring themselves to insult them. The favour is spent instead.");
            list.CheckboxLabeled("Divide and rule", ref s.divideAndRule,
                "A Machiavellian enjoys watching two other colonists fall out.");
            list.CheckboxLabeled("Useful and dead weight", ref s.rateUsefulness,
                "A Machiavellian rates colonists by what they contribute. This also decides who they turn on when they break.");
            list.CheckboxLabeled("Standing", ref s.standing,
                "Being well regarded lifts a Machiavellian's mood. Being overlooked eats at them.");

            list.Gap(24f);
            if (list.ButtonText("Reset to defaults"))
            {
                s.Reset();
            }
            list.End();
        }
    }
}
