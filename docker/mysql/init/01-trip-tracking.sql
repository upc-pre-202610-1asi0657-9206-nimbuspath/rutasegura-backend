-- La imagen de MySQL crea rutasegura_monolith y el usuario rutasegura (MYSQL_DATABASE / MYSQL_USER).
-- Aquí se agrega la base de Trip & Tracking para el mismo usuario. Solo corre con el volumen vacío.
CREATE DATABASE IF NOT EXISTS rutasegura_trip_tracking CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
GRANT ALL PRIVILEGES ON rutasegura_trip_tracking.* TO 'rutasegura'@'%';
FLUSH PRIVILEGES;
