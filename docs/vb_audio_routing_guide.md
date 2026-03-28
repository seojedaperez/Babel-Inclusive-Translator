# Guía de Enrutamiento: VB-Audio Virtual Cable + Inclusive Communication Hub

Esta guía detalla cómo configurar **Windows 11** y aplicaciones de terceros (Microsoft Teams, Zoom, OBS) para aprovechar el pipeline de audio sintetizado por el **Inclusive Communication Hub (ICH)** a través del **VB-Audio Virtual Cable**.

---

## 1. Comprobación del Controlador (Drivers)

1. Presiona `Win + R`, escribe `mmsys.cpl` y presiona Enter.
2. En la pestaña **Reproducción (Playback)**, asegúrate de que exista un dispositivo llamado **"CABLE Input (VB-Audio Virtual Cable)"**.
    - *Asegúrate de NO fijarlo como tu dispositivo de salida predeterminado*, de lo contrario dejarás de escuchar todo el audio de tu PC. Tu dispositivo por defecto debe seguir siendo tu Altavoz/Auricular normal.
3. En la pestaña **Grabación (Recording)**, verifica que exista un dispositivo llamado **"CABLE Output (VB-Audio Virtual Cable)"**.

---

## 2. Enrutamiento en el Motor de ICH

El sistema fue programado para **autodescubrir** el índice de `CABLE Input`.
- Una vez levantes el *Background Service* de ICH y actives tu micrófono, el texto traducido y sintetizado se inyectará silenciosamente directo a "CABLE Input".
- No escucharás a la IA en tus altavoces principales, previniendo eco o interrupciones en la llamada.

---

## 3. Configuración en Microsoft Teams / Zoom

Para que los demás participantes "escuchen" la voz de la IA como si fueras tú hablando fluidamente (Virtual Loopback):

### Microsoft Teams
1. Únete a una reunión.
2. Ve a **Configuración de Dispositivos** (Device Settings).
3. En la sección **Micrófono (Microphone)**, cambia tu micrófono físico por: **`CABLE Output (VB-Audio Virtual Cable)`**.
4. ¡Listo! Todo lo que el Hub procese se enviará como "micrófono" perfecto y limpio a Teams.

### Zoom
1. Entra a **Configuración -> Audio**.
2. En la sección de **Micrófono**, selecciona **`CABLE Output (VB-Audio Virtual Cable)`**.
3. *Tip Pro:* Desactiva la reducción de ruido de fondo de Zoom (ponla en 'Low' o 'Original Sound'), pues el Hub ya purificó y sintetizó el audio nativamente mediante Azure Neural TTS.

---

## 4. Escuchar el Audio Virtual Simultáneamente (Opcional)

Si deseas monitorear activamente lo que la IA le está diciendo a la sala por el micrófono virtual:
1. Vuelve a abrir `mmsys.cpl` -> Pestaña **Grabación (Recording)**.
2. Haz clic derecho sobre **CABLE Output** -> **Propiedades**.
3. Ve a la pestaña **Escuchar (Listen)**.
4. Marca la casilla **"Escuchar este dispositivo" (Listen to this device)** y en el menú desplegable selecciona tus altavoces/auriculares principales.
5. Haz clic en **Aplicar**.

> **⚠️ Advertencia:** Si activas esto mientras el Hub captura el *Output del Sistema*, podrías crear un blucle de retroalimentación infinito (feedback loop) si no configuras exclusiones de proceso. Úsalo solo para debug / pruebas iniciales.
