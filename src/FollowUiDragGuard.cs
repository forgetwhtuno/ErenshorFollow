using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ErenshorFollow
{
    // Standalone-safe retained-uGUI drag ownership. Follow never creates an EventSystem. Ownership begins
    // on LEFT pointer-down (before Unity's drag threshold), reasserts while held, and participates in the
    // suite's process-local BCL-only ownership registry without a SuiteHub assembly dependency. The final
    // participating mod owner restores the native pre-gesture GameData.DraggingUIElement value.
    internal sealed class FollowUiDragGuard : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerUpHandler, IPointerDownHandler
    {
        private const string ProcessOwnersKey = "forgetwhtuno.erenshor.ui.drag.owners.v1";
        private const string ProcessBaselineKey = "forgetwhtuno.erenshor.ui.drag.nativeBaseline.v1";
        private const string ProcessBaselineCapturedKey = "forgetwhtuno.erenshor.ui.drag.nativeBaselineCaptured.v1";
        private const string ProcessOwner = "forgetwhtuno.erenshor.follow";

        private static int _owned;
        private static int _ownershipEpoch;

        internal RectTransform Target;
        internal Action Completed;
        internal Action Activated;
        private RectTransform _parent;
        private Vector2 _startPointer, _startPosition;
        private bool _dragging, _owning;
        private int _ownerEpoch;

        internal static bool OwnsPointerGesture { get { return _owned > 0; } }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left) return;
            try { if (Activated != null) Activated(); } catch { }
            AcquireOwnership();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left) return;
            if (Target == null) Target = GetComponent<RectTransform>();
            _parent = Target == null ? null : Target.parent as RectTransform;
            if (_parent == null) return;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent, eventData.position, eventData.pressEventCamera, out local)) return;
            _startPointer = local;
            _startPosition = Target.anchoredPosition;
            _dragging = true;
            AcquireOwnership();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left || !_dragging || _parent == null || Target == null) return;
            ReassertOwnership();
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent, eventData.position, eventData.pressEventCamera, out local)) return;
            Target.anchoredPosition = _startPosition + (local - _startPointer);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left) return;
            End(true);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left) return;
            // Pointer-up may be delivered before OnEndDrag. Complete/persist the gesture here if a drag
            // actually occurred; the later EndDrag callback then becomes an idempotent no-op.
            End(true);
        }

        private void Update()
        {
            if (!_owning) return;
            ReassertOwnership();
            if (!Input.GetMouseButton(0)) End(_dragging);
        }

        private void OnApplicationFocus(bool focus) { if (!focus) End(false); }
        private void OnApplicationPause(bool paused) { if (paused) End(false); }
        private void OnDisable() { End(false); }
        private void OnDestroy() { End(false); }

        private void AcquireOwnership()
        {
            // ForceRelease invalidates every outstanding local token. A stale component callback after a
            // scene/lifecycle reset must not decrement the new epoch's owner count.
            if (_owning && _ownerEpoch != _ownershipEpoch) _owning = false;
            if (!_owning)
            {
                bool firstLocalOwner = _owned == 0;
                _owning = true;
                _ownerEpoch = _ownershipEpoch;
                _owned++;
                if (firstLocalOwner) AcquireProcessOwnership();
            }
            ReassertOwnership();
        }

        private void ReassertOwnership()
        {
            if (!_owning || _ownerEpoch != _ownershipEpoch) return;
            try { GameData.DraggingUIElement = true; } catch { }
        }

        private void End(bool notify)
        {
            bool completed = _dragging;
            _dragging = false;
            _parent = null;
            if (_owning)
            {
                bool currentEpoch = _ownerEpoch == _ownershipEpoch;
                _owning = false;
                if (currentEpoch)
                {
                    _owned--;
                    if (_owned < 0) _owned = 0;
                    if (_owned == 0) ReleaseProcessOwnership();
                }
            }
            if (notify && completed) { try { if (Completed != null) Completed(); } catch { } }
        }

        internal static void ForceReleaseIfOwned()
        {
            bool hadLocalOwner = _owned > 0;
            _owned = 0;
            _ownershipEpoch++;
            if (_ownershipEpoch < 0) _ownershipEpoch = 1;
            if (hadLocalOwner || ProcessContainsOwner()) ReleaseProcessOwnership();
        }

        private static void AcquireProcessOwnership()
        {
            HashSet<string> owners = GetProcessOwners(true);
            if (owners == null) return;
            lock (owners)
            {
                if (owners.Count == 0)
                {
                    bool baseline = false;
                    try { baseline = GameData.DraggingUIElement; } catch { }
                    AppDomain.CurrentDomain.SetData(ProcessBaselineKey, baseline);
                    AppDomain.CurrentDomain.SetData(ProcessBaselineCapturedKey, true);
                }
                owners.Add(ProcessOwner);
            }
            try { GameData.DraggingUIElement = true; } catch { }
        }

        private static void ReleaseProcessOwnership()
        {
            HashSet<string> owners = GetProcessOwners(false);
            if (owners == null)
            {
                RestoreProcessBaselineIfCaptured();
                return;
            }

            bool lastOwner;
            lock (owners)
            {
                owners.Remove(ProcessOwner);
                lastOwner = owners.Count == 0;
            }
            if (lastOwner) RestoreProcessBaselineIfCaptured();
            else { try { GameData.DraggingUIElement = true; } catch { } }
        }

        private static bool ProcessContainsOwner()
        {
            HashSet<string> owners = GetProcessOwners(false);
            if (owners == null) return false;
            lock (owners) { return owners.Contains(ProcessOwner); }
        }

        private static HashSet<string> GetProcessOwners(bool create)
        {
            try
            {
                HashSet<string> owners = AppDomain.CurrentDomain.GetData(ProcessOwnersKey) as HashSet<string>;
                if (owners == null && create)
                {
                    owners = new HashSet<string>(StringComparer.Ordinal);
                    AppDomain.CurrentDomain.SetData(ProcessOwnersKey, owners);
                }
                return owners;
            }
            catch { return null; }
        }

        private static void RestoreProcessBaselineIfCaptured()
        {
            bool captured = false;
            bool baseline = false;
            try
            {
                object capturedValue = AppDomain.CurrentDomain.GetData(ProcessBaselineCapturedKey);
                captured = capturedValue is bool && (bool)capturedValue;
                object baselineValue = AppDomain.CurrentDomain.GetData(ProcessBaselineKey);
                baseline = baselineValue is bool && (bool)baselineValue;
                if (captured) GameData.DraggingUIElement = baseline;
                AppDomain.CurrentDomain.SetData(ProcessBaselineCapturedKey, false);
                AppDomain.CurrentDomain.SetData(ProcessBaselineKey, false);
            }
            catch { }
        }
    }
}
