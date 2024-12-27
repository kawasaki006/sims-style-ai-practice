using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

// specific AI has specific set of stats

[RequireComponent(typeof(BaseNavigation))]
public class NotSoSimpleAI : CommonAIBase
{
    [SerializeField] protected float PickInteractionInterval = 2f;
    [SerializeField] protected float DefaultInteractionScore = 0f;
    [SerializeField] protected int InteractionPickSize = 5;
    [SerializeField] bool AvoidInUseObjects = true;

    protected float TimeUntilNextInteractionPicked = -1f;

    // Start is called before the first frame update
    protected override void Start()
    {
        base.Start();
    }

    // Update is called once per frame
    protected override void Update()
    {
        base.Update();

        if (CurrentInteraction == null)
        {
            TimeUntilNextInteractionPicked -= Time.deltaTime;

            // time to pick an interaction
            if (TimeUntilNextInteractionPicked <= 0)
            {
                TimeUntilNextInteractionPicked = PickInteractionInterval;
                PickBestInteraction();
            }
        }

    }

    float ScoreInteraction(BaseInteraction interaction)
    {
        if (interaction.StatChanges.Length == 0)
        {
            return DefaultInteractionScore;
        }

        List<MemoryFragment> recentMemories = IndividualBlackboard.GetGeneric<List<MemoryFragment>>(EBlackboardKey.Memories_ShortTerm);
        List<MemoryFragment> permanentMemories = IndividualBlackboard.GetGeneric<List<MemoryFragment>>(EBlackboardKey.Memories_LongTerm);

        float score = 0f;

        foreach (var change in interaction.StatChanges)
            score += ScoreChange(change.LinkedStat, change.Value, recentMemories, permanentMemories);
        
        return score;
    }

    /*------------- SIMS ------------//
     
    Score interactions based on their impact,
    Pick randomly from those

    When currentValue is high, that stat is marginal:
        - interaction that increases it (positive amount) is NOT too preferred -> less increase in score 
        - interaction that decreases it (negative amount) is NOT too averse    -> less decrease in score

    When currentValue is low, that stat is influential:
        - interaction that increases it (positive amount) is highly preferred  -> more increase in score
        - interaction that decreases it (positive amount) is highly averse     -> more decrease in score

    //------------- SIMS ------------*/

    float ScoreChange(AIStat linkedStat, float amount, List<MemoryFragment> recentMemories, List<MemoryFragment> permanentMemories)
    {
        float currentValue = GetStatValue(linkedStat);

        currentValue = ModifyValueBasedOnMemories(currentValue, linkedStat, recentMemories);
        currentValue = ModifyValueBasedOnMemories(currentValue, linkedStat, permanentMemories);

        return (1f - currentValue) * ApplyTraitsTo(linkedStat, Trait.ETargetType.Score, amount); // penalize high current state, scaled by amount
                                     
    }

    float ModifyValueBasedOnMemories(float currentValue, AIStat linkedStat, List<MemoryFragment> memories)
    {
        foreach (var memory in memories)
        {
            foreach (var change in memory.StatChanges)
            {
                if (change.LinkedStat == linkedStat)
                    currentValue *= change.Value;
            }
        }

        return currentValue;
    }

    class ScoredInteraction
    {
        public SmartObject targetObject;
        public BaseInteraction Interaction;
        public float Score;
    }

    void PickBestInteraction()
    {
        List<GameObject> objectsInUse = null;
        HouseholdBlackboard.TryGetGeneric(EBlackboardKey.Household_ObjectsInUse, out objectsInUse, null);

        List<ScoredInteraction> unsortedInteractions = new List<ScoredInteraction>();
        // loop through all objects
        foreach (var smartObject in SmartObjectManager.Instance.RegisteredObjects)
        {
            foreach (var interaction in smartObject.Interactions)
            {
                if (!interaction.CanPerform())
                    continue;

                // skip if someone else is using
                if (AvoidInUseObjects && objectsInUse != null && objectsInUse.Contains(interaction.gameObject))
                    continue;

                float score = ScoreInteraction(interaction);

                unsortedInteractions.Add(new ScoredInteraction { targetObject = smartObject,
                                                                 Interaction = interaction, 
                                                                 Score = score });
            }
        }

        if (unsortedInteractions.Count == 0)
            return;

        // sort and pick from one of the best interactions
        var sortedInteractions = unsortedInteractions.OrderByDescending(scoredInteraction =>  scoredInteraction.Score).ToList();
        int maxIndex = Mathf.Min(InteractionPickSize, sortedInteractions.Count);

        var selectedIndex = Random.Range(0, maxIndex);

        var selectedObject = sortedInteractions[selectedIndex].targetObject;
        var selectedInteraction = sortedInteractions[selectedIndex].Interaction;

        CurrentInteraction = selectedInteraction;
        CurrentInteraction.LockInteraction(this);
        StartedPerforming = false;

        // move to the target
        if (!Navigation.SetDestination(selectedObject.InteractionPoint))
        {
            Debug.LogError($"Could not move to {selectedObject.name}");
            CurrentInteraction = null;
        }
        else
            Debug.Log($"Going to {CurrentInteraction.DisplayName} at {selectedObject.DisplayName}");
    }
}
