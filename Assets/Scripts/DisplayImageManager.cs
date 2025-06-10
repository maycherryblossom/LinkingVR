using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

public class DisplayImageManager : MonoBehaviour
{
    [SerializeField] private RawImage[] displaySlots;
    [SerializeField] private Texture2D defaultPlaceholder;

    // 각 RawImage 슬롯이 어느 owner에게 쓰이고 있는지 추적
    private readonly Dictionary<object, List<RawImage>> _ownerSlots
        = new Dictionary<object, List<RawImage>>();

    /// <summary>
    /// owner별로 텍스처 슬롯을 할당합니다.
    /// </summary>
    public RawImage[] SetDisplayImages(object owner, Texture2D[] textures)
    {
        // 1) 기존 owner 슬롯 해제
        if (_ownerSlots.TryGetValue(owner, out var oldSlots))
        {
            foreach (var slot in oldSlots)
                ReleaseSlot(slot);
            _ownerSlots.Remove(owner);
        }

        // 2) 다른 owner들이 쓰는 슬롯 집합
        var usedByOthers = _ownerSlots
            .Values
            .SelectMany(list => list)
            .ToHashSet();

        var assigned = new List<RawImage>();

        // 3) 각 텍스처마다 빈 슬롯 고르기 (다른 owner + 이번에 할당된 것 모두 제외)
        foreach (var tex in textures)
        {
            RawImage free = displaySlots
                .FirstOrDefault(s =>
                    !usedByOthers.Contains(s) &&
                    !assigned.Contains(s));

            if (free == null)
            {
                Debug.LogWarning("[DisplayImageManager] No free slots left!");
                break;
            }

            free.texture = tex;
            free.name    = $"OCR_IMG_{tex.GetInstanceID()}";
            free.gameObject.SetActive(true);

            assigned.Add(free);
        }

        // 4) 이 owner의 슬롯으로 기록 후 반환
        _ownerSlots[owner] = assigned;
        return assigned.ToArray();
    }

    /// <summary>
    /// owner가 차지한 슬롯만 해제합니다.
    /// </summary>
    public void ClearDisplayImages(object owner)
    {
        if (!_ownerSlots.TryGetValue(owner, out var slots)) return;

        foreach (var slot in slots)
            ReleaseSlot(slot);

        _ownerSlots.Remove(owner);
    }

    // 플레이스홀더 또는 꺼버리기
    private void ReleaseSlot(RawImage slot)
    {
        if (defaultPlaceholder != null)
        {
            slot.texture = defaultPlaceholder;
            slot.gameObject.SetActive(true);
        }
        else
        {
            slot.gameObject.SetActive(false);
        }
    }
}
