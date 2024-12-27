using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using static Trait;

[System.Serializable]
public class AIStatConfiguration
{
    [field: SerializeField] public AIStat LinkedStat { get; private set; }
    [field: SerializeField] public bool OverrideDefault { get; private set; } = false;
    [field: SerializeField, Range(0f, 1f)] public float Override_InitialValue { get; protected set; } = 0.5f;
    [field: SerializeField, Range(0f, 1f)] public float Override_DecayRate { get; protected set; } = 0.005f;
}

[RequireComponent(typeof(BaseNavigation))]
public class CommonAIBase : MonoBehaviour
{
    [Header("General")]
    [SerializeField] int HouseholdID = 1;
    [field: SerializeField] AIStatConfiguration[] Stats;
    [SerializeField] protected FeedbackUIPanel LinkedUI;

    [Header("Traits")]
    [SerializeField] protected List<Trait> Traits;

    [Header("Memories")]
    [SerializeField] int LongTermMemoryThreshold = 2;

    protected BaseNavigation Navigation;

    protected bool StartedPerforming = false;

    public Blackboard IndividualBlackboard { get; protected set; }
    public Blackboard HouseholdBlackboard { get; protected set; }

    protected Dictionary<AIStat, float> DecayRates = new Dictionary<AIStat, float>();
    protected Dictionary<AIStat, AIStatPanel> StatUIPanels = new Dictionary<AIStat, AIStatPanel>();

    protected BaseInteraction CurrentInteraction
    {
        get 
        {
            BaseInteraction interaction = null;
            IndividualBlackboard.TryGetGeneric<BaseInteraction>(EBlackboardKey.Character_FocusObject, out interaction, null);
            return interaction; 
        }
        set 
        { 
            BaseInteraction previousInteraction = null;
            IndividualBlackboard.TryGetGeneric(EBlackboardKey.Character_FocusObject, out previousInteraction, null);

            IndividualBlackboard.SetGeneric(EBlackboardKey.Character_FocusObject, value);

            List<GameObject> objectsInUse = null;
            HouseholdBlackboard.TryGetGeneric(EBlackboardKey.Household_ObjectsInUse, out objectsInUse, null);

            // are we starting to use something?
            if (value != null)
            {
                // need to create list?
                if (objectsInUse == null) 
                    objectsInUse = new List<GameObject>();

                // not already in list? add and update the blackboard
                if (!objectsInUse.Contains(value.gameObject))
                {
                    objectsInUse.Add(value.gameObject);
                    HouseholdBlackboard.SetGeneric(EBlackboardKey.Household_ObjectsInUse, objectsInUse);
                }
            } // we've stopped using something
            else if (objectsInUse != null) 
            {
                // attempt to remove and update the blackboard if changed 
                if (objectsInUse.Remove(previousInteraction.gameObject))
                    HouseholdBlackboard.SetGeneric(EBlackboardKey.Household_ObjectsInUse, objectsInUse);
            }
        }
    }

    protected virtual void Awake()
    {
        Navigation = GetComponent<BaseNavigation>();
    }

    // Start is called before the first frame update
    protected virtual void Start()
    {
        HouseholdBlackboard = BlackboardManager.Instance.GetSharedBlackboard(HouseholdID);
        IndividualBlackboard = BlackboardManager.Instance.GetIndividualBlackboard(this);

        // set up memory in blackboard
        IndividualBlackboard.SetGeneric(EBlackboardKey.Memories_ShortTerm, new List<MemoryFragment>());
        IndividualBlackboard.SetGeneric(EBlackboardKey.Memories_LongTerm, new List<MemoryFragment>());

        // set up stats
        foreach (var statConfig in Stats)
        {
            var linkedStat = statConfig.LinkedStat;
            float initialValue = statConfig.OverrideDefault ? statConfig.Override_InitialValue : linkedStat.InitialValue;
            float decayRate = statConfig.OverrideDefault ? statConfig.Override_DecayRate : linkedStat.DecayRate;

            // set up initial blackboard stat
            IndividualBlackboard.SetStat(linkedStat, initialValue);
            DecayRates[linkedStat] = decayRate;

            // set up initial stat display
            if (linkedStat.isVisible)
                StatUIPanels[linkedStat] = LinkedUI.AddStat(linkedStat, initialValue);
        }
                
    }

    protected float ApplyTraitsTo(AIStat targetStat, Trait.ETargetType targetType, float currentValue)
    {
        foreach (var trait in Traits)
        {
            currentValue = trait.Apply(targetStat, targetType, currentValue);
        }
        return currentValue;
    }

    // Update is called once per frame
    protected virtual void Update()
    {
        if (CurrentInteraction != null)
        {
            if (Navigation.IsAtDestination && !StartedPerforming)
            {
                StartedPerforming = true;
                CurrentInteraction.Perform(this, OnInteractionFinished);
            }
        }

        // apply the decay rate
        foreach (var statConfig in Stats)
        {
            UpdateIndividualStat(statConfig.LinkedStat, -DecayRates[statConfig.LinkedStat] * Time.deltaTime, ETargetType.DecayRate);
        }

        // tick recent memories
        List<MemoryFragment> recentMemories = IndividualBlackboard.GetGeneric<List<MemoryFragment>>(EBlackboardKey.Memories_ShortTerm);
        bool memoriesChanged = false;

        for (int index = recentMemories.Count - 1; index >= 0; index --)
        {
            if (!recentMemories[index].Tick(Time.deltaTime))
            {
                recentMemories.RemoveAt(index);
            }
        }

        // update blackboard
        if (memoriesChanged)
            IndividualBlackboard.SetGeneric(EBlackboardKey.Memories_ShortTerm, recentMemories);
    }

    protected virtual void OnInteractionFinished(BaseInteraction interaction)
    {
        interaction.UnlockInteraction(this);
        CurrentInteraction = null;
        Debug.Log($"Finished {interaction.DisplayName}");
    }

    public void UpdateIndividualStat(AIStat linkedStat, float amount, Trait.ETargetType targetType)
    {
        float adjustedAmount = ApplyTraitsTo(linkedStat, targetType, amount);
        float newValue = Mathf.Clamp01(GetStatValue(linkedStat) + adjustedAmount);

        // update blackboard stat
        IndividualBlackboard.SetStat(linkedStat, newValue);
        // update stat display
        if (linkedStat.isVisible)
            StatUIPanels[linkedStat].OnStatChanged(newValue);
    }

    public float GetStatValue(AIStat linkedStat)
    {
        return IndividualBlackboard.GetStat(linkedStat);
    } 

    public void AddMemories(MemoryFragment[] memoriesToAdd)
    {
        foreach (var memory in memoriesToAdd)
            AddMemory(memory);
    }

    /*------------- Add Memory Logic ------------//

    1. Check if memoryToAdd exists in long-term memory list
        - Yes -> do nothing and return
        - No -> check does it cancel any long-term memory
            - Yes -> cancel and update blackboard long-term memory
    2. Check if memoryToAdd exists in short-term memory list
        - No -> simply add and update blackboard short-term memory
        - Yes -> reinforce it and check if it satisfies the requirement for long-term memory
            - Yes -> transform it into a long-term memory
        - check does it cancel any short-term memory
            - Yes -> cancel and update blackboard short-term list

    //------------- Add Memory Logic ------------*/

    protected void AddMemory(MemoryFragment memoryToAdd)
    {
        List<MemoryFragment> permanentMemories = IndividualBlackboard.GetGeneric<List<MemoryFragment>>(EBlackboardKey.Memories_LongTerm);

        // in permanent memory already?
        MemoryFragment memoryToCancel = null;
        foreach (var memory in permanentMemories)
        {
            if (memoryToAdd.IsSimilarTo(memory))
                return;
            if (memory.IsCancelledBy(memoryToAdd))
                memoryToCancel = memory;
        }

        // does this cancel a long-term memory?
        if (memoryToCancel != null)
        {
            permanentMemories.Remove(memoryToCancel);
            // update blackboard
            IndividualBlackboard.SetGeneric(EBlackboardKey.Memories_LongTerm, permanentMemories);
        }

        List<MemoryFragment> recentMemories = IndividualBlackboard.GetGeneric<List<MemoryFragment>>(EBlackboardKey.Memories_ShortTerm);

        // does this exists? 
        MemoryFragment existingRecentMemory = null;
        foreach (var memory in recentMemories)
        {
            if (memoryToAdd.IsSimilarTo(memory))
                existingRecentMemory = memory;
            if (memory.IsCancelledBy(memoryToAdd))
                memoryToCancel = memory;
        }

        // does this cancel a recent memory?
        if (memoryToCancel != null)
        {
            recentMemories.Remove(memoryToCancel);
            // update blackboard
            IndividualBlackboard.SetGeneric(EBlackboardKey.Memories_ShortTerm, recentMemories);
        }

        // add memoryToAdd to short-term memories if it does not already exist
        if (existingRecentMemory == null)
        {
            Debug.Log($"Added memory {memoryToAdd.Name}");

            recentMemories.Add(memoryToAdd.Duplicate());
            // update blackboard
            IndividualBlackboard.SetGeneric(EBlackboardKey.Memories_ShortTerm, recentMemories);
        } 
        // reinforce memoryToAdd if it already exists
        else
        {
            Debug.Log($"Reinforced memory {memoryToAdd.Name}");

            existingRecentMemory.Reinforce(memoryToAdd);

            // transform this into a long-term memory
            if (existingRecentMemory.Occurrences >= LongTermMemoryThreshold)
            {
                permanentMemories.Add(existingRecentMemory);
                recentMemories.Remove(existingRecentMemory);

                // update blackboard
                IndividualBlackboard.SetGeneric(EBlackboardKey.Memories_ShortTerm, recentMemories);
                IndividualBlackboard.SetGeneric(EBlackboardKey.Memories_LongTerm, permanentMemories);

                Debug.Log($"Memory {existingRecentMemory.Name} became permanent!");
            }
        }
    }
}
