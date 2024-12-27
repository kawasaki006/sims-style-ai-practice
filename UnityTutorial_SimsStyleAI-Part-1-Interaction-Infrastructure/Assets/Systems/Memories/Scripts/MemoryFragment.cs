using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "AI/Memory", fileName = "Memory")]
public class MemoryFragment : ScriptableObject
{
    public string Name;
    public string Description;
    public float Duration = 0f;
    public InteractionStatChange[] StatChanges;
    public MemoryFragment[] MemoriesCountered;

    public int Occurrences { get; private set; } = 0;
    public float DurationRemaning { get; private set; } = 0f;

    public bool IsSimilarTo(MemoryFragment other)
    {
        return Name == other.Name && Description == other.Description;
    }

    public bool IsCancelledBy(MemoryFragment other)
    {
        foreach (var fragment in MemoriesCountered)
        {
            if (fragment.IsSimilarTo(other))
                return true;
        }

        return false;
    }

    public void Reinforce(MemoryFragment other)
    {
        DurationRemaning = Mathf.Max(DurationRemaning, other.DurationRemaning);
        Occurrences++;
    }

    public MemoryFragment Duplicate()
    {
        var newMemory = ScriptableObject.Instantiate(this);
        newMemory.Occurrences = 1;
        newMemory.DurationRemaning = Duration;
        return newMemory;
    }

    public bool Tick(float deltaTime)
    {
        DurationRemaning -= deltaTime;

        return DurationRemaning > 0f;
    }
}
