using UnityEngine;

/// <summary>
/// 그레이박스 오브젝트에 붙는 식별 표식. M01GreyboxBuilder가 id(CP01, SPAWN_PLAYER 등)와 메모를 채운다.
/// 런타임 코드에서 위치 기준점을 찾을 때 쓴다.
/// </summary>
public class GreyboxMarker : MonoBehaviour
{
    public string id;
    [TextArea] public string note;
}
