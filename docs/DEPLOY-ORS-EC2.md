# Configurar OpenRouteService en EC2

El workflow del monolito usa el [despliegue compartido](DEPLOY-EC2.md) y combina su configuración con `/opt/rutasegura/docker-compose.prod.yml` de la instancia. El build de ECR no necesita la API key. La instancia la lee antes de recrear el monolito, usando su rol de IAM.

El adaptador usa `https://api.heigit.org/openrouteservice/`. Si la instancia tenía un `Administration__Maps__BaseUrl` con el dominio anterior, retira ese ajuste para usar el valor actual. Véase el [anuncio de migración de HeiGIT](https://ask.openrouteservice.org/t/deprecating-api-openrouteservice-org-in-favour-of-api-heigit-org/7912).

1. En **AWS Secrets Manager**, región `us-east-1`, crea `rutasegura/prod/ors-api-key` y guarda como texto plano únicamente la clave emitida por ORS, sin comillas ni JSON. Adjunta al perfil IAM de la instancia permiso `secretsmanager:GetSecretValue` sobre el ARN de ese secreto. Si se usa una clave KMS administrada por el equipo, permite `kms:Decrypt` solo en esa clave.
2. Con el usuario de despliegue, crea `/opt/rutasegura/ors-secret-id` en EC2 con una línea que contenga el nombre o ARN del secreto. El archivo solo contiene el identificador.
3. Añade estas entradas al **Compose de producción de EC2**, dentro de las secciones que ya existen. Conserva los demás montajes y la configuración de servicios.

   ```yaml
   services:
     monolith:
       secrets:
         - source: ors_api_key
           target: Administration__Maps__ApiKey
   secrets:
     ors_api_key:
       file: /opt/rutasegura/secrets/ors/Administration__Maps__ApiKey
   ```

   El workflow crea el archivo dentro de un directorio privado del usuario de despliegue antes de ejecutar Compose. Docker lo monta de solo lectura para el proceso del monolito. Mantén este montaje en el Compose principal para que cualquier workflow que use el mismo archivo conserve la configuración.
4. Despliega el monolito actualizado desde `develop`. El workflow descarga el secreto, publica su archivo por reemplazo atómico y recrea solo el monolito. Durante el primer cambio, instala el montaje y el archivo identificador justo antes de ejecutar este workflow. Si la descarga falla, el flujo sale antes de recrear el contenedor. Para rotar la clave en ORS, actualiza el valor en Secrets Manager y vuelve a ejecutar el despliegue del monolito.
5. Verifica `/health/ready` del Gateway y los logs de migraciones. Desde una cuenta ADMIN, calcula un itinerario de dos paradas **ficticias o públicas** y confirma que se devuelve una geometría vial en la web. Un arranque saludable no verifica la autenticación con ORS. Conserva los secretos y los encabezados de autorización fuera de los logs.

## Integración con Trip & Tracking

El nuevo padrón del viaje y su itinerario usan el mismo cliente interno configurado por `GuardianAccess` en Tracking. En EC2, comprueba `GuardianAccess__BaseUrl`, `GuardianAccess__ServiceToken` y su equivalente `InternalServices__TripTrackingToken` en el monolito. Usa transporte interno protegido. El Gateway enruta `/api/v1/trips/**` de forma existente. Las rutas `/internal/**` quedan reservadas a la comunicación interna.

Despliega **Monolith y Trip & Tracking antes que web y Flutter**. Prueba un retorno con pasajeros, nombres en ambas vistas, GPS en estado retrasado e itinerario vial. El instalador de datos demo para desarrollo se ejecuta por separado.

En local sigue funcionando `ORS_API_KEY` en `.local/notification/compose.env`. El código, los archivos de configuración versionados y los clientes solo usan el nombre de la configuración.
