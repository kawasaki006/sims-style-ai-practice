using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

public enum EInteractionType
{
    Instantaneous = 0,
    OverTime = 1,
    AfterTime = 2
}

[System.Serializable] 
public class InteractionStatChange
{
    public AIStat LinkedStat;   // target stat
    public float Value;    // amount of stat change
}

[System.Serializable]
public class InteractionOutcome
{
    public string Description;
    [Range(0f, 1f)] public float Weighting = 1f;
    public float StatMultiplier = 1f;
    public bool AbandonInteraction = false;

    public InteractionStatChange[] StatChanges;
    public MemoryFragment[] MemoriesCaused;

    public float NormalizedWeighting { get; set; } = -1f;
}

public abstract class BaseInteraction : MonoBehaviour
{
    [SerializeField] protected string _DisplayName;
    [SerializeField] protected EInteractionType _InteractionType = EInteractionType.Instantaneous;
    [SerializeField] protected float _Duration = 0f;
    [SerializeField, FormerlySerializedAs("StatChanges")] protected InteractionStatChange[] _StatChanges;
    [SerializeField] InteractionOutcome[] _Outcomes = new InteractionOutcome[] { new InteractionOutcome() {
        Weighting = 1f, Description = ""
    } };
    bool OutcomeWeightingsNormalized = false;

    public string DisplayName => _DisplayName;
    public EInteractionType InteractionType => _InteractionType;
    public float Duration => _Duration;
    public InteractionStatChange[] StatChanges => _StatChanges;

    public abstract bool CanPerform();
    public abstract bool LockInteraction(CommonAIBase performer);
    public abstract bool Perform(CommonAIBase performer, UnityAction<BaseInteraction> onCompleted);
    public abstract bool UnlockInteraction(CommonAIBase performer);

    public bool ApplyInteractionEffects(CommonAIBase performer, float proportion, bool rollForOutcomes)
    {
        bool abandonInteraction = false;

        InteractionOutcome selectedOutcome = null;
        if (rollForOutcomes && _Outcomes.Length > 0)
        {
            // normalize weightings if needed
            if (!OutcomeWeightingsNormalized)
            {
                OutcomeWeightingsNormalized = true;
                float weightingSum = 0;
                foreach (var outcome in _Outcomes)
                {
                    weightingSum += outcome.Weighting;
                }

                foreach (var outcome in _Outcomes)
                {
                    outcome.NormalizedWeighting = outcome.Weighting / weightingSum;
                }
            }

            // pick an outcome
            float randomRoll = Random.value;
            foreach (var outcome in _Outcomes)
            {
                if (randomRoll <= outcome.NormalizedWeighting)
                {
                    selectedOutcome = outcome;
                    if (selectedOutcome.AbandonInteraction)
                        abandonInteraction = true;

                    break;
                }

                randomRoll -= outcome.NormalizedWeighting;
            }
        }

        float statMultiplier = selectedOutcome != null ? selectedOutcome.StatMultiplier : 1f;

        foreach (var statChange in StatChanges)
        {
            performer.UpdateIndividualStat(statChange.LinkedStat, statMultiplier * statChange.Value * proportion, Trait.ETargetType.Impact);
        }

        if (selectedOutcome != null)
        {
            if (!string.IsNullOrEmpty(selectedOutcome.Description))
                Debug.Log($"Outcome was {selectedOutcome.Description}");
            foreach(var statChange in selectedOutcome.StatChanges)
            {
                performer.UpdateIndividualStat(statChange.LinkedStat, statChange.Value * proportion, Trait.ETargetType.Impact);
            }

            // if the outcome causes any memory change
            if (selectedOutcome.MemoriesCaused.Length > 0)
            {
                // performer holds memories
                performer.AddMemories(selectedOutcome.MemoriesCaused);
            }
        }
        
        return !abandonInteraction;
    }
}
