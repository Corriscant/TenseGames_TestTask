using UnityEngine;
using System.Collections.Generic;
using Fusion;

public class SelectionManager : MonoBehaviour
{
    [SerializeField] private RectTransform selectionBox; // UI for selection box
    [SerializeField] private Camera mainCamera; // Camera for raycasting
    [SerializeField] private LayerMask selectableLayer; // Layer for units

    private Vector2 _startPosition;
    private RectTransform _canvasRect; // Canvas holding SelectionRect

    // made it [SerializeField] for Debug purposes
    [SerializeField] private List<Unit> _selectedUnits = new();
    public List<Unit> SelectedUnits => _selectedUnits;

    public void Start()
    {
        _canvasRect = selectionBox.GetComponentInParent<Canvas>().GetComponent<RectTransform>();
    }

    public void StartSelection(Vector2 startPosition)
    {
        // Convert screen coordinates to local coordinates of the selection box
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, startPosition, mainCamera, out Vector2 localPoint);
        _startPosition = localPoint;

        selectionBox.gameObject.SetActive(true);
    }

    public void UpdateSelection(Vector2 currentPosition)
    {
        // Convert current position to local coordinates of the Canvas
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, currentPosition, mainCamera, out Vector2 localPoint);

        // Calculate the size of the selection box
        Vector2 size = localPoint - _startPosition;
        selectionBox.sizeDelta = new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y));
        selectionBox.anchoredPosition = _startPosition + size / 2;
    }

    public void EndSelection()
    {
        selectionBox.gameObject.SetActive(false);

        // Select new units
        SelectUnits(BasicSpawner.Instance.NetRunner.LocalPlayer);
    }

    private void SelectUnits(PlayerRef localPlayer)
    {
        // Clear previous selection
        foreach (var unit in _selectedUnits)
        {
            unit.Selected = false;
        }
        _selectedUnits.Clear();

        foreach (var unit in FindObjectsByType<Unit>(FindObjectsSortMode.None))
        {
            Vector3 unitPosition_Screen = mainCamera.WorldToScreenPoint(unit.transform.position);

            // Convert local coordinates of the selection box to screen coordinates (reverse action to what was done in StartSelection)
            var leftTop_Local = new Vector2(selectionBox.anchoredPosition.x - selectionBox.sizeDelta.x / 2, selectionBox.anchoredPosition.y - selectionBox.sizeDelta.y / 2);
            var rightBottom_Local = new Vector2(selectionBox.anchoredPosition.x + selectionBox.sizeDelta.x / 2, selectionBox.anchoredPosition.y + selectionBox.sizeDelta.y / 2);

            Vector2 leftTop_Screen = LocalToScreenPoint(mainCamera, _canvasRect, leftTop_Local);
            Vector2 rightBottom_Screeen = LocalToScreenPoint(mainCamera, _canvasRect, rightBottom_Local);

            Rect selectionBox_Screen = GetRectFromPoints(leftTop_Screen, rightBottom_Screeen);

            if (selectionBox_Screen.Contains(unitPosition_Screen))
            {
                if (unit.IsOwnedBy(localPlayer))
                {
                    unit.Selected = true;
                    _selectedUnits.Add(unit);
                    Debug.Log($"Unit selected: {unit.name}");
                }
            }
        }
    }

    public static Vector2 LocalToScreenPoint(Camera mainCamera, RectTransform rectTransform, Vector2 localPoint)
    {
        // Convert local point to world coordinates
        Vector3 worldPoint = rectTransform.TransformPoint(localPoint);

        // Convert world coordinates to screen coordinates
        return mainCamera.WorldToScreenPoint(worldPoint);
    }

    private Rect GetRectFromPoints(Vector2 point1, Vector2 point2)
    {
        float xMin = Mathf.Min(point1.x, point2.x);
        float yMin = Mathf.Min(point1.y, point2.y);
        float width = Mathf.Abs(point1.x - point2.x);
        float height = Mathf.Abs(point1.y - point2.y);

        return new Rect(xMin, yMin, width, height);
    }
}
