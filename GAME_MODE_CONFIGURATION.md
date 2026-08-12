# Voice Chess 게임 모드 설정

기본 씬의 `NetworkChessGame`은 `Assets/_Project/Settings/DefaultGameMode.asset`을 참조한다.
이 자산이 게임 설정 대시보드이며 규칙 스위치, 기물/보드, 입력, 음성 인식, UI/세션 값을 한곳에서 관리한다.
새 모드를 만들 때는 이 자산을 복제한 뒤 `NetworkChessGame`의 **게임 설정 대시보드** 슬롯에 새 자산 하나만 연결한다.

## 요청 사항별 설정 위치

- 플레이어가 킹: **Victory > Royal Unit Mode**를 `Player Commander`로 바꾼다. `Board King And Player Commander`와 Royal Requirement를 조합하면 둘 중 하나 또는 둘 다 사망해야 패배하도록 만들 수 있다. 플레이어 피해/장외 시스템에서는 서버에서 `NetworkPlayer.ServerSetEliminated(true)`를 호출한다.
- 기물별 차이: **Piece Archetypes**에서 이동/회전 속도, 질량, 충돌 반경, 넉백 감쇠, 장외 여유 거리, 명령 코스트 배수, 주기 점령 간격·점수, 최종 점령 가치, 이동 제약과 Ability 자산을 조절한다.
- 이동 구동 방식: 각 **Piece Archetype > Movement Control**에서 `Continuous (Legacy)` 또는 `Flick Impulse (Alkkagi)`를 선택한다. 기본 모드는 모든 기물이 알까기 방식이다.
  - `Continuous (Legacy)`: 기존처럼 이동 명령 후 `Stop` 명령 전까지 `Move Speed`로 계속 움직인다.
  - `Flick Impulse (Alkkagi)`: 명령 순간에만 힘을 받는다. `Quiet Flick Speed`와 `Loud Flick Speed` 사이에서 실제 음성 dB에 따라 초기 속도가 정해진다.
  - `Flick Friction`: 초당 감소하는 속도다. 높이면 빨리 멈추고 낮추면 오래 미끄러진다.
  - `Flick Loudness Exponent`: 1이면 dB와 속도가 선형이다. 1보다 높으면 큰 목소리에서 속도가 더 가파르게 증가한다.
  - `Accumulate Flick Impulses`: 켜면 움직이는 도중 내린 다음 명령이 추가 타격처럼 현재 속도에 더해진다.
  - `Maximum Flick Speed`: 연속 타격으로 지나치게 빨라지는 것을 막는 상한이다.
  - 알까기 방식에서 `Stop`은 현재 미끄러짐과 회전을 즉시 정지한다.
- 특수 스킬: `Create > Voice Chess > Piece Abilities > Impulse`로 스킬 자산을 만들고 기물의 **Abilities**에 연결한다. 기본 Knight에는 `KnightDash` 예제가 연결돼 있으며 “주 스킬 사용”, “첫 번째 스킬”, “1번 스킬”로 발동한다.
- 음성 명령 버전: **Commands > Voice Command Version**에서 구/신 방식을 전환한다.
  - `Legacy - Look To Select`: 기존 방식이다. 바라보는 아군 기물에 파란 원이 생기며 발화 시작 시 바라보던 기물에 기존 이동·회전·정지·스킬 명령을 내린다. 목소리 전달 거리 제한도 기존처럼 적용된다.
  - `New - Click Lock + Charge`: 바라보는 아군 기물에 파란 원이 생기고, **Players > Confirm Selection Button**으로 지정한 버튼을 누르면 그 기물이 확정 선택되며 주황 원으로 표시된다. 시선을 돌리거나 빈 곳을 클릭해도 풀리지 않으며, 명령이 정상 접수되거나 다른 아군 기물을 같은 버튼으로 선택할 때 교체·해제된다.
  - 신규 방식에서는 현재 `돌진`만 사용할 수 있다. 발화 시작 시 카메라 중앙에서 레이저를 쏴 기물(양 팀), 상대 플레이어, 보드, 보드 외곽의 논리적 경기장 벽 중 가장 먼저 닿은 지점을 목표로 저장한다. Azure 응답을 기다리는 동안 시선을 돌려도 저장된 지점이 유지된다.
  - 돌진 시 기물의 **Movement Control**이 `Continuous (Legacy)`면 목표 방향으로 계속 이동하고, `Flick Impulse (Alkkagi)`면 목표 방향으로 한 번 튕겨 나간 뒤 마찰로 감속한다.
  - **Charge Laser Range / Visible Seconds / Width / Color**와 **Charge Cost**는 모두 인스펙터에서 조절한다.
- 점령전 전체 스위치: **Capture Mode > Enabled**를 켠다. 끄면 점령 계산과 원 표시가 모두 사라진다. 점령 모드는 시간, 왕 사망, 턴제, 코스트제 등 다른 규칙과 독립적으로 조합된다.
- 점령 점수 규칙: **Capture Mode > Scoring Rule**에서 선택한다.
  - `Periodic Score Per Piece`: 원 안의 각 기물 인스턴스가 자기 타이머로 점수를 번다. 기물 종류별 **Periodic Capture Interval Seconds**와 **Periodic Capture Points**에서 “몇 초마다 몇 점”을 정한다. 같은 종류 기물도 타이머는 각각 독립적이며 양 팀 기물이 함께 있어도 각자 점수를 얻는다.
  - `Final Occupancy Piece Value`: 점수를 시간에 따라 누적하지 않는다. 종료 시 원 안에 있는 각 기물의 **Final Capture Value**를 합산한다. HUD에 보이는 값은 현재 원 점유 상태의 실시간 미리보기이며 최종 승패에는 종료 순간 값이 사용된다.
- 원 설정: **Capture Mode > Zones**에서 원을 원하는 만큼 추가하고 각 원의 **Board Position**, **Radius In Squares**, 채움/외곽선 색, 선 굵기와 높이를 조절한다. 플레이 중에는 반투명 원이 보이고, Scene 뷰에서는 Gizmo로 보인다.
- 주기 타이머 이탈 처리: **Reset Periodic Timer When Leaving**을 켜면 원에서 나간 기물의 남은 시간이 초기화되고, 끄면 다시 들어왔을 때 이어서 계산한다.
- 시간 종료 승패: **Resolve Winner At Time Limit**을 켜면 제한 시간 종료 시 점령 점수로 승자를 정한다. 끄면 **Clock > Time Limit Resolution** 규칙을 사용한다.
- 목표 점수 즉시 승리: 주기 점수 방식에서 **Victory > End At Capture Score**와 목표 점수를 켜면 시간 종료 전에도 게임이 끝난다. 최종 점유 가치 방식은 종료 순간 합산 규칙이므로 이 옵션을 사용하지 않는다.
- 턴제 명령: **Commands > Mode**를 `Alternating Turns`로 바꾼다. 첫 팀, 턴 시간, 명령 후 자동 턴 넘김, 비활성 팀 이동 정지를 설정할 수 있다. 수동 턴 종료 키는 **Players > End Turn Key**에서 정한다.
- 코스트 시스템: **Commands > Cost System Enabled**로 독립적으로 켜고 끈다. 실시간 명령과 턴제 양쪽에 조합할 수 있다.
  - **Starting Cost / Maximum Cost**: 경기 시작 코스트와 보유 상한을 정한다.
  - **Recharge Interval Seconds / Recharge Amount**: “몇 초마다 몇 코스트”가 들어오는지 정한다. 예를 들어 `2 / 1`이면 2초마다 1코스트가 충전된다.
  - **Cost Per Command**: 전진·후진·좌우·대각선·회전·정지·주 스킬·보조 스킬 비용을 각각 정한다. 실제 비용에는 기물별 **Command Cost Multiplier**와 Ability의 **Additional Command Cost**도 반영된다.
  - 활성화하면 경기 화면 왼쪽 위에 코스트 바와 `(현재/최대)` 숫자가 표시된다.
- 무제한 시간: **Clock > Mode**를 `Unlimited`로 바꾼다.
- 다른 클리어 방식: **Victory**에서 왕 사망, 전멸, 점령 목표를 독립적으로 조합하고, 제한 시간 종료 판정은 **Clock > Time Limit Resolution**에서 선택한다.
- 커스텀 시작 배치: 게임 모드 자산 하단의 **Make Standard 32-Piece Position Editable** 버튼을 누른 뒤 **Board Setup > Custom Placements**를 편집한다.

## 중앙 대시보드의 추가 설정

- **Board Presentation**: 양 팀 기물 프리팹, 보드 간격/회전, 장외 연출, 선택 원, 방향 화살표.
- **Players**: 1인칭 이동/점프/충돌, 턴 종료 키, 신규 방식의 기물 확정 클릭 버튼.
- **Voice Recognition**: 신뢰도, 조용한/큰 명령 dB, 전달 거리, 자동 발화 경계, 기본 입력 모드와 Push-to-Talk 키. 플레이어가 음성 설정 UI에서 저장한 감도와 노이즈 설정은 개인 설정이므로 저장값이 대시보드 기본값보다 우선한다.
- **Interface And Session**: 기준 해상도, 최대 세션 인원, 목록 갱신 간격, 타이머/코스트/점령 점수 HUD 위치, 일시정지 메뉴 키.

## 씬에 남아 있는 값

- `NetworkChessGame`: 사용할 게임 설정 대시보드 에셋 연결.
- `ChessSpawner`: 보드 배치 기준 Transform과 생성 기물 부모 Transform 연결.
- 카메라, 조명, 보드 메시처럼 씬 오브젝트 자체에 속하는 참조와 배치는 씬에서 관리한다.
- 컴포넌트 안의 기존 직렬화 값은 오래된 씬/프리팹 호환용 폴백으로만 남아 있고 기본 인스펙터에서는 숨겨진다. 중앙 대시보드가 연결되어 있으면 항상 대시보드 값이 우선한다.

모든 승패, 코스트, 턴, 점령 판정은 서버에서 수행되며 클라이언트 HUD에는 현재 턴, 코스트 또는 점령 점수가 네트워크 상태로 표시된다.
