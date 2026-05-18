using System;
using UnityEngine;

namespace DaggerfallWorkshop.Sim
{
    /// Phase 2 demonstrator: a read-only HUD overlay driven *entirely* from sim
    /// registries. Compare it against DFU's own HUD — if values match in real
    /// time, the Phase 1 mirror is correct.
    ///
    /// Drawn top-right. Toggle with F10.
    public sealed class SimHUD : MonoBehaviour
    {
        [SerializeField] bool show = true;
        [SerializeField] KeyCode toggleKey = KeyCode.F10;

        const int BarChars = 16;

        void Update()
        {
            if (Input.GetKeyDown(toggleKey)) show = !show;
        }

        void OnGUI()
        {
            if (!show) return;
            var driver = SimDriver.Instance;
            if (driver == null) return;
            var ctx = driver.Context;

            var playerId = ctx.Identity.PlayerId;
            var clock = ctx.WorldClock.Current;

            const int w = 250;
            int h = playerId.IsNone ? 60 : 130;
            int x = Screen.width - w - 10;
            int y = 10;

            GUI.Box(new Rect(x, y, w, h), "Sim HUD (F10)");

            GUI.Label(new Rect(x + 10, y + 22, w - 20, 18),
                "clock " + clock.Year + "-" + clock.Month.ToString("00") + "-" + clock.Day.ToString("00") +
                " " + clock.Hour.ToString("00") + ":" + clock.Minute.ToString("00"));

            if (playerId.IsNone)
            {
                GUI.Label(new Rect(x + 10, y + 40, w - 20, 18), "(no player entity)");
                return;
            }

            string name = "?";
            if (ctx.Identity.TryGet(playerId, out var ident) && !string.IsNullOrEmpty(ident.Name))
                name = ident.Name;
            GUI.Label(new Rect(x + 10, y + 40, w - 20, 18), "player " + playerId.Value + "  " + name);

            if (ctx.Vitals.TryGet(playerId, out var vit))
            {
                GUI.Label(new Rect(x + 10, y + 58, w - 20, 18), "HP " + Bar(vit.CurrentHealth, vit.MaxHealth));
                GUI.Label(new Rect(x + 10, y + 76, w - 20, 18), "MP " + Bar(vit.CurrentMagicka, vit.MaxMagicka));
                GUI.Label(new Rect(x + 10, y + 94, w - 20, 18), "FT " + Bar(vit.CurrentFatigue, vit.MaxFatigue));
            }
        }

        static string Bar(int value, int max)
        {
            if (max <= 0) return value + "/" + max;
            double frac = (double)value / max;
            if (frac < 0) frac = 0;
            if (frac > 1) frac = 1;
            int filled = (int)Math.Round(frac * BarChars);
            return "[" + new string('#', filled) + new string('-', BarChars - filled) + "] " + value + "/" + max;
        }
    }
}
