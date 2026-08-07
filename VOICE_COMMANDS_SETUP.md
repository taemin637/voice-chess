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

Set these environment variables before starting the Windows player:

- `AZURE_SPEECH_KEY`
- `AZURE_SPEECH_REGION`

For a shipped game, replace the subscription-key configuration with a backend
that issues short-lived Azure authorization tokens. Do not distribute an Azure
subscription key inside a player build.

## Commands

- `앞으로 이동`, `앞으로 가`, `계속 앞으로`, `전진`
- `뒤로 이동`, `뒤로 가`, `계속 뒤로`, `후진`
- `즉시 멈춰`, `멈춰`, `정지`, `이동 중지`
- `왼쪽으로 가` (현재 앞방향의 왼쪽 90도로 이동), `왼쪽 회전`, `왼쪽으로 돌아`, `좌회전`
- `오른쪽으로 가` (현재 앞방향의 오른쪽 90도로 이동), `오른쪽 회전`, `오른쪽으로 돌아`, `옆으로 돌아`, `우회전`
- `오른쪽 위로 가`, `왼쪽 위로 가`, `오른쪽 아래로 가`, `왼쪽 아래로 가` (모두 현재 앞방향 기준 대각선 이동)

Only a final Azure N-best candidate that maps to this closed command set and
has at least 0.55 confidence is executed. Other speech is shown but rejected.
The 80th-percentile microphone loudness measured during the utterance controls
the command reach. A quiet command reaches nearby pieces; a firm, loud command
reaches beyond the board diagonal. A player's capsule passes through friendly
pieces, but enemy pieces block the player. Knockback only occurs when a moving
enemy piece hits the player along its direction of travel; walking into a still
piece or into the side of a moving piece does not cause a bounce. The piece keeps
almost all of its existing momentum while the lighter player takes the impact.
Every piece owns a heading initialized to its team's forward direction. Rotation
commands change that heading, while movement commands use an offset relative to
the current heading without changing it. A colored arrow beneath each piece shows
its current heading. The intentionally directionless `옆으로 돌아` phrase starts
continuous clockwise/right rotation so that its behavior is deterministic.
Movement and rotation commands remain active together until `멈춰` is recognized.
