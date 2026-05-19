using UnityEngine;

public abstract class Character : MonoBehaviour
{
    [Header("Stats")]
    public float maxHealth = 100f;
    public float currentHealth;

    public bool IsDead => currentHealth <= 0f;

    protected virtual void Awake()
    {
        currentHealth = maxHealth;
    }

    public virtual void TakeDamage(float amount)
    {
        currentHealth = Mathf.Max(0f, currentHealth - amount);
    }

    public virtual void Heal(float amount)
    {
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
    }
}
