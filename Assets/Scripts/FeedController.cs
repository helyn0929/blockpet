using UnityEngine;

public class FeedController : MonoBehaviour
{
    public GameObject pet;

    public void Feed()
    {
        Debug.Log("button clicked");
        pet.transform.localScale += Vector3.one * 0.1f;
    }
}

