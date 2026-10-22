using UnityEngine;
using UnityEngine.EventSystems;

namespace Arawn.EnemyMasses.Runtime.UI
{
    /// <summary>
    /// Lightweight hover trigger that notifies <see cref="RTSSelectionCommandPanel"/>
    /// when the pointer enters or exits a command button.
    /// Attach to each button/toggle in the RTS Command Panel.
    /// </summary>
    [AddComponentMenu("")]
    public class RTSCommandTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>The key that identifies which tooltip entry to show (e.g. "offensive", "stop").</summary>
        [HideInInspector] public string tooltipKey;

        /// <summary>Back-reference to the owning panel.</summary>
        [HideInInspector] public RTSSelectionCommandPanel panel;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (panel != null)
                panel.ShowCommandTooltip(tooltipKey, gameObject);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (panel != null)
                panel.HideCommandTooltip(tooltipKey);
        }

        private void OnDisable()
        {
            if (panel != null)
                panel.HideCommandTooltip(tooltipKey);
        }
    }
}
