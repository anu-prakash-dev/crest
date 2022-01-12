using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Crest;
[ExecuteAlways]
public class DebugFloatingOrigin : MonoBehaviour
{
    FloatingOrigin m_FloatingOrigin;
    [SerializeField]
    bool m_UpdatePosition = false;
    [SerializeField]
    float m_PositionOffset = 0f;
    [SerializeField]
    float m_Offset = 0.01f;

    void Start()
    {
        m_FloatingOrigin = GetComponent<FloatingOrigin>();
    }

    void Update()
    {
        UpdatePosition();
    }

    void UpdatePosition()
    {
        if (Application.isPlaying)
        {
            return;
        }

        if (!m_UpdatePosition)
        {
            return;
        }

        var position = transform.position;
        position.x = position.z = Mathf.Floor(m_PositionOffset / m_FloatingOrigin._threshold) * m_FloatingOrigin._threshold + m_FloatingOrigin._threshold - m_Offset;
        transform.position = position;
    }
}
