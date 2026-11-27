# Despliegue del backend en EC2

Los pipelines de Monolith, Trip & Tracking, Notifications y Gateway publican su imagen y llaman al workflow compartido `.github/workflows/deploy-ec2.yml`. La instancia sigue usando `/opt/rutasegura/docker-compose.prod.yml` como base privada del despliegue existente. Si cambia ese directorio, se puede ajustar la variable de GitHub Actions `EC2_DEPLOY_DIR`. El Compose de la instancia conserva la configuración de las bases, redes, volúmenes y credenciales del entorno.

## Cambiar la configuración desde el repositorio

1. Editar `docker/ec2/compose.override.yml` y enviar el cambio a `develop`.
2. El workflow **Deploy backend to EC2** envía ese archivo por la conexión SSH existente, valida la combinación con el Compose de EC2 y aplica la configuración a los cuatro servicios de aplicación. En un cambio exclusivamente de configuración, usa las imágenes disponibles en la instancia. En un cambio de código, descarga la imagen del servicio que lo solicitó y aplica el mismo archivo de configuración.
3. Confirmar `/health/ready` y la ruta modificada. Con el proxy de Notifications activo, `GET /api/v1/notifications/contexts` debe responder `401` sin autenticación. Con un tutor autorizado, comprobar también el acceso a la bandeja por HTTPS.

EC2 conserva la última configuración aplicada correctamente en `.deploy-state/`. Los despliegues de código la reutilizan, así que un build lento no repone un Compose anterior. Cada build publica una etiqueta propia que incluye el commit, el número de ejecución y el intento. Los despliegues posteriores de configuración reutilizan las referencias de imagen activas. Si una ejecución antigua termina después de una más reciente para el mismo servicio, EC2 la omite. Para reaplicar una configuración, ejecutar **Deploy backend to EC2** desde `develop`. El workflow espera el arranque de los servicios recreados y usa `--no-deps` para respetar MySQL, RabbitMQ y Redis.

Para revertir un cambio de configuración, restaurar el valor anterior en el override mediante un cambio revisado a `develop`. Si una opción se debe desactivar y el valor en la base privada no está confirmado, definir explícitamente el valor deseado en el override.

## Habilitar Firebase Cloud Messaging

El Gateway y la bandeja funcionan independientemente de Firebase. Para habilitar los push desde EC2, se realiza una única configuración del proveedor.

1. Guardar una credencial JSON de cuenta de servicio en **AWS Secrets Manager**, como el valor completo de un secreto en la región de EC2. La cuenta debe estar autorizada a enviar FCM en el proyecto configurado.
2. Dar al rol de la instancia permiso `secretsmanager:GetSecretValue` sobre ese secreto. Si se usa una clave KMS propia, incluir el permiso de descifrado correspondiente.
3. Crear la variable de repositorio de GitHub Actions `NOTIFICATION_FCM_SECRET_ID` con el nombre o ARN de AWS. El despliegue obtiene el ID del proyecto del JSON. Para comprobar que la credencial corresponde al proyecto esperado, se puede definir también `NOTIFICATION_FCM_PROJECT_ID`.
4. Desplegar un cambio de configuración o de `notification` en `develop`. Si el workflow también está en la rama predeterminada del repositorio, se puede ejecutar **Deploy backend to EC2** desde `develop` con **Run workflow**. El script descarga y valida el JSON en EC2, lo coloca en el directorio privado del servicio y usa el override de FCM del repositorio. La clave se monta solo en `notification`.
5. Registrar un token desde un navegador de prueba, provocar un evento nuevo con un tutor autorizado y comprobar bandeja y push. El servicio web debe implementar su registro de suscripciones para que esto ocurra automáticamente con los usuarios.

Para rotar la credencial, actualizar el secreto de AWS y volver a ejecutar el workflow. El valor de la credencial queda fuera del repositorio y de la configuración de GitHub. La descarga de FCM requiere AWS CLI y Python 3 en EC2. El despliegue usa Docker Compose con `up --wait` y `flock` para serializar cambios.
