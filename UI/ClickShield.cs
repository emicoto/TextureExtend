// ClaudeNote: 0.7.3 点击不穿透（Lune 2026-11-17：框架的窗口开着时，别不小心点到背后的东西）。
//   为什么普通的“挡板”不够：游戏里的物品、格子不是靠 Unity 的按钮点击，而是每帧自己向整个界面发一次射线、
//     在**全部**命中里找物品（RenderHandler / CustomEventHandler 里的 EventSystem.RaycastAll），前面盖着窗口也照样找得到后面的。
//   做法：鼠标在我们的窗口范围内时，把别的画布的射线接收器（BaseRaycaster）临时关掉——那次射线就只打得到我们自己的窗口；
//     鼠标移出窗口、或窗口关掉，立刻原样打开。只关当时开着的那些，关了谁记着谁，绝不留下关着的。
//   两个细节：鼠标键按着的时候不切换（正拖着物品经过窗口时不打断拖动）；我们窗口所在的那块画布不关（窗口自己要能点）。
//   每帧的开销：一次“鼠标在不在矩形里”的判断；只有进出窗口的那一下才去找全场的接收器。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ProbablyExtendFramework
{
    internal sealed class ClickShield : MonoBehaviour
    {
        private static readonly List<ClickShield> live = new List<ClickShield>();
        private static readonly List<BaseRaycaster> switchedOff = new List<BaseRaycaster>();
        private static bool blocking;
        private RectTransform area;
        private Canvas canvas;

        /// <summary>给一个窗口（它的矩形范围）装上。重复装无害。</summary>
        internal static void Guard(RectTransform window)
        {
            if (window == null || window.GetComponent<ClickShield>() != null) return;
            window.gameObject.AddComponent<ClickShield>().area = window;
        }

        /// <summary>停用 mod 时：不管还有没有窗口，先把关掉的都打开。</summary>
        internal static void ReleaseAll() { Release(); live.Clear(); }

        private void OnEnable()
        {
            if (area == null) area = transform as RectTransform;
            Canvas c = GetComponentInParent<Canvas>();
            canvas = c != null ? c.rootCanvas : null;
            live.Add(this);
        }

        private void OnDisable()
        {
            live.Remove(this);
            if (live.Count == 0 || blocking) { Release(); }   // 窗口关了：马上放开（还有别的窗口的话，下一帧按鼠标位置重新判断）
        }

        private void Update()
        {
            if (live.Count == 0 || live[0] != this) return;   // 多个窗口同时开着时只让第一个来判断
            if (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2)) return;   // 按着键（拖动中）不切换
            bool over = false;
            Vector2 mouse = Input.mousePosition;
            foreach (ClickShield s in live)
                if (s.area != null && RectTransformUtility.RectangleContainsScreenPoint(s.area, mouse, s.Camera())) { over = true; break; }
            if (over == blocking) return;
            if (over) Block(); else Release();
        }

        private Camera Camera() { return canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera; }

        private static void Block()
        {
            blocking = true;
            foreach (BaseRaycaster r in FindObjectsOfType<BaseRaycaster>())
            {
                if (r == null || !r.enabled || Ours(r)) continue;
                r.enabled = false;
                switchedOff.Add(r);
            }
        }

        private static void Release()
        {
            blocking = false;
            foreach (BaseRaycaster r in switchedOff) if (r != null) r.enabled = true;
            switchedOff.Clear();
        }

        // 这个接收器是不是在我们某个窗口所在的画布上
        private static bool Ours(BaseRaycaster r)
        {
            Canvas c = r.GetComponentInParent<Canvas>();
            Canvas root = c != null ? c.rootCanvas : null;
            if (root == null) return false;
            foreach (ClickShield s in live) if (s.canvas == root) return true;
            return false;
        }
    }
}
