using System.Collections.Generic;
using UnityEngine;

namespace DaggerfallWorkshop.Sim
{
    /// Runtime IMGUI debug window for poking at sim registries. Toggle with F9.
    ///
    /// Phase 1 view: world clock + entity table (id, kind, name, position, vitals).
    /// Add tabs / filters as more registries land.
    public sealed class SimInspector : MonoBehaviour
    {
        [SerializeField] KeyCode toggleKey = KeyCode.F9;
        bool _open;
        Vector2 _scroll;
        Vector2 _eventScroll;
        Rect _window = new Rect(20, 60, 720, 600);

        void Update()
        {
            if (Input.GetKeyDown(toggleKey)) _open = !_open;
        }

        void OnGUI()
        {
            if (!_open) return;
            _window = GUI.Window(0xD33B07, _window, DrawWindow, "Sim Inspector (F9)");
        }

        void DrawWindow(int id)
        {
            var driver = SimDriver.Instance;
            if (driver == null)
            {
                GUI.Label(new Rect(10, 25, 700, 20), "(SimDriver not running)");
                GUI.DragWindow();
                return;
            }
            var ctx = driver.Context;

            GUILayout.BeginVertical();

            // --- WorldClock ---
            var clock = ctx.WorldClock.Current;
            GUILayout.Label(
                "WorldClock: " + clock.Year + "-" + clock.Month.ToString("00") + "-" + clock.Day.ToString("00") +
                " " + clock.Hour.ToString("00") + ":" + clock.Minute.ToString("00") + ":" + clock.Second.ToString("00") +
                "  scale=" + clock.TimeScale.ToString("F1"));

            var weather = ctx.Weather.Current;
            GUILayout.Label("Weather: " + weather.Kind +
                (weather.IsRaining  ? "  rain"  : "") +
                (weather.IsStorming ? "  storm" : "") +
                (weather.IsSnowing  ? "  snow"  : "") +
                (weather.IsOvercast ? "  overcast" : ""));

            GUILayout.Space(6);
            GUILayout.Label("Entities: " + ctx.Identity.Count);

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Width(700), GUILayout.Height(400));
            GUILayout.BeginHorizontal();
            GUILayout.Label("id", GUILayout.Width(60));
            GUILayout.Label("kind", GUILayout.Width(100));
            GUILayout.Label("name", GUILayout.Width(140));
            GUILayout.Label("pos", GUILayout.Width(200));
            GUILayout.Label("hp / mag / fat", GUILayout.Width(180));
            GUILayout.EndHorizontal();

            // Snapshot ids to a list so we can sort.
            var ids = new List<EntityId>(ctx.Identity.Ids);
            ids.Sort((a, b) => a.Value.CompareTo(b.Value));

            for (int i = 0; i < ids.Count; i++)
            {
                var eid = ids[i];
                if (!ctx.Identity.TryGet(eid, out var ident)) continue;
                ctx.Position.TryGet(eid, out var pos);
                ctx.Vitals.TryGet(eid, out var vit);

                GUILayout.BeginHorizontal();
                GUILayout.Label(eid.Value.ToString(), GUILayout.Width(60));
                GUILayout.Label(ident.Kind.ToString(), GUILayout.Width(100));
                GUILayout.Label(string.IsNullOrEmpty(ident.Name) ? "(unnamed)" : ident.Name, GUILayout.Width(140));
                GUILayout.Label(pos != null ? pos.ToString() : "-", GUILayout.Width(200));
                GUILayout.Label(vit != null
                    ? (vit.CurrentHealth + "/" + vit.MaxHealth +
                       " · " + vit.CurrentMagicka + "/" + vit.MaxMagicka +
                       " · " + vit.CurrentFatigue + "/" + vit.MaxFatigue +
                       (vit.IsDead ? " DEAD" : ""))
                    : "-",
                    GUILayout.Width(180));
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();

            // --- Recent sim events ---
            GUILayout.Space(8);
            GUILayout.Label("Recent events:");
            _eventScroll = GUILayout.BeginScrollView(_eventScroll, GUILayout.Width(700), GUILayout.Height(120));
            if (driver.EventLog != null)
            {
                var events = driver.EventLog.Snapshot();
                for (int i = 0; i < events.Count; i++)
                {
                    var e = events[i];
                    GUILayout.Label("tick " + e.Tick.ToString().PadLeft(6) + "  t=" + e.SimSeconds.ToString("F1") + "s  " + e.Summary);
                }
            }
            GUILayout.EndScrollView();

            GUILayout.EndVertical();

            GUI.DragWindow();
        }
    }
}
