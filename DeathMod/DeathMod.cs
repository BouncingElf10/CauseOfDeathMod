using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using HarmonyLib;
using TMPro;
using UnityEngine;

[BepInPlugin("com.bouncingelf10.deathmod", "Cause of Death Mod", "1.0.3")]
public class DeathMod : BaseUnityPlugin
{
    public static string CauseOfDeath = "Cause undetermined";
    public static string LastOpiateDrug = "";
    public static string LastStimulantDrug = "stimulants";

    void Awake()
    {
        Logger.LogInfo("Loading Cause of Death Mod...");
        new Harmony("com.bouncingelf10.deathmod").PatchAll();
        Logger.LogInfo("Cause of Death mod loaded!");
    }

    public static List<string> DetectAllCauses(Body body)
    {
        var causes = new List<string>();

        var painkillers = body.GetComponent<Painkillers>();
        if (painkillers != null)
        {
            float op = (float) AccessTools.Field(typeof(Painkillers), "opiateAmount").GetValue(painkillers);
            if (op > 0f && !body.breathing)
            {
                string drug = LastOpiateDrug != "" ? LastOpiateDrug : "opiate";
                causes.Add($"Overdose - {drug} - respiratory depression");
            }
            else if (op > 0f && body.consciousness < 10f)
            {
                string drug = LastOpiateDrug != "" ? LastOpiateDrug : "opiate";
                causes.Add($"Overdose - {drug} - loss of consciousness");
            }
        }

        var sleepingPills = body.GetComponent<SleepingPills>();
        if (sleepingPills != null)
        {
            float amt = (float) AccessTools.Field(typeof(SleepingPills), "amount").GetValue(sleepingPills);
            if (amt > 0f && body.consciousness < 10f)
                causes.Add("Overdose - sleeping pills");
        }

        if (body.overdoseIndex >= 3 && body.fibrillationProgress > 1f)
            causes.Add($"Overdose - {LastStimulantDrug} - cardiac event");

        if (body.overdoseIndex >= 3 && body.bloodPressure > 170f)
            causes.Add($"Overdose - {LastStimulantDrug} - hypertensive crisis");

        var antideps = body.GetComponent<Antidepressants>();
        if (antideps != null && body.bloodPressure < 80f)
            causes.Add("Overdose - antidepressants - circulatory collapse");

        if (body.inWater && body.bloodOxygen < 65f)
            causes.Add("Drowning");

        if (!body.breathing)
            causes.Add("Suffocation - airway obstructed");

        if (body.hasPulmonaryEmbolism)
            causes.Add("Pulmonary embolism");

        if (body.fibrillationProgress > 1f)
            causes.Add("Cardiac arrest - ventricular fibrillation");

        if (body.inCardiacArrest)
            causes.Add("Cardiac arrest");

        if (body.strokeAmount > 50f)
            causes.Add("Stroke");

        if (body.septicShock > 75f)
            causes.Add("Septic shock");

        if (body.venomCurrent > 50f)
            causes.Add("Envenomation");

        if (body.bloodOxygen < 65f)
            causes.Add("Hypoxia - critically low blood oxygen");

        if (body.totalBleedSpeed > 0.134f || (body.totalBleedSpeed > 0.02f && body.bloodVolume < 40f))
            causes.Add("Exsanguination - severe blood loss");

        if (body.bloodPressure < 80f)
            causes.Add("Circulatory failure - hypotension");

        if (body.bloodPressure > 170f)
            causes.Add("Hypertensive crisis");

        if (body.temperature < 29f)
            causes.Add($"Hypothermia - core temp {body.temperature:F1}°C");

        if (body.temperature > 41f)
            causes.Add($"Hyperthermia - core temp {body.temperature:F1}°C");

        if (body.radiationSickness > 60f)
            causes.Add("Acute radiation syndrome");

        if (body.hunger < 10f)
            causes.Add("Starvation");

        if (body.thirst < 10f)
            causes.Add("Dehydration");

        if (causes.Count == 0)
            causes.Add("Cause undetermined");

        return causes;
    }

    public static string DetermineCause(Body body)
    {
        var all = DetectAllCauses(body);
        return all[0];
    }

    public static List<string> AllCausesOfDeath = new List<string>();
}

[HarmonyPatch(typeof(Body), "Start")]
public static class PatchBodyStart
{
    static void Postfix(Body __instance)
    {
        __instance.StartCoroutine(TrackDeathCause(__instance));
    }

    static IEnumerator TrackDeathCause(Body body)
    {
        float lastOpiateAmount = 0f;

        while (body != null)
        {
            yield return new WaitForSeconds(0.5f);

            if (body == null) yield break;
            var pk = body.GetComponent<Painkillers>();

            if (pk != null)
            {
                float current = (float)AccessTools.Field(typeof(Painkillers), "opiateAmount").GetValue(pk);
                float delta = current - lastOpiateAmount;
                lastOpiateAmount = current;

                if (delta > 15f) DeathMod.LastOpiateDrug = "fentanyl";
                else if (delta > 5f) DeathMod.LastOpiateDrug = "painkillers";
                else if (delta > 2f) DeathMod.LastOpiateDrug = "heroin";
                else if (delta > 1f) DeathMod.LastOpiateDrug = "morphine";
                else if (delta > 0f) DeathMod.LastOpiateDrug = "opium";
            }

            if (body.overdoseIndex >= 3)
            {
                if (body.onHardStimulants) DeathMod.LastStimulantDrug = "high-grade stimulant";
                else DeathMod.LastStimulantDrug = "stimulants";
            }

            if (body.isDying || !body.alive)
            {
                DeathMod.CauseOfDeath = DeathMod.DetermineCause(body);
            }

            if (!body.alive) yield break;
        }
    }
}

[HarmonyPatch(typeof(PlayerCamera), "HandleDeathScreen")]
public static class PatchHandleDeathScreen
{
    static void Prefix(PlayerCamera __instance, bool ___didDeathScreen, Body ___body, RectTransform ___deathStats)
    {
        if (___body == null || ___body.alive || ___didDeathScreen) return;

        DeathMod.AllCausesOfDeath = DeathMod.DetectAllCauses(___body);
        DeathMod.CauseOfDeath = DeathMod.AllCausesOfDeath[0];

        if (___deathStats == null) return;

        __instance.StartCoroutine(InjectAfterDelay(___deathStats));
    }

    static IEnumerator InjectAfterDelay(RectTransform deathStats)
    {
        yield return null;

        var labelTMP = deathStats.GetChild(10).GetComponent<TextMeshProUGUI>();
        var valueTMP = deathStats.GetChild(11).GetComponent<TextMeshProUGUI>();

        labelTMP.text = "CAUSE OF DEATH";
        valueTMP.text = DeathMod.CauseOfDeath.ToUpper();

        var old = valueTMP.gameObject.GetComponent<UITooltip>();
        if (old != null) Object.Destroy(old);

        if (DeathMod.AllCausesOfDeath.Count > 1)
        {
            valueTMP.text = DeathMod.CauseOfDeath.ToUpper() + " [+]";

            var tooltip = valueTMP.gameObject.AddComponent<UITooltip>();
            tooltip.skipLocale = true;
            tooltip.tipName = "ALL CAUSES";
            tooltip.tipDesc = string.Join("\n", DeathMod.AllCausesOfDeath.Select(c => c.ToUpper()));
        }
    }
}