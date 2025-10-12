using UnityEngine;

public class SpawnProtection : MonoBehaviour
{
    [SerializeField] float protectSeconds = 2f;
    public bool IsProtected { get; private set; }

    void OnEnable()
    {
        // Auto-enable protection on spawn
        if (protectSeconds > 0f) EnableFor(protectSeconds);
    }

    public void EnableFor(float seconds)
    {
        StopAllCoroutines();
        StartCoroutine(CoEnableFor(seconds));
    }

    System.Collections.IEnumerator CoEnableFor(float s)
    {
        IsProtected = true;
        yield return new WaitForSeconds(s);
        IsProtected = false;
    }
}
