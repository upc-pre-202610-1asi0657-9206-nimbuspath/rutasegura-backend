"use strict";

const settings = globalThis.RutaSeguraFcmConfig;
const generateButton = document.getElementById("generate");
const tokenBox = document.getElementById("token");
const copyButton = document.getElementById("copy");
const statusBox = document.getElementById("status");
const vapidInput = document.getElementById("vapid");
let messaging;
let foregroundUnsubscribe;

document.getElementById("project").textContent = settings.firebase.projectId;
vapidInput.value = settings.vapidPublicKey;

function status(message, state = "") {
  statusBox.textContent = message;
  statusBox.dataset.state = state;
}

function showReceived(payload, foreground) {
  document.getElementById("empty")?.remove();
  const row = document.createElement("li");
  const id = typeof payload?.data?.notificationId === "string" ? payload.data.notificationId.slice(0, 100) : "";
  row.textContent = `${new Date().toLocaleTimeString()} · Mensaje recibido ${foreground ? "con la página al frente" : "en segundo plano"}${id ? " · Aviso " + id : ""}.`;
  document.getElementById("messages").prepend(row);
  while (document.getElementById("messages").children.length > 20) document.getElementById("messages").lastElementChild.remove();
}

function validPublicKey(value) {
  try {
    if (!/^[A-Za-z0-9_-]{86,88}$/.test(value)) return false;
    const padded = value.replace(/-/g, "+").replace(/_/g, "/").padEnd(Math.ceil(value.length / 4) * 4, "=");
    const bytes = atob(padded);
    return bytes.length === 65 && bytes.charCodeAt(0) === 4;
  } catch { return false; }
}

function explain(error) {
  const code = error?.code;
  if (code === "messaging/permission-blocked") return "El navegador bloqueó el permiso. Abre los permisos del sitio junto a la dirección, permite notificaciones y vuelve a intentarlo.";
  if (code === "messaging/unsupported-browser") return "Este navegador no permite Firebase Messaging. Prueba Chrome o Edge en una ventana normal.";
  if (code === "messaging/failed-service-worker-registration" || code === "worker-timeout") return "No pudo iniciarse el receptor de mensajes. Comprueba tu conexión, recarga la página y vuelve a intentarlo.";
  if (code === "messaging/token-subscribe-failed") return "Firebase rechazó el registro. Comprueba la clave VAPID y que FCM Registration API esté habilitada para rutasegura-f2703.";
  const knownCode = typeof code === "string" && /^messaging\/[a-z-]{1,80}$/.test(code) ? ` (${code})` : "";
  return `No se pudo obtener el token${knownCode}. Comprueba tu conexión o los bloqueadores del navegador y vuelve a intentarlo.`;
}

generateButton.addEventListener("click", async () => {
  tokenBox.value = "";
  copyButton.disabled = true;
  if (!window.isSecureContext || !["localhost", "127.0.0.1"].includes(location.hostname)) {
    status("Abre la página con la dirección local que muestra el script, no como un archivo HTML.", "error"); return;
  }
  if (!("Notification" in window) || !("serviceWorker" in navigator) || !("PushManager" in window)) {
    status("Este navegador no admite la prueba. Usa Chrome o Edge en una ventana normal.", "error"); return;
  }
  const vapidKey = vapidInput.value.trim();
  if (!validPublicKey(vapidKey)) { status("La clave pública VAPID no tiene el formato esperado. Cópiala completa desde Cloud Messaging.", "error"); return; }
  if (Notification.permission === "denied") { status(explain({ code: "messaging/permission-blocked" }), "error"); return; }
  generateButton.disabled = true;
  try {
    // El permiso se solicita dentro del click, antes de cargar archivos externos. Nunca automaticamente al abrir.
    status("Acepta «Permitir» en el aviso del navegador.");
    if (await Notification.requestPermission() !== "granted") {
      status("No se concedió el permiso. Cuando quieras, pulsa Obtener token para intentarlo de nuevo.", "error"); return;
    }
    status("Preparando el navegador y solicitando el token a Firebase…");
    const [appSdk, fcmSdk] = await Promise.all([
      import(`https://www.gstatic.com/firebasejs/${settings.sdkVersion}/firebase-app.js`),
      import(`https://www.gstatic.com/firebasejs/${settings.sdkVersion}/firebase-messaging.js`)
    ]);
    if (!await fcmSdk.isSupported()) throw { code: "messaging/unsupported-browser" };
    if (!messaging) {
      const name = "rutasegura-fcm-prueba";
      const app = appSdk.getApps().find(item => item.name === name) ?? appSdk.initializeApp(settings.firebase, name);
      messaging = fcmSdk.getMessaging(app);
      foregroundUnsubscribe = fcmSdk.onMessage(messaging, payload => showReceived(payload, true));
    }
    const registration = await navigator.serviceWorker.register("/firebase-messaging-sw.js", { scope: "/" });
    let timeout;
    try {
      await Promise.race([
        navigator.serviceWorker.ready,
        new Promise((_, reject) => { timeout = setTimeout(() => reject({ code: "worker-timeout" }), 20000); })
      ]);
    } finally { clearTimeout(timeout); }
    // El backend actual usa registration tokens. No mezclar con register()/FID sin migrar su contrato.
    const token = await fcmSdk.getToken(messaging, { vapidKey, serviceWorkerRegistration: registration });
    if (!token) throw new Error("Token vacio");
    tokenBox.value = token;
    copyButton.disabled = false;
    status("Token generado. Ya puedes copiarlo y usarlo como destino de una notificación de prueba.", "success");
  } catch (error) { status(explain(error), "error"); }
  finally { generateButton.disabled = false; }
});

copyButton.addEventListener("click", async () => {
  if (!tokenBox.value) return;
  try {
    await navigator.clipboard.writeText(tokenBox.value);
    status("Token copiado.", "success");
  } catch {
    tokenBox.focus(); tokenBox.select();
    status("Seleccioné el token. Pulsa Ctrl+C para copiarlo.");
  }
});

if ("serviceWorker" in navigator) navigator.serviceWorker.addEventListener("message", event => {
  if (event.data?.kind === "rutasegura-fcm-background") showReceived({ data: event.data.data }, false);
});
window.addEventListener("beforeunload", () => foregroundUnsubscribe?.());
