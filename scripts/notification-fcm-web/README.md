# Receptor web de prueba para Firebase Cloud Messaging

Pagina temporal de pruebas, independiente del frontend del producto. Usa la configuracion publica de la app web `rutasegura-f2703` y su VAPID publica; no recibe el JSON de Firebase Admin, JWT ni credenciales del backend. No carga Analytics. El servidor solo permite seis recursos estaticos conocidos, solo escucha en loopback y no registra peticiones ni tokens.

Desde la raiz del backend, con Node.js instalado y sin instalar paquetes:

```powershell
./scripts/Start-NotificationFcmWeb.ps1
```

1. Abrir `http://127.0.0.1:5500` en Chrome o Edge en modo normal.
2. Pulsar **Obtener token** y aceptar el permiso de notificaciones del navegador.
3. Copiar el **token FCM** mostrado. Es distinto del JWT de IAM y debe tratarse como identificador del destino de prueba; no se guarda en archivos ni logs del servidor.
4. Para probar la recepcion desde Firebase Console, usar ese token en **Enviar mensaje de prueba** de Cloud Messaging y dejar la pagina en segundo plano. El permiso de notificaciones del sistema operativo debe estar activo. Si la pagina esta al frente, se registra el mensaje en pantalla. Este paso no demuestra todavia envio desde RutaSegura.

Si el puerto esta ocupado, usar `./scripts/Start-NotificationFcmWeb.ps1 -Port 5501`. Mantener la misma direccion/puerto para conservar el contexto de permisos y registro del navegador. Ctrl+C detiene el servidor; un service worker/registro de Firebase puede seguir instalado en ese origen hasta retirar sus permisos/datos del navegador.

La VAPID publica se encuentra en Configuracion del proyecto Firebase → Cloud Messaging → Configuracion web / Certificados Web Push. La pagina permite corregirla para la prueba actual; cambios permanentes de proyecto se hacen en `firebase-config.js`. Los permisos se solicitan solo tras un click; no se genera un token ni se contacta a FCM para registrarse automaticamente al abrir. Los SDK se cargan desde el CDN oficial cuando se solicita el token; hace falta conexion a Internet. Un error `messaging/token-subscribe-failed` requiere comprobar proyecto/VAPID, FCM Registration API y restricciones de la API key antes de reintentar.

El SDK web queda fijado en 13.1.0. Se usa `getToken()` para mantener el contrato actual de registration tokens de Notification, sin mezclarlo con las APIs nuevas basadas en FID. El worker deja que Firebase muestre una sola notificacion cuando el payload ya contiene `notification`; el callback no crea otra copia.

Verificacion manual del 26/10/2026: el usuario obtuvo un token de registro y confirmo recepcion de una notificacion visible del sistema operativo, enviada desde Firebase Console a este navegador. Esto verifica Firebase → service worker/navegador → notificacion visible. El token de destino no se copia a este README ni se utiliza como destino fijo de otros integrantes.

Los chequeos locales de sintaxis/HTTP, por si solos, no acreditan entrega. La prueba manual tampoco resuelve los contratos pendientes del directorio ni el contexto autorizado de los padres. El registro del token en la API de RutaSegura y un envio desde el adaptador de Notifications se verifican aparte; no se marca ese recorrido como completado por haber enviado desde Firebase Console.

Referencias: [configuracion y tokens web FCM](https://firebase.google.com/docs/cloud-messaging/web/get-started), [SDK por CDN](https://firebase.google.com/docs/web/alt-setup), [referencia getToken](https://firebase.google.com/docs/reference/js/messaging#gettoken).
