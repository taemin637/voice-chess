# Korean voice commands

The current implementation targets Windows x64 and uses Microsoft Azure Speech
SDK 1.51.1 with the `ko-KR` locale.
Speech is recognized locally from the default microphone and sent directly to
the Azure Speech resource. Credentials are never serialized into a scene or
committed to the project.

## Unity Editor setup

1. Create an Azure Speech resource and copy its key and region.
2. In Unity, open **Voice Chess > Azure Speech Settings**.
3. Enter the resource key and region (for example, `koreacentral`) and save.
4. Start a multiplayer match. Move with **WASD**, jump with **Space**, and look
   with the mouse.
   Automatic voice activation is the default. Switch to **HOLD [V]** in Voice
   Settings when push-to-talk is preferable.

The local commander starts at the centre of the board. The friendly piece whose
visual centre is closest to the centre of the camera is the live voice target.
The target keeps changing while speech is active and is captured when local VAD
detects the end of the utterance (or when V is released in push-to-talk mode).
Only the Unity Editor shows the cyan target ring.

## Voice activation

- **AUTOMATIC**: local VAD starts after 0.1 seconds above the activation
  threshold and ends after 0.35 seconds of silence. An utterance is capped at
  3 seconds.
- **HOLD [V]**: hold V to start and release V to finish, using the same Azure
  recognition and command parser.
- A 0.2-second local microphone pre-roll is included in automatic mode so the
  first syllable is not clipped.
- **SENSITIVITY** changes the activation margin above the measured noise floor.
- **AUTO NOISE** measures ambient noise when the microphone starts and then
  follows quiet background changes slowly. **RECALIBRATE** performs a new
  1.5-second measurement; remain silent while it runs.

The editor stores the credential only in the local Unity `EditorPrefs` store.

## Player build setup

Before building, save the key and region in **Voice Chess > Azure Speech
Settings**. The Windows x64 build pipeline writes them to
`VoiceChess_Data/StreamingAssets/azure-speech.json`, and the player loads that
file automatically. Recipients can start `VoiceChess.exe` without setting
environment variables.

The generated JSON is intentionally excluded from Git, but it is distributed
with the player and can be read by anyone who receives the build. Use a
dedicated Speech resource whose key can be rotated or deleted independently.

These environment variables remain supported and override the bundled JSON
when they are present:

- `AZURE_SPEECH_KEY`
- `AZURE_SPEECH_REGION`

For a shipped game, replace the subscription-key configuration with a backend
that issues short-lived Azure authorization tokens. Do not distribute an Azure
subscription key inside a player build.

## Commands

- `돌진`, `돌진해`, `돌진해 줘`
- `공격`, `공격해`, `공격해 줘`

Both command families execute the same charge action. Azure receives only these
phrases as recognition hints, and only a final N-best candidate that maps to this
closed charge command set with at least 0.55 confidence is executed. Other speech
is shown but rejected. Utterance duration, loudness, and pronunciation accuracy
control the charge distance and cost.
