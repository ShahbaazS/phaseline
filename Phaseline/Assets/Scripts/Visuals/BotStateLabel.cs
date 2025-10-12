using UnityEngine;
using TMPro;

[RequireComponent(typeof(BotController))]
public class BotStateLabel : MonoBehaviour
{
    public Vector3 offset = new Vector3(0, 1.6f, 0);
    public bool faceCamera = true;

    BotController ai;
    TextMeshPro tmp;
    Camera cam;

    void Awake()
    {
        ai = GetComponent<BotController>();
        cam = Camera.main;

        // Build a child with TMP text (world-space)
        var go = new GameObject("StateLabel");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = offset;

        tmp = go.AddComponent<TextMeshPro>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 2.2f;
        tmp.text = "";
        tmp.enableAutoSizing = false;
        tmp.raycastTarget = false;
        tmp.sortingOrder = 100; // render on top
    }

    void LateUpdate()
    {
        if (!tmp) return;

        // Text and color by state
        tmp.text = ai.State.ToString();

        switch (ai.State)
        {
            case BotController.BotState.Emergency: tmp.color = new Color(1,0.25f,0.25f,1); break;
            case BotController.BotState.Evade:     tmp.color = new Color(1,0.85f,0.25f,1); break;
            case BotController.BotState.Cutoff:    tmp.color = new Color(0.6f,1,0.25f,1); break;
            default:                                 tmp.color = new Color(0.25f,0.8f,1,1); break;
        }

        if (faceCamera && cam)
        {
            var toCam = (cam.transform.position - tmp.transform.position).normalized;
            tmp.transform.rotation = Quaternion.LookRotation(-toCam, Vector3.up);
        }
    }
}