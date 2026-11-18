#!/usr/bin/env python3
"""Render deterministic UI design mockups and architecture diagrams for docs/images/.

All iPhone 6 UI panels reflect exact BTTheme colors and Auto Layout proportions
from src/iOS/UI/*.m at 667x375 pt (1334x750 @2x px) landscape geometry and carry
an explicit 'RENDERED DESIGN MOCKUP' disclosure label (not physical device photos).
"""
import argparse
import pathlib
import shutil
import subprocess
import sys
import tempfile


COMMON_CSS = """
:root {
  --canvas: #0B0E14;
  --surface: #141A24;
  --elevated: #1C2432;
  --pill: #252F40;
  --hairline: #2A3649;
  --teal: #3DD6C6;
  --blue: #5298FF;
  --amber: #F5A623;
  --green: #36D37C;
  --red: #F05A66;
  --text-primary: #F4F7FB;
  --text-secondary: #9AA6B8;
  --text-muted: #69758A;
}
* { box-sizing: border-box; margin: 0; padding: 0; }
body {
  background: #07090D;
  color: var(--text-primary);
  font-family: -apple-system, BlinkMacSystemFont, "SF Pro Text", "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  padding: 20px 24px;
}
.phone-frame {
  width: 1000px;
  height: 562px; /* Exact 16:9 / 667x375 pt scaled 1.5x cleanly */
  background: var(--canvas);
  border: 2px solid #2C3648;
  border-radius: 18px;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  position: relative;
  box-shadow: 0 24px 60px rgba(0,0,0,0.65);
}
.mockup-banner {
  margin-top: 10px;
  font-size: 12px;
  color: var(--text-secondary);
  letter-spacing: 0.03em;
  display: flex;
  gap: 12px;
  align-items: center;
}
.mockup-tag {
  background: rgba(245, 166, 35, 0.14);
  color: var(--amber);
  border: 1px solid rgba(245, 166, 35, 0.35);
  border-radius: 6px;
  padding: 2px 8px;
  font-weight: 700;
  font-size: 11px;
}
.topbar {
  height: 48px;
  background: var(--surface);
  border-bottom: 1px solid var(--hairline);
  padding: 0 16px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex-shrink: 0;
}
.status-row { display: flex; align-items: center; gap: 8px; font-size: 13px; font-weight: 600; }
.dot { width: 10px; height: 10px; border-radius: 50%; display: inline-block; }
.seg {
  display: flex;
  border: 1px solid var(--teal);
  border-radius: 8px;
  overflow: hidden;
}
.seg span {
  padding: 5px 12px;
  font-size: 12px;
  font-weight: 600;
  color: var(--teal);
  border-right: 1px solid rgba(61, 214, 198, 0.35);
}
.seg span:last-child { border-right: none; }
.seg span.active { background: var(--teal); color: var(--canvas); font-weight: 700; }
.card {
  background: var(--surface);
  border: 1px solid var(--hairline);
  border-radius: 14px;
  padding: 14px;
}
.chip {
  display: inline-block;
  border-radius: 7px;
  padding: 4px 10px;
  font-size: 12px;
  font-weight: 700;
}
.chip-teal { color: var(--teal); background: rgba(61,214,198,0.16); }
.chip-blue { color: var(--blue); background: rgba(82,152,255,0.16); }
.chip-amber { color: var(--amber); background: rgba(245,166,35,0.16); }
.chip-green { color: var(--green); background: rgba(54,211,124,0.16); }
.chip-red { color: var(--red); background: rgba(240,90,102,0.18); }
.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: 40px;
  padding: 8px 14px;
  border-radius: 10px;
  font-size: 13px;
  font-weight: 600;
}
.btn-primary { background: var(--teal); color: var(--canvas); font-weight: 700; }
.btn-pill { background: var(--pill); color: var(--text-primary); border: 1px solid var(--hairline); }
.slider {
  width: 100%;
  height: 5px;
  background: var(--pill);
  border-radius: 3px;
  position: relative;
}
.slider-fill {
  height: 100%;
  background: var(--teal);
  border-radius: 3px;
  position: relative;
}
.slider-knob {
  width: 16px;
  height: 16px;
  border-radius: 50%;
  background: #FFFFFF;
  position: absolute;
  right: -8px;
  top: -5.5px;
  box-shadow: 0 2px 6px rgba(0,0,0,0.45);
}
"""


def wrap_doc(body_html: str, banner_text: str, extra_css: str = "") -> str:
    return f"""<!DOCTYPE html>
<html><head><meta charset="utf-8"><style>{COMMON_CSS}{extra_css}</style></head>
<body>
{body_html}
<div class="mockup-banner"><span class="mockup-tag">RENDERED DESIGN MOCKUP</span><span>{banner_text} · Built from BTTheme &amp; UIKit layout specs (not a live physical hardware capture)</span></div>
</body></html>"""


def topbar(active: str, conn_text: str = "USB Connected", dot_color: str = "var(--green)") -> str:
    tabs = ["Hub", "Media", "PiP", "Actions", "Jobs", "History", "Setup"]
    seg_html = "".join(
        f'<span class="{"active" if t == active else ""}">{t}</span>' for t in tabs
    )
    return f"""
    <div class="topbar">
      <div class="status-row"><span class="dot" style="background:{dot_color}"></span><span>{conn_text}</span></div>
      <div class="seg">{seg_html}</div>
    </div>
    <div style="height:34px;background:var(--elevated);border-bottom:1px solid var(--hairline);padding:0 14px;display:flex;align-items:center;gap:12px;font-size:12px;font-variant-numeric:tabular-nums;flex-shrink:0;">
      <span style="font-weight:800;color:var(--teal);letter-spacing:0.03em;">16:08:42</span>
      <span style="font-weight:600;color:var(--text-primary);">iPhone 84%+ &middot; Wi-Fi On</span>
      <span style="color:var(--text-secondary);">PC CPU 28% &middot; RAM 6.4/16.0G</span>
      <span style="color:var(--text-secondary);">PC 88% AC &middot; Wi-Fi On</span>
      <span style="flex:1;"></span>
      <span class="chip chip-amber" style="padding:2px 8px;font-size:11px;">BT: On (0 conn)</span>
      <span class="chip chip-blue" style="padding:2px 8px;font-size:11px;">Out: Both</span>
    </div>"""


def equalizer_bars_html() -> str:
    return """
    <span style="display:inline-flex;align-items:flex-end;gap:2.5px;height:14px;">
      <span style="width:3px;height:9px;background:var(--teal);border-radius:1.5px;"></span>
      <span style="width:3px;height:14px;background:var(--teal);border-radius:1.5px;"></span>
      <span style="width:3px;height:7px;background:var(--teal);border-radius:1.5px;"></span>
      <span style="width:3px;height:12px;background:var(--teal);border-radius:1.5px;"></span>
    </span>"""


def build_now_playing_html() -> str:
    body = f"""
    <div class="phone-frame">
      {topbar("Media", "USB Connected", "var(--green)")}
      <div style="flex:1;padding:12px 14px;display:flex;gap:14px;align-items:flex-start;">
        <div class="card" style="width:340px;display:flex;flex-direction:column;gap:8px;padding:12px;">
          <div style="width:100%;aspect-ratio:16/9;background:linear-gradient(135deg,#102636,#1A4B5C 60%,#132B3A);border-radius:10px;position:relative;overflow:hidden;display:flex;align-items:center;justify-content:center;border:1px solid #2A3D52;">
            <svg width="240" height="110" viewBox="0 0 260 120" opacity="0.85">
              <defs><linearGradient id="g1" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#3DD6C6" stop-opacity="0.3"/><stop offset="1" stop-color="#5298FF" stop-opacity="0.1"/></linearGradient></defs>
              <rect x="8" y="10" width="244" height="100" rx="8" fill="url(#g1)" stroke="#3DD6C6" stroke-opacity="0.4"/>
              <polygon points="118,44 118,76 146,60" fill="#3DD6C6"/>
            </svg>
            <div style="position:absolute;bottom:8px;right:8px;background:rgba(10,12,18,0.82);padding:4px 9px;border-radius:7px;font-size:11px;font-weight:700;border:1px solid var(--hairline);">Full PiP</div>
          </div>
          <div style="display:flex;align-items:center;justify-content:space-between;gap:8px;">
            <span class="chip chip-teal">Mode A &middot; Direct Wi-Fi</span>
            {equalizer_bars_html()}
          </div>
          <div style="font-size:14px;font-weight:600;color:var(--text-secondary);">Independent on iPhone</div>
          <div style="font-size:12px;color:var(--text-muted);">DirectInternet &middot; Buffered 18s</div>
          <div class="btn btn-primary" style="width:100%;min-height:36px;">Open Video PiP</div>
          <div class="btn btn-pill" style="width:100%;min-height:36px;">Return to Windows</div>
        </div>

        <div class="card" style="flex:1;display:flex;flex-direction:column;gap:8px;padding:12px 14px;">
          <div style="font-size:20px;font-weight:700;line-height:1.18;">Clean Architecture Deep Dive (1080p HLS)</div>
          <div style="font-size:14px;font-weight:500;color:var(--text-secondary);">Independent iPhone AVPlayer &middot; Windows Paused</div>
          <div style="display:flex;align-items:center;gap:10px;">
            <div class="slider" style="flex:1;"><div class="slider-fill" style="width:42%;"><div class="slider-knob"></div></div></div>
            <span style="font-size:12px;color:var(--text-muted);font-variant-numeric:tabular-nums;">09:42 / 23:10</span>
          </div>
          <div style="display:grid;grid-template-columns:1fr 1fr 1fr;gap:8px;">
            <div class="btn btn-pill" style="min-height:34px;color:var(--text-muted);opacity:0.55;">Prev</div>
            <div class="btn btn-primary" style="min-height:34px;">Pause</div>
            <div class="btn btn-pill" style="min-height:34px;color:var(--text-muted);opacity:0.55;">Next</div>
          </div>
          <div class="seg" style="width:100%;">
            <span style="flex:1;text-align:center;">Only PC</span>
            <span style="flex:1;text-align:center;">Only Phone</span>
            <span class="active" style="flex:1;text-align:center;">Both</span>
          </div>
          <div style="background:var(--elevated);border:1px solid var(--hairline);border-radius:10px;padding:7px 10px;display:flex;align-items:center;gap:10px;">
            <span class="btn btn-pill" style="min-height:28px;padding:3px 10px;font-size:12px;font-variant-numeric:tabular-nums;border-color:var(--teal);color:var(--teal);">PC Vol: 72%</span>
            <div class="slider" style="flex:1;"><div class="slider-fill" style="width:72%;"><div class="slider-knob"></div></div></div>
            <span class="chip chip-blue" style="font-size:11px;">Route</span>
          </div>
          <div style="display:grid;grid-template-columns:1fr 1fr 1fr;gap:8px;">
            <div class="btn btn-pill" style="min-height:34px;">Focus: Auto</div>
            <div class="btn btn-pill" style="min-height:34px;">Cap: 720p</div>
            <div class="btn btn-pill" style="min-height:34px;">Awake: On</div>
          </div>
        </div>
      </div>
    </div>"""
    return wrap_doc(body, "BTNowPlayingView · Studio Media + Clock/Telemetry Strip + 3 Audio Modes + Inline Volume · 667×375 pt")


def build_pip_focus_html() -> str:
    body = f"""
    <div class="phone-frame" style="background:#000000;">
      <div style="position:absolute;inset:0;background:radial-gradient(circle at 35% 40%, #1F4E5F 0%, #0E1F2D 52%, #070B11 100%);display:flex;align-items:center;justify-content:center;">
        <svg width="880" height="420" viewBox="0 0 880 420" opacity="0.55">
          <path d="M 40 310 Q 220 120 440 240 T 840 160" fill="none" stroke="#3DD6C6" stroke-width="3"/>
          <path d="M 40 340 Q 260 180 480 280 T 840 210" fill="none" stroke="#5298FF" stroke-width="2" stroke-dasharray="6 6"/>
          <circle cx="440" cy="240" r="36" fill="rgba(61,214,198,0.14)" stroke="#3DD6C6" stroke-width="2"/>
          <polygon points="432,226 432,254 456,240" fill="#F4F7FB"/>
        </svg>
      </div>

      <!-- Top HUD with Live Clock & PC Telemetry -->
      <div style="position:relative;z-index:2;margin:14px 16px 0;background:rgba(15,18,26,0.86);border:1px solid var(--hairline);border-radius:14px;padding:8px 12px;display:flex;align-items:center;gap:10px;">
        <span class="chip chip-teal">Mode A &middot; Wi-Fi</span>
        {equalizer_bars_html()}
        <span style="font-size:12px;font-weight:700;color:var(--teal);font-variant-numeric:tabular-nums;">16:08:42 &middot; PC 28% / 6.4G</span>
        <span style="flex:1;font-size:14px;font-weight:600;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;">Distributed Systems Lecture 04 — Raft Consensus</span>
        <span class="btn btn-pill" style="min-height:34px;">Fill</span>
        <span class="btn btn-pill" style="min-height:34px;">To Windows</span>
        <span class="btn btn-pill" style="min-height:34px;">Studio</span>
      </div>

      <div style="flex:1;position:relative;z-index:2;display:flex;align-items:flex-end;justify-content:flex-end;padding:0 24px 12px;">
        <div style="background:rgba(15,18,26,0.74);border:1px dashed rgba(61,214,198,0.45);border-radius:10px;padding:7px 12px;font-size:12px;color:var(--text-secondary);">
          HUD auto-hides after 4s &middot; Top notifications never pause or cover full screen &middot; 3pt Ambient corner badge
        </div>
      </div>

      <!-- Bottom HUD with inline volume + 3-mode output cycle -->
      <div style="position:relative;z-index:2;margin:0 16px 14px;background:rgba(15,18,26,0.86);border:1px solid var(--hairline);border-radius:14px;padding:8px 14px;display:flex;align-items:center;gap:12px;">
        <span class="btn btn-primary" style="min-width:80px;min-height:34px;">Pause</span>
        <div class="slider" style="flex:1.1;"><div class="slider-fill" style="width:54%;"><div class="slider-knob"></div></div></div>
        <span style="font-size:12px;color:var(--text-secondary);font-variant-numeric:tabular-nums;">24:18 / 45:00</span>
        <span class="btn btn-pill" style="min-height:32px;padding:4px 9px;font-size:11px;border-color:var(--blue);color:var(--blue);">Out: Both</span>
        <span style="font-size:11px;font-weight:700;color:var(--teal);font-variant-numeric:tabular-nums;">AVPlayer 88%</span>
        <div class="slider" style="width:110px;"><div class="slider-fill" style="width:88%;"><div class="slider-knob"></div></div></div>
        <span class="btn btn-pill" style="min-height:32px;">Ambient</span>
      </div>
    </div>"""
    return wrap_doc(body, "BTVideoSurfaceView · External PiP HUD with Clock/Telemetry, 3-Mode Output & Inline Volume · 667×375 pt")


def build_idle_hub_html() -> str:
    body = f"""
    <div class="phone-frame">
      {topbar("Hub", "USB Connected · Ready", "var(--green)")}
      <div style="flex:1;padding:14px;display:grid;grid-template-columns:1fr 1fr 1fr;gap:14px;align-items:start;">
        <div class="card" style="display:flex;flex-direction:column;gap:9px;">
          <div style="display:flex;align-items:center;justify-content:space-between;">
            <span style="font-size:12px;font-weight:700;color:var(--teal);letter-spacing:0.04em;">COMPANION &amp; AUDIO</span>
            {equalizer_bars_html()}
          </div>
          <div style="font-size:18px;font-weight:600;">USB Connected</div>
          <div style="font-size:13px;color:var(--text-secondary);">PCM Ring: 1440 frames &middot; Drop 0 B</div>
          <div style="font-size:13px;color:var(--text-muted);">Output: Both &middot; PC Vol 72% &middot; Live PCM</div>
          <div class="btn btn-pill" style="width:100%;margin-top:6px;">Open Studio Media</div>
        </div>

        <div class="card" style="display:flex;flex-direction:column;gap:8px;">
          <div style="font-size:12px;font-weight:700;color:var(--blue);letter-spacing:0.04em;">QUICK ACTIONS</div>
          <div class="btn btn-pill" style="width:100%;background:var(--elevated);">Play / Pause</div>
          <div class="btn btn-pill" style="width:100%;background:var(--elevated);">Toggle Mic Mute</div>
          <div class="btn btn-pill" style="width:100%;background:var(--elevated);">Audio: Only Phone</div>
          <div class="btn btn-pill" style="width:100%;margin-top:4px;">All Actions</div>
        </div>

        <div class="card" style="display:flex;flex-direction:column;gap:9px;">
          <div style="font-size:12px;font-weight:700;color:var(--amber);letter-spacing:0.04em;">WORKER &amp; NOTIFICATIONS</div>
          <div style="font-size:13px;font-weight:600;color:var(--text-secondary);">build-release.zip &middot; Completed (100%)</div>
          <div style="font-size:13px;color:var(--text-muted);line-height:1.45;">Latest Windows Toast: Build Succeeded<br>0 errors, 42/42 checks passing</div>
          <div class="btn btn-pill" style="width:100%;margin-top:8px;">Jobs &amp; History</div>
        </div>
      </div>
    </div>"""
    return wrap_doc(body, "BTIdleHubView · Three-Column Hub with Live Clock & Telemetry Strip · 667×375 pt Landscape")


def build_notifications_html() -> str:
    extra = """
    body { padding: 20px; }
    .split { display:flex; gap:18px; align-items:stretch; }
    .mini-phone {
      width: 667px;
      height: 375px;
      background: var(--canvas);
      border: 2px solid #2C3648;
      border-radius: 14px;
      position: relative;
      overflow: hidden;
      display: flex;
      flex-direction: column;
      box-shadow: 0 18px 42px rgba(0,0,0,0.6);
    }
    """
    body = """
    <div class="split">
      <!-- Left: Priority 2 Spotlight Top Bar -->
      <div class="mini-phone">
        <div style="position:absolute;inset:0;background:radial-gradient(circle at 40% 50%,#1B4353,#0A1520);display:flex;align-items:flex-end;justify-content:center;padding-bottom:68px;color:rgba(244,247,251,0.52);font-size:15px;font-weight:600;">
          AVPlayer External PiP Video Continues Playing Unobstructed Underneath
        </div>
        <div style="position:relative;z-index:3;margin:10px auto 0;width:540px;background:rgba(17,33,41,0.98);border:1.5px solid var(--teal);border-radius:14px;overflow:hidden;box-shadow:0 12px 30px rgba(0,0,0,0.7);display:flex;">
          <div style="width:4px;background:var(--teal);flex-shrink:0;"></div>
          <div style="flex:1;padding:10px 14px;display:flex;flex-direction:column;gap:5px;">
            <div style="display:flex;align-items:center;gap:8px;">
              <div style="width:28px;height:28px;border-radius:6px;background:var(--elevated);display:flex;align-items:center;justify-content:center;font-weight:700;color:var(--teal);font-size:11px;">CI</div>
              <span style="flex:1;font-size:12px;font-weight:600;color:var(--text-secondary);">GitHub Actions &middot; Windows Toast</span>
              <span class="chip chip-teal" style="font-size:10px;padding:2px 7px;">SPOTLIGHT</span>
              <span class="chip chip-amber" style="font-size:10px;padding:2px 7px;">PRIORITY</span>
              <span class="btn btn-pill" style="min-height:26px;padding:2px 10px;font-size:11px;">Dismiss</span>
            </div>
            <div style="font-size:15px;font-weight:700;">Workflow backthread-ci succeeded</div>
            <div style="font-size:12px;color:var(--text-secondary);">42/42 host tests &amp; 23 ARM64 units clean &middot; taps pass through below bar</div>
            <div style="width:100%;height:3px;background:var(--pill);border-radius:2px;margin-top:2px;overflow:hidden;">
              <div style="width:65%;height:100%;background:var(--teal);"></div>
            </div>
          </div>
        </div>
      </div>

      <!-- Right: Priority 3 Beacon Critical Top Bar (Zero Fullscreen Modal) -->
      <div class="mini-phone">
        <div style="position:absolute;inset:0;background:radial-gradient(circle at 40% 50%,#1B4353,#0A1520);display:flex;align-items:flex-end;justify-content:center;padding-bottom:68px;color:rgba(244,247,251,0.52);font-size:15px;font-weight:600;">
          Video Remains Visible &amp; Touch-Interactive Below Top Bar (No Fullscreen Modal)
        </div>
        <div style="position:relative;z-index:3;margin:10px auto 0;width:600px;background:rgba(37,17,22,0.98);border:1.5px solid var(--red);border-radius:14px;overflow:hidden;box-shadow:0 14px 34px rgba(240,90,102,0.28);display:flex;">
          <div style="width:5px;background:var(--red);flex-shrink:0;"></div>
          <div style="flex:1;padding:10px 14px;display:flex;flex-direction:column;gap:5px;">
            <div style="display:flex;align-items:center;gap:8px;">
              <div style="width:32px;height:32px;border-radius:7px;background:var(--elevated);display:flex;align-items:center;justify-content:center;font-weight:700;color:var(--red);font-size:11px;">WIN</div>
              <span style="flex:1;font-size:12px;font-weight:600;color:var(--text-secondary);">Microsoft Teams &middot; Real Windows Toast</span>
              <span class="chip chip-red" style="font-size:10px;padding:2px 7px;">BEACON</span>
              <span class="chip chip-red" style="font-size:10px;padding:2px 7px;">CRITICAL BAR</span>
              <span class="btn btn-primary" style="min-height:28px;padding:3px 12px;font-size:11px;background:var(--red);color:#FFFFFF;">Dismiss</span>
            </div>
            <div style="font-size:16px;font-weight:700;">Production Incident Call Incoming</div>
            <div style="font-size:12px;color:var(--text-secondary);">Compact beacon strip with countdown bar; respects Reduce Motion &amp; never tears down AVPlayerLayer.</div>
            <div style="width:100%;height:3px;background:var(--pill);border-radius:2px;margin-top:2px;overflow:hidden;">
              <div style="width:82%;height:100%;background:var(--red);"></div>
            </div>
          </div>
        </div>
      </div>
    </div>"""
    return wrap_doc(body, "BTNotificationOverlayView (0.2.0 Top-Bar-Only) · Left: Spotlight Priority Bar · Right: Beacon Critical Bar (Zero Fullscreen Modal)", extra)


def build_actions_worker_html() -> str:
    extra = """
    .split { display:flex; gap:18px; align-items:stretch; }
    .mini-phone {
      width: 667px;
      height: 375px;
      background: var(--canvas);
      border: 2px solid #2C3648;
      border-radius: 14px;
      overflow: hidden;
      display: flex;
      flex-direction: column;
      box-shadow: 0 18px 42px rgba(0,0,0,0.6);
    }
    """
    body = f"""
    <div class="split">
      <div class="mini-phone">
        {topbar("Actions", "USB Connected", "var(--green)")}
        <div style="padding:14px;display:flex;flex-direction:column;gap:10px;">
          <div style="font-size:15px;font-weight:700;">Windows Quick Actions (Allowlisted)</div>
          <div style="font-size:12px;font-weight:600;color:var(--teal);">Tap any action to trigger on Windows (no remote shell input)</div>
          <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-top:4px;">
            <div class="btn btn-pill" style="min-height:50px;background:var(--surface);">Play / Pause Media</div>
            <div class="btn btn-pill" style="min-height:50px;background:var(--surface);">Toggle Mic Mute</div>
            <div class="btn btn-pill" style="min-height:50px;background:var(--surface);">Build Solution</div>
            <div class="btn btn-pill" style="min-height:50px;background:var(--surface);">Open Logs Folder</div>
          </div>
        </div>
      </div>

      <div class="mini-phone">
        {topbar("Jobs", "USB Connected", "var(--green)")}
        <div style="padding:14px;">
          <div class="card" style="display:flex;flex-direction:column;gap:11px;">
            <div style="display:flex;justify-content:space-between;align-items:center;">
              <span style="font-size:12px;font-weight:700;color:var(--blue);letter-spacing:0.04em;">COMPANION WORKER &amp; USB BUFFER</span>
              <span class="chip chip-green">Memory Normal</span>
            </div>
            <div style="font-size:18px;font-weight:600;">Job verify-artifact-09 · Running</div>
            <div style="width:100%;height:6px;background:var(--pill);border-radius:3px;overflow:hidden;">
              <div style="width:74%;height:100%;background:var(--teal);"></div>
            </div>
            <div style="font-size:13px;color:var(--text-secondary);">74% · SHA-256 verification &amp; text metrics executing on bounded A8 utility queue</div>
            <div style="font-size:12px;color:var(--text-muted);">Mode B USB Stream 9a24f8c1 · 18.40 / 24.00 MiB</div>
            <div class="btn btn-pill" style="width:100%;">Cancel active job</div>
          </div>
        </div>
      </div>
    </div>"""
    return wrap_doc(body, "Left: BTQuickActionsView (2-Column Allowlist) · Right: BTWorkerStatusView (Bounded Job + Mode B USB Buffer)", extra)


def build_architecture_html() -> str:
    extra = """
    body { padding: 18px 24px; }
    .canvas-card {
      width: 1280px;
      height: 700px;
      background: var(--canvas);
      border: 2px solid var(--hairline);
      border-radius: 16px;
      padding: 20px;
      display: flex;
      flex-direction: column;
      gap: 14px;
    }
    """
    body = """
    <div class="canvas-card">
      <div style="display:flex;justify-content:space-between;align-items:center;">
        <div>
          <div style="font-size:20px;font-weight:700;">BackThread 0.2.0 Hybrid Media, Telemetry &amp; Audio Architecture</div>
          <div style="font-size:13px;color:var(--text-secondary);margin-top:2px;">Windows Host (.NET 8) &harr; Lightning USB (usbmux :27015 / BTH1 7-Channel Wire) &harr; iPhone 6 (iOS 12.5.8) + Independent Wi-Fi</div>
        </div>
        <span class="chip chip-teal">3-Mode Audio &middot; Strict Telemetry &middot; Top-Bar Alerts</span>
      </div>

      <div style="flex:1;display:grid;grid-template-columns:1.15fr 0.72fr 1.15fr;gap:14px;align-items:stretch;">
        <!-- Windows Side -->
        <div class="card" style="display:flex;flex-direction:column;gap:9px;border-color:#2D4B6E;">
          <div style="font-size:13px;font-weight:700;color:var(--blue);letter-spacing:0.04em;">WINDOWS HOST (x64 &middot; 19045)</div>
          <div style="background:var(--elevated);border:1px solid var(--hairline);border-radius:10px;padding:10px;">
            <div style="font-size:13px;font-weight:700;">MediaRouter &amp; 3-Mode Audio Routing</div>
            <div style="font-size:12px;color:var(--text-secondary);margin-top:3px;">Output: PcOnly / PhoneOnly (save/restore PC mute) / Both<br>4 Media Modes: DirectInternet (A) &middot; BufferedUsb (B) &middot; LivePcm (C) &middot; RemoteControl (D)</div>
          </div>
          <div style="display:grid;grid-template-columns:1fr 1fr;gap:8px;">
            <div style="background:var(--elevated);border:1px solid var(--hairline);border-radius:10px;padding:9px;">
              <div style="font-size:12px;font-weight:700;color:var(--green);">LoopbackBridge &amp; MMDevice</div>
              <div style="font-size:11px;color:var(--text-secondary);">WASAPI 48kHz + signal status + Endpoint scalar volume</div>
            </div>
            <div style="background:var(--elevated);border:1px solid var(--hairline);border-radius:10px;padding:9px;">
              <div style="font-size:12px;font-weight:700;color:var(--amber);">SystemTelemetryProvider</div>
              <div style="font-size:11px;color:var(--text-secondary);">CPU, RAM, AC/Bat, Wi-Fi, strict BT Aep.IsConnected (2s)</div>
            </div>
          </div>
          <div style="background:var(--elevated);border:1px solid var(--hairline);border-radius:10px;padding:10px;">
            <div style="font-size:12px;font-weight:700;color:var(--teal);">LocalApi (:23452) &amp; Chromium NMH</div>
            <div style="font-size:11px;color:var(--text-secondary);">/v1/telemetry, /v1/audio/*, /v1/notifications/diagnostics &middot; Bearer + Origin check</div>
          </div>
          <div style="background:var(--elevated);border:1px solid var(--hairline);border-radius:10px;padding:9px;">
            <div style="font-size:12px;font-weight:700;color:var(--red);">NotificationCollector &amp; MSIX Option</div>
            <div style="font-size:11px;color:var(--text-secondary);">UserNotificationListener + Build-Msix.ps1 + privacy masking + style routing</div>
          </div>
        </div>

        <!-- USB Transport Spine -->
        <div class="card" style="display:flex;flex-direction:column;justify-content:space-between;background:var(--elevated);border-color:var(--teal);">
          <div>
            <div style="font-size:13px;font-weight:700;color:var(--teal);letter-spacing:0.04em;">USB TRANSPORT</div>
            <div style="font-size:11px;color:var(--text-secondary);margin-top:3px;">Apple usbmux :27015 &rarr; iPhone loopback :23451<br>HMAC-SHA256 mutual nonce challenge</div>
          </div>
          <div style="display:flex;flex-direction:column;gap:6px;font-size:12px;">
            <div style="background:var(--surface);border:1px solid var(--hairline);border-radius:8px;padding:7px 9px;"><strong style="color:var(--teal);">Ch 1 &middot; Control</strong> &mdash; Audio mode, volume, actions</div>
            <div style="background:var(--surface);border:1px solid var(--hairline);border-radius:8px;padding:7px 9px;"><strong style="color:var(--green);">Ch 2 &middot; Audio</strong> &mdash; 10ms PCM16 stereo (drop-oldest)</div>
            <div style="background:var(--surface);border:1px solid var(--hairline);border-radius:8px;padding:7px 9px;"><strong style="color:var(--blue);">Ch 3 &middot; Media</strong> &mdash; State, handoff, player volume, sync</div>
            <div style="background:var(--surface);border:1px solid var(--hairline);border-radius:8px;padding:7px 9px;"><strong style="color:var(--amber);">Ch 4 &middot; Notify</strong> &mdash; Top-bar style, beacon, icon, expiry</div>
            <div style="background:var(--surface);border:1px solid var(--hairline);border-radius:8px;padding:7px 9px;"><strong>Ch 5 &middot; Jobs</strong> &mdash; SHA256, verify, text, metadata</div>
            <div style="background:var(--surface);border:1px solid var(--hairline);border-radius:8px;padding:7px 9px;"><strong>Ch 6 &middot; Files</strong> &mdash; Async worker uploads + Mode B chunks</div>
            <div style="background:var(--surface);border:1px solid var(--hairline);border-radius:8px;padding:7px 9px;"><strong style="color:var(--red);">Ch 7 &middot; Diag</strong> &mdash; Stats, audio.error, system.telemetry</div>
          </div>
          <div style="font-size:11px;color:var(--text-muted);text-align:center;">Offline disconnect restores saved PC mute state automatically</div>
        </div>

        <!-- iPhone Side -->
        <div class="card" style="display:flex;flex-direction:column;gap:9px;border-color:#2C6E67;">
          <div style="font-size:13px;font-weight:700;color:var(--teal);letter-spacing:0.04em;">iPHONE 6 COMPANION (ARM64 &middot; iOS 12.5.8)</div>
          <div style="background:var(--elevated);border:1px solid var(--teal);border-radius:10px;padding:10px;">
            <div style="font-size:13px;font-weight:700;">Independent PiP + Playback Volume Control</div>
            <div style="font-size:12px;color:var(--text-secondary);margin-top:3px;"><code>BTDirectMediaPlayer</code> + persistent <code>AVPlayerLayer</code><br>Inline PC Vol / App Gain / AVPlayer slider + <code>BTEqualizerView</code></div>
          </div>
          <div style="display:grid;grid-template-columns:1fr 1fr;gap:8px;">
            <div style="background:var(--elevated);border:1px solid var(--hairline);border-radius:10px;padding:9px;">
              <div style="font-size:12px;font-weight:700;color:var(--green);">BTAgent &amp; AudioQueue</div>
              <div style="font-size:11px;color:var(--text-secondary);">Serialized audio control lock + 19,200 B ring</div>
            </div>
            <div style="background:var(--elevated);border:1px solid var(--hairline);border-radius:10px;padding:9px;">
              <div style="font-size:12px;font-weight:700;color:var(--amber);">BTTelemetryStripView</div>
              <div style="font-size:11px;color:var(--text-secondary);">1s live Clock + public iOS battery/Wi-Fi + PC strip</div>
            </div>
          </div>
          <div style="background:var(--elevated);border:1px solid var(--hairline);border-radius:10px;padding:10px;">
            <div style="font-size:12px;font-weight:700;">Modular Landscape-First UIKit Shell (667&times;375 pt)</div>
            <div style="font-size:11px;color:var(--text-secondary);">Hub &middot; Media &middot; PiP / Ambient &middot; Actions &middot; Jobs &middot; History &middot; Setup</div>
          </div>
          <div style="background:var(--elevated);border:1px solid var(--hairline);border-radius:10px;padding:9px;">
            <div style="font-size:12px;font-weight:700;color:var(--red);">BTNotificationOverlayView (Top-Bar-Only)</div>
            <div style="font-size:11px;color:var(--text-secondary);">Compact Spotlight &amp; Beacon top bars; pass-through touch &amp; Reduce Motion aware</div>
          </div>
        </div>
      </div>
    </div>"""
    return wrap_doc(body, "BackThread 0.2.0 System, Audio Routing & Telemetry Diagram", extra)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", default="docs/images")
    args = parser.parse_args()

    chrome = shutil.which("google-chrome") or shutil.which("chromium") or shutil.which("chromium-browser")
    if not chrome:
        raise SystemExit("Headless Chrome/Chromium is required to render previews")

    out_dir = pathlib.Path(args.out).resolve()
    out_dir.mkdir(parents=True, exist_ok=True)

    specs = [
        ("ui-now-playing-mode-a.png", build_now_playing_html(), "1120,680"),
        ("ui-external-pip-focus.png", build_pip_focus_html(), "1120,680"),
        ("ui-idle-hub.png", build_idle_hub_html(), "1120,680"),
        ("ui-notifications-p2-p3.png", build_notifications_html(), "1420,490"),
        ("ui-quick-actions-worker.png", build_actions_worker_html(), "1420,490"),
        ("hybrid-media-architecture.png", build_architecture_html(), "1360,800"),
    ]

    with tempfile.TemporaryDirectory() as tmp:
        tmp_dir = pathlib.Path(tmp)
        for name, html, win_size in specs:
            html_path = tmp_dir / f"{name}.html"
            png_path = out_dir / name
            html_path.write_text(html, encoding="utf-8")
            subprocess.run(
                [
                    chrome,
                    "--headless",
                    "--no-sandbox",
                    "--disable-gpu",
                    "--hide-scrollbars",
                    f"--window-size={win_size}",
                    "--force-device-scale-factor=1.5",
                    f"--screenshot={png_path}",
                    html_path.as_uri(),
                ],
                check=True,
                capture_output=True,
                text=True,
            )
            print(f"Rendered {png_path} ({png_path.stat().st_size} bytes)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
