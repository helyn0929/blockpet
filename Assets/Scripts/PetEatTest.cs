using UnityEngine;

public class PetEatTest : MonoBehaviour
{
    public Animator anim;
    public PetAnimationEvents events;

    void Reset()
    {
        anim = GetComponentInChildren<Animator>();
        events = GetComponentInChildren<PetAnimationEvents>();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.T))
        {
            Debug.Log("[PetEatTest] Trigger Eat");
            if (anim != null) anim.SetTrigger("Eat");
            else Debug.LogWarning("[PetEatTest] Animator not found");
        }

        if (Input.GetKeyDown(KeyCode.Y))
        {
            Debug.Log("[PetEatTest] Call OnEatBite directly");
            if (events != null) events.OnEatBite("bite");
            else Debug.LogWarning("[PetEatTest] PetAnimationEvents not found");
        }
    }
}
