# JellyChannels

Plugin ligero para Jellyfin que crea canales de TV lineales (estilo Tunarr / ErsatzTV) a partir de la propia biblioteca, con EPG propio, anuncios, marcas de agua y ventana de visualización propia. Sin dependencia obligatoria de ffmpeg.

## Objetivos

- Canales con shows, películas, colecciones o tags de la biblioteca como fuente.
- Carpeta de anuncios por canal, intercalados entre items.
- Marca de agua (logo) por canal.
- EPG propio generado desde la parrilla.
- Integración con Live TV de Jellyfin y ventana propia tipo JellyTV.
- Bajo consumo: sin transcodificación por defecto, sin procesos por canal.
- Añadir vídeos de YouTube a un canal, con descarga y borrado a demanda (ver sección 6).

## Qué es y qué no es

- Es un plugin de servidor (C# / .NET) con una página web propia dentro de Jellyfin.
- No genera un stream continuo codificado. Reproduce los ficheros en direct play y calcula qué item y qué minuto toca en cada momento.
- No graba la marca de agua en el vídeo. Se superpone en el cliente.

## Dónde se hace

| Pieza | Dónde vive |
|---|---|
| Lógica de canales y parrilla | Plugin Jellyfin, proyecto C# (`Jellyfin.Plugin.JellyChannels`) |
| Configuración de canales | Página de configuración del plugin (HTML + JS embebidos) |
| Ventana propia | Página web servida por el plugin en `/JellyChannels/Web` |
| EPG | Endpoint del plugin: `/JellyChannels/epg.xml` (XMLTV) |
| Lista de canales | Endpoint del plugin: `/JellyChannels/channels.m3u` |
| Datos | `channels.json` en `<datos de Jellyfin>/JellyChannels/` |

## Cómo funciona

### 1. Modelo de canal

```json
{
  "id": "canal-1",
  "name": "Canal Retro",
  "number": 1,
  "logo": "logo.png",
  "watermark": { "file": "marca.png", "position": "bottom-right", "opacity": 0.6, "size": 12 },
  "sources": [
    { "type": "series", "id": "<jellyfin-item-id>" },
    { "type": "movie", "id": "<jellyfin-item-id>" },
    { "type": "collection", "id": "<jellyfin-item-id>" },
    { "type": "tag", "value": "80s" },
    { "type": "youtube", "url": "https://www.youtube.com/watch?v=<id>", "retention": "delete-after-airing" }
  ],
  "mode": "sequential | shuffle | seeded-shuffle",
  "ads": { "folder": "/ruta/anuncios", "everyMinutes": 15, "maxPerBreak": 3 },
  "epgDays": 3
}
```

### 2. Parrilla determinista

- Cada canal tiene una semilla y una fecha de inicio.
- La lista de items se ordena según el modo y se recorre en bucle sumando duraciones (`RunTimeTicks` de Jellyfin).
- El item actual se calcula con `(ahora - inicio) mod duración_total_ciclo`.
- Los anuncios se insertan en los huecos entre items, o en puntos fijos si se configura.
- No se guarda estado por canal: se recalcula bajo demanda, así que no hay procesos en segundo plano.

### 3. EPG (XMLTV)

- Se genera recorriendo la misma parrilla hacia delante `epgDays` días.
- Cada programa lleva título, sinopsis, póster y episodio, sacados de los metadatos de Jellyfin.
- Se sirve en `/JellyChannels/epg.xml` y se cachea unos minutos.

### 4. Integración con Jellyfin

**Vía A: Live TV nativo**
- Exponer `channels.m3u` y `epg.xml` y añadirlos como tuner M3U y proveedor XMLTV en Jellyfin.
- Más adelante, implementar `ITunerHost` e `IListingsProvider` dentro del plugin para que se instalen solos.
- Limitación: al terminar cada item el reproductor puede cortar y hay que volver a sintonizar.

**Vía B: ventana propia (recomendada)**
- Página web con la guía de canales, el canal activo y un reproductor.
- Pide al servidor `item actual + offset` y reproduce en direct play con `StartTimeTicks`.
- Al terminar el item, pide el siguiente. Los anuncios son items más.
- La marca de agua es un `<img>` superpuesto con CSS según la configuración del canal.

### 5. Sobre ffmpeg

- Modo por defecto: no se usa.
- Opcional y desactivado por defecto (v0.5): remux usando el ffmpeg que ya trae Jellyfin, para dar un stream continuo a clientes que solo hablan Live TV. Copia las pistas que ya son H.264 y AAC (muy poca CPU) y recodifica solo las que no lo son, para que los ficheros de un canal puedan tener códecs distintos. Detalles en la sección *Remux* más abajo.
- Marca de agua grabada en vídeo: solo sería posible recodificando. Queda fuera del alcance.

### 6. Vídeos de YouTube (descarga y borrado a demanda)

Un canal puede incluir fuentes de tipo `youtube`: un vídeo suelto o, más adelante, una lista de reproducción. Los vídeos no se descargan al añadirlos, solo cuando hacen falta.

**Flujo**

1. **Alta**: al pegar la URL, el plugin pide solo los metadatos (título, duración, miniatura, descripción), sin descargar el vídeo. Con la duración el vídeo ya entra en la parrilla y en el EPG como cualquier otro item.
2. **Descarga a demanda**: unos minutos antes de que toque emitirlo (margen configurable, por defecto 30 min), una tarea ligera lo descarga a una carpeta de caché del plugin. Si aún no está listo cuando llega su hora, el canal salta al siguiente item y lo reintenta después.
3. **Emisión**: se reproduce desde la caché en direct play, igual que el resto de items. El plugin lo sirve con soporte de rangos (`Range`) para poder arrancar en un offset.
4. **Borrado**: según la política `retention` de la fuente:
   - `delete-after-airing` (por defecto): se borra al terminar su emisión.
   - `keep-days:N`: se conserva N días por si se repite en el ciclo.
   - `keep`: no se borra automáticamente.
   - Borrado manual desde la página de ajustes y límite de espacio en disco (al superarlo se borran primero los ya emitidos, más antiguos primero).

**Cómo se descarga**

- Con `yt-dlp` como binario externo, configurable por ruta en los ajustes del plugin. No se incluye en el plugin.
- Para no depender de ffmpeg se pide un formato progresivo en un solo archivo (por ejemplo `-f "b[ext=mp4]"`), que `yt-dlp` descarga sin fusionar pistas. La calidad máxima de esos formatos es limitada. Si se quiere más calidad, `yt-dlp` necesita ffmpeg para unir vídeo y audio, y eso sería una opción activable, no el comportamiento por defecto.
- Un proceso de descarga a la vez, con prioridad baja, para no cargar el servidor.

**Dónde vive**

- Caché: `<datos de Jellyfin>/JellyChannels/cache/youtube/`.
- Los vídeos de caché no pertenecen a la biblioteca de Jellyfin: los sirve el propio plugin, así que funcionan en la ventana propia. En Live TV nativo dependen del redirect de cada canal, con las mismas limitaciones que ya se describen en la sección 4.

**Nota legal**: descargar vídeos puede incumplir las condiciones de servicio de YouTube y los derechos de autor del contenido. Está pensado para uso personal con contenido que tengas derecho a guardar. La función va desactivada por defecto.

## Remux (v0.5)

Se activa en los ajustes del plugin (*Stream continuo*). Añade `GET /JellyChannels/channels/{id}/live.ts`, un MPEG-TS continuo que sigue la parrilla: empieza en el item y el punto que tocan ahora y, al acabar cada item, encadena el siguiente sin que el cliente tenga que volver a sintonizar. Con el remux activo, `/channels/{id}/stream` (la URL del M3U) redirige a `live.ts` en vez de al fichero suelto, así que el tuner de Live TV lo usa sin cambiar nada.

**Cómo funciona**

- Usa el ffmpeg de Jellyfin (`IMediaEncoder.EncoderPath`). No hay ruta que configurar ni binario que instalar.
- Un proceso ffmpeg por espectador y solo mientras ve el canal. Cuando el cliente se desconecta, se mata el proceso. No hay procesos permanentes por canal.
- Cada item es un tramo `ffmpeg -re -ss <offset> -i <fichero> ... -f mpegts` a velocidad real, con la primera pista de vídeo y la primera de audio.
- **Códecs**: el stream sale siempre en H.264 (8 bits, 4:2:0) y AAC, así que el reproductor nunca ve un cambio de códec a mitad. Antes de cada tramo se leen las pistas del fichero (de la base de datos de Jellyfin) y se decide pista a pista: si ya es H.264 de 8 bits en 4:2:0 se copia; si no (HEVC, VP9, AV1, MPEG-4, H.264 de 10 bits o 4:4:4...) se recodifica con `libx264 -preset veryfast -crf 23 -bf 0` y keyframes cada 2 s. Lo mismo con el audio: AAC se copia y el resto se recodifica a AAC 192k. Si no se pueden leer las pistas, se recodifica todo.
- Se puede desactivar la recodificación en los ajustes (*Recodificar lo que no sea H.264 / AAC*). Entonces todo se copia y los ficheros de un canal deben compartir códecs, como en la primera versión del remux. La parrilla decide qué tramo toca, y los fallos se tratan así: si un fichero no se puede remuxar se reintenta dos veces y se espera al siguiente item; tras tres huecos seguidos imposibles se cierra el stream.
- Las marcas de tiempo del stream nunca retroceden: cada tramo empieza en `max(reloj, final del tramo anterior)` y el muxer marca el salto con el *discontinuity indicator*.
- Al abrir el canal desde Live TV, Jellyfin mantiene el stream (y por tanto el ffmpeg del plugin) abierto hasta que el cliente cierra la reproducción.
- Límite de streams simultáneos configurable (2 por defecto). Pasado el límite, `live.ts` responde 503 con `Retry-After`.

**Límites (importantes)**

- Con vídeo copiado, el salto de `-ss` cae en el keyframe anterior, así que al sintonizar a mitad de un item el stream puede empezar hasta un GOP antes de lo que dice la guía (unos segundos en la mayoría de ficheros). Con vídeo recodificado el salto es exacto. El desfase no se acumula: cada tramo siguiente se recalcula con el reloj.
- Entre items hay un pequeño hueco (la puesta en marcha de ffmpeg y un margen de 0,2 s para que el DTS no retroceda).
- Recodificar usa CPU (solo mientras se emite un fichero que lo necesite, y es CPU, no aceleración por hardware). `veryfast` aguanta tiempo real con 1080p en una CPU moderna, pero no en un equipo muy justo; si va lento, el stream se entrecorta.
- La resolución y la tasa de fotogramas no se unifican: si cambian entre items, el SPS cambia a mitad del stream y casi todos los reproductores lo toleran, pero no se garantiza.
- Solo ficheros locales. Los items que no lo sean se saltan.
- No añade marca de agua ni anuncios al vídeo (eso exigiría recodificar).

## Estructura del proyecto

```
Jellyfin.Plugin.JellyChannels/
├── Plugin.cs                      # Punto de entrada, ID, nombre, páginas
├── PluginServiceRegistrator.cs    # Registro de servicios en el contenedor de Jellyfin
├── Configuration/
│   ├── PluginConfiguration.cs     # Configuración global
│   └── configPage.html            # Ajustes del plugin y editor de canales
├── Models/
│   ├── Channel.cs                 # + ChannelSource, Watermark, AdSettings, enums
│   ├── ScheduleItem.cs            # Item emitible con duración y metadatos
│   └── ScheduleEntry.cs           # Hueco de parrilla (item + inicio + fin)
├── Services/
│   ├── ChannelStore.cs            # Lectura/escritura de channels.json
│   ├── LibraryResolver.cs         # Resuelve fuentes a items vía ILibraryManager
│   ├── ScheduleEngine.cs          # Calcula item actual y parrilla
│   ├── ScheduleService.cs         # Caché de parrillas por canal
│   ├── StableRandom.cs            # Barajado determinista (SplitMix64)
│   ├── EpgGenerator.cs            # Genera XMLTV
│   ├── M3uGenerator.cs
│   ├── GuideService.cs            # Caché del EPG
│   ├── RemuxService.cs            # (v0.5) Encadena tramos ffmpeg -c copy siguiendo la parrilla
│   ├── RemuxArguments.cs          # (v0.5) Argumentos de ffmpeg
│   └── FfmpegProcessRunner.cs     # (v0.5) Lanza el ffmpeg de Jellyfin
│   ├── AdInserter.cs              # (v0.3) Escanea carpeta de anuncios e intercala
│   └── YoutubeCache.cs            # (v0.6) Metadatos, descarga a demanda y borrado (yt-dlp)
├── Api/
│   ├── ChannelsController.cs      # CRUD de canales
│   ├── NowPlayingController.cs    # item + offset, parrilla, redirect de stream y live.ts
│   └── GuideController.cs         # epg.xml, channels.m3u y assets
├── Web/                           # (v0.2) Ventana propia
│   ├── player.html
│   ├── player.js
│   └── player.css
└── Jellyfin.Plugin.JellyChannels.csproj
Jellyfin.Plugin.JellyChannels.Tests/  # Tests xUnit (motor, EPG, M3U, almacén)
```

## API

| Método y ruta | Acceso | Qué hace |
|---|---|---|
| `GET /JellyChannels/channels` | usuario | Lista de canales |
| `GET /JellyChannels/channels/{id}` | usuario | Un canal |
| `POST /JellyChannels/channels` | admin | Crea un canal (JSON del modelo de canal) |
| `PUT /JellyChannels/channels/{id}` | admin | Reemplaza un canal. Si no se envían `seed` y `startDate`, se conservan |
| `DELETE /JellyChannels/channels/{id}` | admin | Borra un canal |
| `GET /JellyChannels/channels/{id}/items` | usuario | Items resueltos del canal, en orden |
| `GET /JellyChannels/channels/{id}/now` | usuario | Item actual, `startTimeTicks` y siguiente item |
| `GET /JellyChannels/channels/{id}/schedule?hours=24` | usuario | Parrilla desde ahora |
| `POST /JellyChannels/channels/refresh` | admin | Vacía la caché de parrillas |
| `GET /JellyChannels/channels/{id}/stream` | guía | Redirect 302 al stream del canal (para el tuner M3U): a `live.ts` si el remux está activo, y si no al item actual en direct play |
| `GET /JellyChannels/channels/{id}/live.ts` | guía | (v0.5) MPEG-TS continuo remuxado. 404 si el remux está desactivado, 503 si se alcanzó el máximo de streams |
| `GET /JellyChannels/epg.xml` | guía | EPG XMLTV |
| `GET /JellyChannels/channels.m3u` | guía | Lista M3U |
| `GET /JellyChannels/assets/{fichero}` | anónimo | Logos y marcas de agua de `<datos de Jellyfin>/JellyChannels/assets/` |

"guía" significa: anónimo si está activado *Servir sin autenticación* en los ajustes del plugin; si no, hace falta sesión o `?ApiKey=<clave>` (Panel > Claves de API). La clave se propaga a las URLs que aparecen dentro del M3U.

Si `id` de un canal no se indica al crearlo, se genera a partir del nombre (`Canal Retro` → `canal-retro`). Si faltan `seed` o `startDate`, se rellenan al crear (semilla derivada del id e inicio en el minuto actual).

## Requisitos

- Jellyfin 12.0 o superior. El plugin compila contra `Jellyfin.Controller` y `Jellyfin.Model` 12.0.0 (`targetAbi` 12.0.0.0) y está probado en Jellyfin 12.2. No carga en 10.x.
- .NET SDK 10.
- Un IDE: Visual Studio, Rider o VS Code con la extensión C#.
- Jellyfin de pruebas en local o en Docker.
- Opcional, solo para fuentes de YouTube: `yt-dlp` instalado en el servidor y espacio en disco para la caché.

## Pasos de desarrollo

1. **Plantilla**: partir de `jellyfin-plugin-template` y renombrar namespace, GUID y nombre.
2. **Modelos y almacenamiento**: `Channel`, `ChannelSource`, `ChannelStore` con JSON en la carpeta de datos del plugin.
3. **Resolver de biblioteca**: usar `ILibraryManager` para convertir fuentes (serie, película, colección, tag) en una lista de items con duración.
4. **Motor de parrilla**: implementar `ScheduleEngine` con semilla y cálculo por módulo. Añadir tests unitarios con duraciones inventadas.
5. **Endpoints**: `GET /JellyChannels/channels`, `GET /JellyChannels/channels/{id}/now`, `GET /JellyChannels/epg.xml`, `GET /JellyChannels/channels.m3u`.
6. **Prueba en Live TV**: añadir el M3U y el XMLTV como tuner y guía en Jellyfin y comprobar que aparecen los canales.
7. **Ventana propia**: `player.html` con guía, reproductor y overlay de marca de agua. Reproducción en direct play con `StartTimeTicks` y paso automático al siguiente item.
8. **Anuncios**: escaneo de la carpeta, duración por fichero y reglas de inserción (cada X minutos o entre items).
9. **Configuración**: página de ajustes para crear y editar canales, elegir fuentes, logo, marca de agua y carpeta de anuncios.
10. **Opcional**: remux `-c copy` para Live TV continuo, con interruptor en ajustes.
11. **YouTube**: `YoutubeCache` con lectura de metadatos, tarea de prefetch, descarga con `yt-dlp`, política de retención y límite de disco. Endpoint de caché con soporte `Range`.
12. **Empaquetado**: generar el ZIP del plugin con su `meta.json` y publicar un manifiesto de repositorio para instalarlo desde Jellyfin.

## Compilar y probar

```bash
dotnet build Jellyfin.Plugin.JellyChannels.slnx -c Release
dotnet test Jellyfin.Plugin.JellyChannels.slnx
```

La DLL queda en `Jellyfin.Plugin.JellyChannels/bin/Release/net10.0/Jellyfin.Plugin.JellyChannels.dll`.

Los tests del remux usan un ejecutor de ffmpeg falso y un reloj controlado, así que no necesitan ffmpeg instalado.

## Instalación (una vez compilado)

1. Copiar `Jellyfin.Plugin.JellyChannels.dll` a `<datos de Jellyfin>/plugins/JellyChannels_0.5.0.0/`.
2. Reiniciar Jellyfin.
3. Abrir Panel > Plugins > JellyChannels y configurar los canales.
4. Para Live TV: Panel > Live TV > añadir tuner M3U con `http://<servidor>/JellyChannels/channels.m3u?ApiKey=<clave>` y proveedor XMLTV con `http://<servidor>/JellyChannels/epg.xml?ApiKey=<clave>` (sin `?ApiKey` si se activa el acceso anónimo).
5. Para la ventana propia: abrir `/JellyChannels/Web`.

## Riesgos y decisiones abiertas

- Algunos clientes de Jellyfin (TV, móvil) solo usan Live TV, donde el cambio entre items puede cortar la reproducción. Por eso la ventana propia es la vía principal.
- En Live TV sin remux, el redirect de stream sirve el fichero en direct play (`static=true`), que no admite empezar en un offset: al sintonizar, el item empieza desde el principio aunque la guía diga que va por la mitad. Activar el remux (v0.5) lo resuelve, con los límites descritos en su sección.
- La parrilla se calcula a partir de la lista de items. Si se añaden o quitan episodios de una fuente, la duración del ciclo cambia y la parrilla se desplaza. Se aplica al vencer la caché (10 min por defecto) o con `POST /JellyChannels/channels/refresh`.
- Reproducir en direct play depende de que el códec del fichero sea compatible con el cliente. Sin transcodificación, un fichero no soportado simplemente no se verá.
- Los anuncios deben tener duración conocida. Habrá que leerla desde Jellyfin si están en la biblioteca, o con una librería de metadatos si son ficheros sueltos.
- Si más adelante se quiere marca de agua grabada o salida HLS real, habrá que aceptar ffmpeg como dependencia en ese modo. El remux de la v0.5 ya usa ffmpeg, pero solo para copiar pistas.
- YouTube cambia con frecuencia y `yt-dlp` necesita mantenerse actualizado, así que las descargas pueden fallar hasta actualizarlo.
- Si una descarga no termina a tiempo, el canal salta ese item. Por eso el margen de prefetch importa.
- Descargar de YouTube puede incumplir sus condiciones de servicio: función opcional y desactivada por defecto.

## Hoja de ruta

- [x] v0.1: canales, parrilla, M3U y XMLTV (incluye resolución por serie, película, colección y tag, y un editor JSON básico en la página de ajustes)
- [ ] v0.2: ventana propia con marca de agua
- [ ] v0.3: anuncios
- [ ] v0.4: página de configuración completa
- [x] v0.5: remux `-c copy` opcional
- [ ] v0.6: fuentes de YouTube con descarga y borrado a demanda
