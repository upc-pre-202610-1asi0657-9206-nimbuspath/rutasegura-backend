/* global firebase */
importScripts("/firebase-config.js");
const settings = self.RutaSeguraFcmConfig;
importScripts(
  `https://www.gstatic.com/firebasejs/${settings.sdkVersion}/firebase-app-compat.js`,
  `https://www.gstatic.com/firebasejs/${settings.sdkVersion}/firebase-messaging-compat.js`
);
firebase.initializeApp(settings.firebase);
const messaging = firebase.messaging();
self.addEventListener("install", event => event.waitUntil(self.skipWaiting()));
self.addEventListener("activate", event => event.waitUntil(self.clients.claim()));
messaging.onBackgroundMessage(async payload => {
  const windows = await self.clients.matchAll({ type: "window", includeUncontrolled: true });
  for (const client of windows) client.postMessage({ kind: "rutasegura-fcm-background", data: { notificationId: payload.data?.notificationId } });
  // Firebase muestra automaticamente los mensajes con notification; no duplicar la notificacion del backend.
  if (!payload.notification) await self.registration.showNotification("RutaSegura", {
    body: "Recibiste un mensaje de prueba.", tag: payload.data?.notificationId || "rutasegura-prueba"
  });
});
