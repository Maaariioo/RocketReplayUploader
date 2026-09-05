# RocketReplayUploader

Sube automáticamente tus replays de Rocket League a ballchasing.com y los
renombra según jugador/modo/fecha. Incluye una ventana de gestión con interfaz
moderna para ver todos tus replays, subirlos, renombrarlos o eliminarlos.

## Primer uso

1. Necesitas el SDK de .NET 9 instalado solo para COMPILARLO (el usuario
   final que reciba el .exe ya publicado NO necesita instalar nada).

   Para publicarlo como un único .exe portable:

   ```
   dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
   ```

   El .exe queda en:
   `bin\Release\net9.0-windows\win-x64\publish\RocketReplayUploader.exe`

2. Comparte ese único `RocketReplayUploader.exe` (no hace falta nada más).

## Para cualquier persona que lo reciba

1. Doble clic en `RocketReplayUploader.exe`.
2. La primera vez se abre la ventana de configuración:
   - Carpeta donde Rocket League guarda tus replays (ya te propone la ruta
     habitual, puedes elegirla con "Examinar...").
   - Tu nombre de jugador.
   - Tu API key de ballchasing.com (la validamos en el momento).
   - Si subir los replays como public / unlisted / private.
   - Si quieres que arranque solo cada vez que enciendes el PC.
3. La ventana principal te muestra todos los replays de tu carpeta, con
   botones en cada uno para:
   - **Renombrar**: lo renombra a `Jugador_Modo_Game_Fecha`.
   - **Subir**: lo sube a ballchasing.com y le pone el título.
   - **Eliminar**: lo borra de la carpeta (pide confirmación).
4. El interruptor **Autosubida** (activado por defecto) vigila la carpeta:
   cada replay que guarde Rocket League se sube y se renombra solo.
5. Al cerrar la ventana la app sigue funcionando en segundo plano desde la
   **bandeja del sistema**. Clic derecho en el icono: abrir ventana,
   activar/desactivar autosubida o salir del todo.

## Cambiar la configuración más adelante

Botón "Configuración" en la ventana principal, o ejecuta:

```
RocketReplayUploader.exe --setup
```

## Activar/desactivar el arranque automático a mano

```
RocketReplayUploader.exe --install      (arranca al iniciar sesión, recomendado)
RocketReplayUploader.exe --uninstall    (lo quita)
```

Alternativa (Servicio de Windows de verdad, sin interfaz; requiere abrir la
consola "como Administrador"):

```
RocketReplayUploader.exe --install-service
RocketReplayUploader.exe --uninstall-service
```

## Dónde queda guardada tu configuración

`%AppData%\RocketReplayUploader\config.json`

(No la borres al desinstalar/actualizar el programa si quieres conservar
tu API key y tus preferencias.)
