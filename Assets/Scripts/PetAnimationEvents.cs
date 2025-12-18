using UnityEngine;

public class PetAnimationEvents : MonoBehaviour
{
    // Eat animation bite moment
    public void OnEatBite(string note)
    {
        Debug.Log("Eat Bite Event: " + note);
    }
}
