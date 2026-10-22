using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Events;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// RTS Mouse Controller for unit selection and commands.
    /// Handles selection boxes, click-to-select, and right-click commands.
    /// </summary>
    public class RTSMouseController : MonoBehaviour
    {
        #region Enums
        
        public enum SelectionMode
        {
            /// <summary>Single click selects, click empty deselects all</summary>
            SingleClick,
            
            /// <summary>Shift+click adds/removes from selection</summary>
            ShiftModifier,
            
            /// <summary>Ctrl+click adds to selection, Shift+click removes</summary>
            CtrlShiftModifier
        }
        
        #endregion
        
        #region Events
        
        [System.Serializable]
        public class SelectionEvent : UnityEvent<GameObject[]> { }
        
        [System.Serializable]
        public class CommandEvent : UnityEvent<Vector3> { }
        
        [System.Serializable]
        public class TargetCommandEvent : UnityEvent<GameObject> { }
        
        #endregion
        
        #region Inspector Fields
        
        [Header("Player Controlled Factions")]
        [Tooltip("Select which factions can be controlled by the player. Only agents belonging to these factions can be selected and given move/attack commands. If no factions are selected, all factions can be controlled.")]
        [SerializeField] private FactionMask playerControlledFactions = new FactionMask();
        
        [Header("Selection Settings")]
        [Tooltip("Enable box selection (drag to select multiple units)")]
        [SerializeField] private bool enableBoxSelection = true;
        
        [Tooltip("Mouse button for selection (reserved for future use)")]
        #pragma warning disable CS0414 // Field is assigned but never used
        [SerializeField] private int selectionButton = 0; // Left click
        #pragma warning restore CS0414
        
        [Tooltip("Minimum drag distance to start box selection (in pixels)")]
        [SerializeField] private float minBoxSelectionDrag = 10f;
        
        [Tooltip("Selection mode for click selection")]
        [SerializeField] private SelectionMode selectionMode = SelectionMode.ShiftModifier;
        
        [Tooltip("Layer mask for selectable objects")]
        [SerializeField] private LayerMask selectableLayer = ~0;
        
        [Tooltip("Tag for selectable objects (empty = all)")]
        [SerializeField] private string selectableTag = "Selectable";
        
        [Header("Command Settings")]
        [Tooltip("Mouse button for commands (reserved for future use)")]
        #pragma warning disable CS0414 // Field is assigned but never used
        [SerializeField] private int commandButton = 1; // Right click
        #pragma warning restore CS0414
        
        [Tooltip("Layer mask for command targets (ground, enemies, etc.)")]
        [SerializeField] private LayerMask commandLayer = ~0;
        
        [Tooltip("Double-click time window for double-click actions")]
        [SerializeField] private float doubleClickTime = 0.3f;
        
        [Header("Selection Box Visual")]
        [Tooltip("Selection box border color")]
        [SerializeField] private Color boxBorderColor = new Color(0.2f, 0.8f, 0.2f, 1f);
        
        [Tooltip("Selection box fill color")]
        [SerializeField] private Color boxFillColor = new Color(0.2f, 0.8f, 0.2f, 0.2f);
        
        [Tooltip("Selection box border width")]
        [SerializeField] private float boxBorderWidth = 2f;
        
        [Header("Events")]
        [Tooltip("Called when selection changes")]
        public SelectionEvent OnSelectionChanged;
        
        [Tooltip("Called when a move command is issued")]
        public CommandEvent OnMoveCommand;
        
        [Tooltip("Called when an attack command is issued on a target")]
        public TargetCommandEvent OnTargetCommand;
        
        [Tooltip("Called when double-clicking a unit (e.g., select all of same type)")]
        public TargetCommandEvent OnDoubleClickUnit;
        
        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = false;
        
        #endregion
        
        #region Private Fields
        
        // Selection state
        private bool _isSelecting;
        private Vector2 _selectionStartPos;
        private Vector2 _selectionCurrentPos;
        private bool _isDragging;
        
        // Click tracking for double-click
        private float _lastClickTime;
        private GameObject _lastClickedObject;
        
        // Current selection
        private GameObject[] _currentSelection = new GameObject[0];
        
        // Cached
        private Camera _camera;
        
        // Selection box texture
        private Texture2D _boxTexture;
        private Texture2D _borderTexture;
        
        #endregion
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            _camera = Camera.main;
            CreateSelectionBoxTextures();
        }
        
        private void Update()
        {
            if (Mouse.current == null) return;
            
            ProcessSelection();
            ProcessCommands();
        }
        
        private void OnGUI()
        {
            if (_isDragging && enableBoxSelection)
            {
                DrawSelectionBox();
            }
            
            if (showDebugInfo)
            {
                DrawDebugInfo();
            }
        }
        
        private void OnDestroy()
        {
            if (_boxTexture != null) Destroy(_boxTexture);
            if (_borderTexture != null) Destroy(_borderTexture);
        }
        
        #endregion
        
        #region Selection Processing
        
        private void ProcessSelection()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            
            bool selectionPressed = mouse.leftButton.isPressed;
            bool selectionDown = mouse.leftButton.wasPressedThisFrame;
            bool selectionUp = mouse.leftButton.wasReleasedThisFrame;
            
            Vector2 mousePos = mouse.position.ReadValue();
            
            // Start selection
            if (selectionDown)
            {
                _isSelecting = true;
                _selectionStartPos = mousePos;
                _selectionCurrentPos = mousePos;
                _isDragging = false;
            }
            
            // Update selection
            if (_isSelecting && selectionPressed)
            {
                _selectionCurrentPos = mousePos;
                
                // Check if we should start box selection
                if (!_isDragging)
                {
                    float dragDist = Vector2.Distance(_selectionStartPos, _selectionCurrentPos);
                    if (dragDist >= minBoxSelectionDrag)
                    {
                        _isDragging = true;
                    }
                }
            }
            
            // End selection
            if (selectionUp && _isSelecting)
            {
                _isSelecting = false;
                
                if (_isDragging && enableBoxSelection)
                {
                    // Box selection
                    PerformBoxSelection(keyboard);
                }
                else
                {
                    // Click selection
                    PerformClickSelection(keyboard);
                }
                
                _isDragging = false;
            }
        }
        
        private void PerformClickSelection(Keyboard keyboard)
        {
            Ray ray = _camera.ScreenPointToRay(_selectionStartPos);
            
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, selectableLayer))
            {
                GameObject hitObject = hit.collider.gameObject;
                
                // Check tag if specified
                if (!string.IsNullOrEmpty(selectableTag) && !hitObject.CompareTag(selectableTag))
                {
                    // Clicked on non-selectable - clear selection
                    ClearSelection();
                    return;
                }
                
                // Check if this unit is player-controlled
                if (!IsPlayerControlled(hitObject))
                {
                    // Clicked on non-player-controlled unit - don't select, but don't clear either
                    // This allows clicking enemy units without losing selection
                    if (showDebugInfo)
                    {
                        Debug.Log($"[RTSMouseController] Unit '{hitObject.name}' is not player-controlled, skipping selection");
                    }
                    return;
                }
                
                // Check for double-click
                float timeSinceLastClick = Time.unscaledTime - _lastClickTime;
                if (timeSinceLastClick < doubleClickTime && _lastClickedObject == hitObject)
                {
                    // Double-click detected
                    OnDoubleClickUnit?.Invoke(hitObject);
                    _lastClickTime = 0f;
                    return;
                }
                
                _lastClickTime = Time.unscaledTime;
                _lastClickedObject = hitObject;
                
                // Handle selection based on mode
                bool shiftHeld = keyboard != null && 
                    (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
                bool ctrlHeld = keyboard != null && 
                    (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
                
                switch (selectionMode)
                {
                    case SelectionMode.SingleClick:
                        SetSelection(new[] { hitObject });
                        break;
                        
                    case SelectionMode.ShiftModifier:
                        if (shiftHeld)
                        {
                            ToggleSelection(hitObject);
                        }
                        else
                        {
                            SetSelection(new[] { hitObject });
                        }
                        break;
                        
                    case SelectionMode.CtrlShiftModifier:
                        if (ctrlHeld)
                        {
                            AddToSelection(hitObject);
                        }
                        else if (shiftHeld)
                        {
                            RemoveFromSelection(hitObject);
                        }
                        else
                        {
                            SetSelection(new[] { hitObject });
                        }
                        break;
                }
            }
            else
            {
                // Clicked on nothing - clear selection (unless shift/ctrl held)
                bool shiftHeld = keyboard != null && 
                    (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
                bool ctrlHeld = keyboard != null && 
                    (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
                
                if (!shiftHeld && !ctrlHeld)
                {
                    ClearSelection();
                }
            }
        }
        
        private void PerformBoxSelection(Keyboard keyboard)
        {
            // Get box corners in screen space
            Vector2 min = Vector2.Min(_selectionStartPos, _selectionCurrentPos);
            Vector2 max = Vector2.Max(_selectionStartPos, _selectionCurrentPos);
            
            // Find all selectable objects in the scene
            var selectables = FindSelectableObjects();
            var inBox = new System.Collections.Generic.List<GameObject>();
            
            foreach (var obj in selectables)
            {
                Vector3 screenPos = _camera.WorldToScreenPoint(obj.transform.position);
                
                // Check if behind camera
                if (screenPos.z < 0) continue;
                
                // Check if in box
                if (screenPos.x >= min.x && screenPos.x <= max.x &&
                    screenPos.y >= min.y && screenPos.y <= max.y)
                {
                    inBox.Add(obj);
                }
            }
            
            // Handle selection based on modifiers
            bool shiftHeld = keyboard != null && 
                (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            bool ctrlHeld = keyboard != null && 
                (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
            
            if (shiftHeld || ctrlHeld)
            {
                // Add to current selection
                var newSelection = new System.Collections.Generic.List<GameObject>(_currentSelection);
                foreach (var obj in inBox)
                {
                    if (!newSelection.Contains(obj))
                    {
                        newSelection.Add(obj);
                    }
                }
                SetSelection(newSelection.ToArray());
            }
            else
            {
                // Replace selection
                SetSelection(inBox.ToArray());
            }
        }
        
        private GameObject[] FindSelectableObjects()
        {
            var result = new System.Collections.Generic.List<GameObject>();
            
            if (string.IsNullOrEmpty(selectableTag))
            {
                // Find all objects on selectable layer
                var allObjects = FindObjectsByType<Collider>(FindObjectsSortMode.None);
                foreach (var collider in allObjects)
                {
                    if (((1 << collider.gameObject.layer) & selectableLayer) != 0)
                    {
                        result.Add(collider.gameObject);
                    }
                }
            }
            else
            {
                // Find all objects with tag
                var tagged = GameObject.FindGameObjectsWithTag(selectableTag);
                result.AddRange(tagged);
            }
            
            return result.ToArray();
        }
        
        #endregion
        
        #region Command Processing
        
        private void ProcessCommands()
        {
            var mouse = Mouse.current;
            
            if (mouse.rightButton.wasPressedThisFrame)
            {
                Ray ray = _camera.ScreenPointToRay(mouse.position.ReadValue());
                
                if (Physics.Raycast(ray, out RaycastHit hit, 1000f, commandLayer))
                {
                    // Check if we hit a targetable object
                    if (IsTargetable(hit.collider.gameObject))
                    {
                        OnTargetCommand?.Invoke(hit.collider.gameObject);
                    }
                    else
                    {
                        // Ground click - move command
                        OnMoveCommand?.Invoke(hit.point);
                    }
                }
            }
        }
        
        private bool IsTargetable(GameObject obj)
        {
            // Override this or add your own logic
            // For example, check if it's an enemy, resource, etc.
            return obj.CompareTag("Enemy") || obj.CompareTag("Resource");
        }
        
        #endregion
        
        #region Selection Management
        
        /// <summary>
        /// Checks if a GameObject belongs to a player-controlled faction.
        /// Returns true if no factions are configured (allowing all), or if the object's faction is in the list.
        /// </summary>
        private bool IsPlayerControlled(GameObject obj)
        {
            if (obj == null) return false;
            
            // If no factions are selected, allow all
            if (!playerControlledFactions.HasAnyFaction)
            {
                return true;
            }
            
            // Try to find the agent in the crowd controller
            var controller = EnemyMassesCrowdController.Instance;
            if (controller != null)
            {
                var agent = controller.GetAgentByGameObject(obj);
                if (agent != null && agent.enemyCrowd != null)
                {
                    return playerControlledFactions.ContainsFaction(agent.enemyCrowd.factionName);
                }
            }
            
            // If we can't determine the faction, allow selection (backward compatibility)
            return true;
        }
        
        /// <summary>
        /// Filters an array of GameObjects to only include player-controlled units
        /// </summary>
        private GameObject[] FilterPlayerControlled(GameObject[] objects)
        {
            if (objects == null || objects.Length == 0) return objects;
            if (!playerControlledFactions.HasAnyFaction) return objects; // No filtering if no factions set
            
            var filtered = new System.Collections.Generic.List<GameObject>();
            foreach (var obj in objects)
            {
                if (IsPlayerControlled(obj))
                {
                    filtered.Add(obj);
                }
            }
            return filtered.ToArray();
        }
        
        public void SetSelection(GameObject[] objects)
        {
            // Filter to only player-controlled units
            _currentSelection = FilterPlayerControlled(objects ?? new GameObject[0]);
            OnSelectionChanged?.Invoke(_currentSelection);
        }
        
        public void ClearSelection()
        {
            SetSelection(new GameObject[0]);
        }
        
        public void AddToSelection(GameObject obj)
        {
            if (obj == null) return;
            if (!IsPlayerControlled(obj)) return; // Only add player-controlled units
            if (System.Array.IndexOf(_currentSelection, obj) >= 0) return;
            
            var newSelection = new System.Collections.Generic.List<GameObject>(_currentSelection);
            newSelection.Add(obj);
            _currentSelection = newSelection.ToArray();
            OnSelectionChanged?.Invoke(_currentSelection);
        }
        
        public void RemoveFromSelection(GameObject obj)
        {
            if (obj == null) return;
            
            var newSelection = new System.Collections.Generic.List<GameObject>(_currentSelection);
            newSelection.Remove(obj);
            SetSelection(newSelection.ToArray());
        }
        
        public void ToggleSelection(GameObject obj)
        {
            if (System.Array.IndexOf(_currentSelection, obj) >= 0)
            {
                RemoveFromSelection(obj);
            }
            else
            {
                AddToSelection(obj);
            }
        }
        
        public GameObject[] GetSelection()
        {
            return _currentSelection;
        }
        
        public bool IsSelected(GameObject obj)
        {
            return System.Array.IndexOf(_currentSelection, obj) >= 0;
        }
        
        #endregion
        
        #region Faction Control API
        
        /// <summary>
        /// Gets the FactionMask containing player-controlled factions
        /// </summary>
        public FactionMask PlayerControlledFactions => playerControlledFactions;
        
        /// <summary>
        /// Sets which factions can be controlled by the player
        /// </summary>
        public void SetPlayerControlledFactions(params string[] factionNames)
        {
            playerControlledFactions.Clear();
            foreach (var name in factionNames)
            {
                playerControlledFactions.AddFaction(name);
            }
            
            // Re-filter current selection to remove any non-player-controlled units
            if (_currentSelection.Length > 0)
            {
                var filtered = FilterPlayerControlled(_currentSelection);
                if (filtered.Length != _currentSelection.Length)
                {
                    _currentSelection = filtered;
                    OnSelectionChanged?.Invoke(_currentSelection);
                }
            }
        }
        
        /// <summary>
        /// Adds a faction to the player-controlled list
        /// </summary>
        public void AddPlayerControlledFaction(string factionName)
        {
            playerControlledFactions.AddFaction(factionName);
        }
        
        /// <summary>
        /// Removes a faction from the player-controlled list
        /// </summary>
        public void RemovePlayerControlledFaction(string factionName)
        {
            playerControlledFactions.RemoveFaction(factionName);
            
            // Re-filter current selection
            if (_currentSelection.Length > 0)
            {
                var filtered = FilterPlayerControlled(_currentSelection);
                if (filtered.Length != _currentSelection.Length)
                {
                    _currentSelection = filtered;
                    OnSelectionChanged?.Invoke(_currentSelection);
                }
            }
        }
        
        /// <summary>
        /// Clears all player-controlled factions (allows all factions to be controlled)
        /// </summary>
        public void ClearPlayerControlledFactions()
        {
            playerControlledFactions.Clear();
        }
        
        /// <summary>
        /// Checks if a specific faction is player-controlled
        /// </summary>
        public bool IsFactionPlayerControlled(string factionName)
        {
            // If no factions are set, all are controllable
            if (!playerControlledFactions.HasAnyFaction) return true;
            return playerControlledFactions.ContainsFaction(factionName);
        }
        
        #endregion
        
        #region Selection Box Drawing
        
        private void CreateSelectionBoxTextures()
        {
            // Create fill texture
            _boxTexture = new Texture2D(1, 1);
            _boxTexture.SetPixel(0, 0, Color.white);
            _boxTexture.Apply();
            
            // Create border texture
            _borderTexture = new Texture2D(1, 1);
            _borderTexture.SetPixel(0, 0, Color.white);
            _borderTexture.Apply();
        }
        
        private void DrawSelectionBox()
        {
            // Convert to GUI coordinates (Y is flipped)
            Vector2 start = new Vector2(_selectionStartPos.x, Screen.height - _selectionStartPos.y);
            Vector2 end = new Vector2(_selectionCurrentPos.x, Screen.height - _selectionCurrentPos.y);
            
            // Calculate rect
            Vector2 min = Vector2.Min(start, end);
            Vector2 max = Vector2.Max(start, end);
            Rect rect = new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
            
            // Draw fill
            GUI.color = boxFillColor;
            GUI.DrawTexture(rect, _boxTexture);
            
            // Draw border
            GUI.color = boxBorderColor;
            
            // Top
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, boxBorderWidth), _borderTexture);
            // Bottom
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - boxBorderWidth, rect.width, boxBorderWidth), _borderTexture);
            // Left
            GUI.DrawTexture(new Rect(rect.x, rect.y, boxBorderWidth, rect.height), _borderTexture);
            // Right
            GUI.DrawTexture(new Rect(rect.xMax - boxBorderWidth, rect.y, boxBorderWidth, rect.height), _borderTexture);
            
            GUI.color = Color.white;
        }
        
        #endregion
        
        #region Debug
        
        private void DrawDebugInfo()
        {
            GUILayout.BeginArea(new Rect(10, 10, 300, 250));
            GUILayout.BeginVertical("box");
            
            GUILayout.Label($"Selection: {_currentSelection.Length} objects");
            GUILayout.Label($"Is Selecting: {_isSelecting}");
            GUILayout.Label($"Is Dragging: {_isDragging}");
            GUILayout.Label($"Player Factions: {playerControlledFactions}");
            
            if (_currentSelection.Length > 0)
            {
                GUILayout.Label("Selected:");
                for (int i = 0; i < Mathf.Min(_currentSelection.Length, 5); i++)
                {
                    if (_currentSelection[i] != null)
                    {
                        GUILayout.Label($"  - {_currentSelection[i].name}");
                    }
                }
                if (_currentSelection.Length > 5)
                {
                    GUILayout.Label($"  ... and {_currentSelection.Length - 5} more");
                }
            }
            
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
        
        #endregion
    }
}
