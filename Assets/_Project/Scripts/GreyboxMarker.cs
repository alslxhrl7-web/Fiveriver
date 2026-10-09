using UnityEngine;

/// <summary>
/// 그레이박스 오브젝트에 붙는 식별 표식. M01GreyboxBuilder가 id(CP01, SPAWN_PLAYER 등)와 메모를 채운다.
/// 런타임 코드에서 위치 기준점을 찾을 때 쓴다. 사실적 씬에서는 메시를 숨기므로 씬 뷰에서 기즈모로 위치를 보여 준다.
/// </summary>
public class GreyboxMarker : MonoBehaviour
{
    public string id;
    [TextArea] public string note;

    void OnDrawGizmos()
    {
        Gizmos.color = id != null && id.StartsWith("ENEMY") ? new Color(0.9f, 0.4f, 0.2f)
            : id != null && id.StartsWith("CP") ? new Color(0.9f, 0.9f, 0.3f)
            : new Color(0.3f, 0.7f, 1f);
        Gizmos.DrawWireSphere(transform.position, 0.5f);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 1.2f, id);
#endif
    }
}
