using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ForceOpenDoor
{
    public class ForceOpenDoorMod : Mod
    {
        public ForceOpenDoorMod(ModContentPack content) : base(content)
        {
            Harmony harmony = new Harmony("com.username.forceopendoor");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
    }

    [DefOf]
    public static class ForceOpenDoorJobDefOf
    {
        public static JobDef ForceOpenDoorJob;

        static ForceOpenDoorJobDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ForceOpenDoorJobDefOf));
        }
    }

    public class ForceOpenDoorOptionProvider : FloatMenuOptionProvider
    {
        protected override bool Undrafted => true;
        protected override bool Drafted => true;
        protected override bool Multiselect => true;

        public override bool Applies(FloatMenuContext context)
        {
            return context.ValidSelectedPawns.Any(p => p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)) && context.ValidSelectedPawns.Count() == 1;
        }

        public override bool TargetThingValid(Thing thing, FloatMenuContext context)
        {
            return thing is Building_Door door && (door.Faction == null || door.Faction.IsPlayer);
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing thing, FloatMenuContext context)
        {
            if (!(thing is Building_Door door))
                yield break;

            foreach (Pawn pawn in context.ValidSelectedPawns)
            {
                if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                    continue;

                // Vérifier si la porte est déjà en mode "hold open"
                FieldInfo holdOpenField = typeof(Building_Door).GetField("holdOpenInt", BindingFlags.Instance | BindingFlags.NonPublic);
                bool isHoldingOpen = false;
                if (holdOpenField != null)
                {
                    isHoldingOpen = (bool)holdOpenField.GetValue(door);
                }

                // Changer le texte en fonction de l'état actuel
                string text = isHoldingOpen ? "Closethedoor".Translate() : "Openthedoor".Translate();

                Action action = delegate
                {
                    // Créer une tâche pour le colon avec notre job personnalisé
                    // et passer l'état actuel pour que le job puisse l'inverser
                    Job job = new Job(ForceOpenDoorJobDefOf.ForceOpenDoorJob, door);
                    job.haulMode = isHoldingOpen ? HaulMode.Undefined : HaulMode.ToCellStorage; // Utiliser haulMode comme flag
                    pawn.jobs.TryTakeOrderedJob(job);
                };

                // Vérifiez si le pawn peut atteindre la porte
                if (!pawn.CanReach(door, PathEndMode.OnCell, Danger.Some))
                {
                    text = text + "Cannotreachthedoor".Translate();
                    action = null;
                }

                yield return FloatMenuUtility.DecoratePrioritizedTask(
                    new FloatMenuOption(text, action), pawn, door);
            }
        }
    }

    // Notre JobDriver personnalisé pour forcer l'ouverture d'une porte
    public class JobDriver_ForceOpenDoor : JobDriver
    {
        private Building_Door Door => (Building_Door)job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);

            // Se déplacer vers la porte
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.OnCell);

            // Forcer l'ouverture
            Toil forceOpen = new Toil();
            forceOpen.initAction = delegate
            {
                Building_Door door = Door;
                if (door != null)
                {
                    // Utiliser la réflexion pour accéder au champ privé holdOpenInt
                    FieldInfo holdOpenField = typeof(Building_Door).GetField("holdOpenInt", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (holdOpenField != null)
                    {
                        // Vérifier si nous devons ouvrir ou fermer la porte (en utilisant haulMode comme flag)
                        bool shouldHoldOpen = job.haulMode == HaulMode.ToCellStorage;

                        // Inverser l'état actuel
                        holdOpenField.SetValue(door, shouldHoldOpen);

                        // Ouvrir ou fermer la porte selon le besoin
                        if (shouldHoldOpen && !door.Open)
                        {
                            door.StartManualOpenBy(pawn);
                        }
                        else if (!shouldHoldOpen && door.Open)
                        {
                            // Pour fermer une porte, on doit désactiver holdOpen
                            // Le jeu s'occupera de la fermer automatiquement
                        }
                    }
                }
            };
            forceOpen.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return forceOpen;
        }
    }
}