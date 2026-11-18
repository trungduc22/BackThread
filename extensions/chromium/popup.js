const HOST_NAME = "app.backthread.host";

let currentTab = { url: "", title: "" };

function sendNative(payload) {
  return new Promise((resolve, reject) => {
    chrome.runtime.sendNativeMessage(HOST_NAME, payload, (response) => {
      if (chrome.runtime.lastError) {
        reject(new Error(chrome.runtime.lastError.message));
      } else {
        resolve(response);
      }
    });
  });
}

async function init() {
  const hostBadge = document.getElementById("hostBadge");
  const titleEl = document.getElementById("tabTitle");
  const urlEl = document.getElementById("tabUrl");
  const reasonEl = document.getElementById("capReason");
  const statusEl = document.getElementById("status");
  const sendBtn = document.getElementById("sendBtn");

  const tabs = await chrome.tabs.query({ active: true, currentWindow: true });
  const tab = tabs[0];
  currentTab = { url: tab?.url || "", title: tab?.title || "Browser Tab" };
  titleEl.textContent = currentTab.title;
  urlEl.textContent = currentTab.url || "(no URL)";

  try {
    const status = await sendNative({ action: "status" });
    hostBadge.textContent = status?.ok && status.data?.state ? status.data.state : "offline";
    if (!status?.ok) statusEl.textContent = "BackThread Host unavailable.";
  } catch (err) {
    hostBadge.textContent = "unpaired";
    statusEl.textContent = "Native messaging host not registered or BackThread offline: " + err.message;
    sendBtn.disabled = true;
    return;
  }

  if (currentTab.url.startsWith("https://")) {
    try {
      const cap = await sendNative({ action: "inspect", url: currentTab.url, title: currentTab.title, mode: "Auto" });
      if (cap?.ok && cap.data) {
        const c = cap.data.capabilities || {};
        reasonEl.textContent = `${cap.data.selectedMode || "Auto"}: ${c.reason || "Ready"}`;
      }
    } catch (err) {
      reasonEl.textContent = "Inspection warning: " + err.message;
    }
  } else {
    reasonEl.textContent = "Only HTTPS URLs are eligible for independent handoff; Live PCM covers any Windows tab.";
  }
}

document.getElementById("sendBtn").addEventListener("click", async () => {
  const mode = document.getElementById("mode").value;
  const statusEl = document.getElementById("status");
  statusEl.textContent = "Submitting handoff…";
  try {
    const res = await sendNative({ action: "submitMedia", url: currentTab.url, title: currentTab.title, mode });
    if (res?.ok && res.data) {
      statusEl.textContent = `Owner: ${res.data.owner} · Stage: ${res.data.stage}${res.data.error ? " — " + res.data.error : ""}`;
    } else {
      statusEl.textContent = res?.error || JSON.stringify(res);
    }
  } catch (err) {
    statusEl.textContent = "Error: " + err.message;
  }
});

document.getElementById("returnBtn").addEventListener("click", async () => {
  const statusEl = document.getElementById("status");
  try {
    const res = await sendNative({ action: "returnToWindows", resumeWindows: true });
    statusEl.textContent = res?.ok ? "Returned playback control to Windows." : (res?.error || "Failed");
  } catch (err) {
    statusEl.textContent = "Error: " + err.message;
  }
});

init();
