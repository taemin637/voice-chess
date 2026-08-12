# Voice Chess 게임 모드 설정

기본 씬의 `NetworkChessGame`은 `Assets/_Project/Settings/DefaultGameMode.asset`을 참조한다.
이 자산이 게임 설정 대시보드이며 규칙 스위치, 기물/보드, 입력, 음성 인식, UI/세션 값을 한곳에서 관리한다.
새 모드를 만들 때는 이 자산을 복제한 뒤 `NetworkChessGame`의 **게임 설정 대시보드** 슬롯에 새 자산 하나만 연결한다.

## 요청 사항별 설정 위치

- 플레이어가 킹: **Victory > Royal Unit Mode**를 `Player Commander`로 바꾸면 시작 배치에서 양 팀의 보드 킹이 빠지고, 각 플레이어 아바타가 **Board Presentation**에 등록된 자기 팀 킹 모델을 그대로 사용한다. `Board King And Player Commander`를 선택하면 보드 킹도 남고 플레이어 역시 킹 모델을 사용한다. Royal Requirement를 조합하면 둘 중 하나 또는 둘 다 사망해야 패배하도록 만들 수 있다. 플레이어 피해/장외 시스템에서는 서버에서 `NetworkPlayer.ServerSetEliminated(true)`를 호출한다.
  - 라운드 시작 때 플레이어 킹은 자기 팀의 초기 킹 배치 칸(기본 흰색 e1, 검은색 e8)으로 이동한다. **Players > 플레이어 킹 시작 위치와 시점**에서 초기 배치 추적 여부, 킹이 없을 때의 팀별 예비 좌표, 팀별 시작 시야 각도, 킹 모델 높이에 대한 카메라 비율을 조절한다.
  - 플레이어 킹은 기본적으로 자기 팀 기물과도 충돌한다. 경기장 경계를 넘으면 당시 수평 속도를 유지한 채 아래로 떨어지고, 설정 깊이에 도달하면 장외 사망 처리되어 기존 Royal Requirement 규칙으로 승패를 판정한다. **Players > 플레이어 킹 시작 위치와 시점**에서 아군 충돌, 장외 낙하, 낙하 중력, 사망 깊이와 장외 좌표 한계를 각각 조절한다.
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
  - `New - Click Lock + Charge`: 바라보는 아군 기물에 파란 원이 생기고, **Players > Confirm Selection Button**으로 지정한 버튼을 누르면 그 기물이 확정 선택되며 주황 원으로 표시된다. **Commands > 최대 확정 선택 기물 수**에서 동시 선택 수를 1~3개로 정한다. 같은 기물을 다시 누르면 그 기물만 선택 해제되고, 최대 수에서 새 기물을 고르면 가장 오래 선택한 기물이 빠진다. 시선을 돌리거나 빈 곳을 클릭해도 선택은 유지되며 명령이 접수되면 전체 선택이 해제된다.
  - 신규 방식에서는 현재 `돌진`만 사용할 수 있다. 발화 시작 시 카메라 중앙에서 레이저를 쏴 기물(양 팀), 상대 플레이어, 보드, 보드 외곽의 논리적 경기장 벽 중 가장 먼저 닿은 지점을 목표로 저장한다. Azure 응답을 기다리는 동안 시선을 돌려도 저장된 지점이 유지되며, 승인된 돌진은 확정 선택된 기물 모두에게 한 번에 적용된다.
  - 코스트 소모 버전이 구버전이면 돌진 시 기물의 **Movement Control**이 `Continuous (Legacy)`일 때 계속 이동하고, `Flick Impulse (Alkkagi)`일 때 목표 방향으로 한 번 튕겨 나간 뒤 마찰로 감속한다. 신규 발화 시간 방식에서는 두 이동 방식 모두 계산된 거리만큼 이동한 뒤 멈춘다.
  - **Charge Laser Range / Visible Seconds / Width / Color**는 모두 인스펙터에서 조절한다.
- 점령전 전체 스위치: **Capture Mode > Enabled**를 켠다. 끄면 점령 계산과 원 표시가 모두 사라진다. **Version**에서 `구버전 - 고정 점령 원`과 `신버전 - 랜덤 라운드 점령전`을 전환한다. 점령 모드는 시간, 왕 사망, 턴제, 코스트제 등 다른 규칙과 독립적으로 조합된다.
- 구버전 점령 점수 규칙: **Capture Mode > Scoring Rule**에서 선택한다.
  - `Periodic Score Per Piece`: 원 안의 각 기물 인스턴스가 자기 타이머로 점수를 번다. 기물 종류별 **Periodic Capture Interval Seconds**와 **Periodic Capture Points**에서 “몇 초마다 몇 점”을 정한다. 같은 종류 기물도 타이머는 각각 독립적이며 양 팀 기물이 함께 있어도 각자 점수를 얻는다.
  - `Final Occupancy Piece Value`: 점수를 시간에 따라 누적하지 않는다. 종료 시 원 안에 있는 각 기물의 **Final Capture Value**를 합산한다. HUD에 보이는 값은 현재 원 점유 상태의 실시간 미리보기이며 최종 승패에는 종료 순간 값이 사용된다.
- 구버전 원 설정: **Capture Mode > Zones**에서 원을 원하는 만큼 추가하고 각 원의 **Board Position**, **Radius In Squares**, 채움/외곽선 색, 선 굵기와 높이를 조절한다. 플레이 중에는 반투명 원이 보이고, Scene 뷰에서는 Gizmo로 보인다.
- 주기 타이머 이탈 처리: **Reset Periodic Timer When Leaving**을 켜면 원에서 나간 기물의 남은 시간이 초기화되고, 끄면 다시 들어왔을 때 이어서 계산한다.
- 시간 종료 승패: **Resolve Winner At Time Limit**을 켜면 제한 시간 종료 시 점령 점수로 승자를 정한다. 끄면 **Clock > Time Limit Resolution** 규칙을 사용한다.
- 목표 점수 즉시 승리: 주기 점수 방식에서 **Victory > End At Capture Score**와 목표 점수를 켜면 시간 종료 전에도 게임이 끝난다. 최종 점유 가치 방식은 종료 순간 합산 규칙이므로 이 옵션을 사용하지 않는다.
- 신버전 랜덤 라운드 점령전:
  - 서버가 보드 전체의 독립적인 균등 난수 위치에 미리보기 원을 만들고 **Random Round Duration Seconds**(기본 5초)가 끝나는 순간 판정한다. 기본값은 가장자리와 코너까지 모두 후보이며, **Random Keep Entire Circle Inside Board**를 켜면 원 전체가 판 안에 들어오도록 반지름만큼 가장자리를 제외한다.
  - 원 안의 기물 수가 더 많은 팀이 1점을 얻는다. 수가 같으면 각 팀 기물의 중심까지 거리 합이 더 작은 팀이 이기며, 양 팀 모두 0기물이거나 거리 차가 **Random Distance Tie Tolerance In Squares** 이하면 무득점이다.
  - **Random Round Score To Win**(기본 3점)에 먼저 도달하면 `Victory > End At Capture Score` 스위치와 관계없이 즉시 승리한다. **Random Round Interval Seconds**에서 판정 후 다음 원이 나타날 때까지의 간격을 정한다.
  - **Random Radius Minimum/Maximum In Squares**로 매 라운드 원의 크기 범위를 정한다. 두 값을 같게 두면 크기는 고정되고 위치만 무작위가 된다. **Random Minimum Centre Distance In Squares**는 기본 0으로, 매 위치가 서로 독립적인 진짜 균등 랜덤이다. 값을 올리면 직전 원 근처를 피하지만 큰 값에서는 두 구역을 오가는 느낌이 생길 수 있다.
  - 미리보기 원은 옅은 전체 테두리 위에 진한 테두리가 0%에서 100%까지 시계 방향으로 차오른다. 채움/옅은 선/진한 선 색, 원 분할 수, 선 굵기, 보드 위 높이, 시작 각도를 모두 **신버전 - 미리보기 원 표시**에서 조절한다.
  - **Random Seed**가 0이면 경기마다 달라지고, 0이 아니면 같은 시드의 위치 순서를 재현할 수 있어 테스트에 유용하다.
- 점령전 킹 부활:
  - **Capture Mode > Respawn Eliminated Kings**가 켜져 있으면 점령전 중 킹 사망은 즉시 패배로 처리되지 않는다. 보드 킹과 `Player Commander` 킹 모두 **King Respawn Delay Seconds**(기본 10초) 뒤 체스판의 안전한 랜덤 위치에 부활한다.
  - 랜덤 위치는 **King Respawn Edge Padding In Squares**만큼 가장자리에서 떨어지며, **King Respawn Clearance In Squares**를 포함해 기존 기물·플레이어와 겹치지 않는 후보를 우선한다.
  - 자기 킹이 죽어 있는 동안 카메라는 체스판 중앙 위에서 수직 아래를 바라보고 입력을 받지 않는다. 화면 중앙에는 부활까지 남은 초 숫자만 표시한다. **King Respawn Camera Height In Squares**, **King Respawn Countdown Font Size/Color**로 화면을 조절한다.
  - 점령전이 꺼져 있거나 킹 부활 스위치를 끄면 기존 **Victory > End When Royal Eliminated** 규칙이 그대로 적용된다.
- 턴제 명령: **Commands > Mode**를 `Alternating Turns`로 바꾼다. 첫 팀, 턴 시간, 명령 후 자동 턴 넘김, 비활성 팀 이동 정지를 설정할 수 있다. 수동 턴 종료 키는 **Players > End Turn Key**에서 정한다.
- 코스트 시스템: **Commands > Cost System Enabled**로 독립적으로 켜고 끈다. 실시간 명령과 턴제 양쪽에 조합할 수 있다.
  - **Starting Cost / Maximum Cost**: 경기 시작 코스트와 보유 상한을 정한다.
  - **Recharge Interval Seconds / Recharge Amount**: “몇 초마다 몇 코스트”가 들어오는지 정한다. 예를 들어 `2 / 1`이면 2초마다 1코스트가 충전된다.
  - **Cost Per Command**: 전진·후진·좌우·대각선·회전·정지·주 스킬·보조 스킬 비용을 각각 정한다. 실제 비용에는 기물별 **Command Cost Multiplier**와 Ability의 **Additional Command Cost**도 반영된다.
  - 활성화하면 경기 화면 왼쪽 위에 코스트 바와 `(현재/최대)` 숫자가 표시된다.
  - **Cost Consumption Version**에서 `구버전 - 명령별 고정 코스트`와 `신버전 - 발화 시간 비례 코스트`를 전환한다. 구버전 돌진은 기존 **Charge Cost**를 한 번 차감한다.
  - 신버전은 **Charge Cost**를 승인된 돌진의 최소 사용 비용으로 차감하고, **Voice Charge Cost Step**만큼의 시간 비례 비용을 **Voice Charge Seconds Per Cost Step**마다 계산한다. 두 값은 합산하지 않고 더 큰 값을 사용한다. `Charge Cost`의 최솟값과 기본값은 1이므로 짧은 돌진은 총 1코스트다. 길게 말할 때 생기는 시간 비례 비용은 확정 선택 기물 수만큼 증가한다. 예를 들어 1기물의 시간 비용이 1.5인 발화는 3기물 선택 시 4.5가 되며, 총비용은 **Maximum Cost**를 넘지 않는다. 최소 비용 1은 기물마다 반복해서 더하지 않는다. `0.01` 단위도 지정할 수 있으며 **Maximum Duration** 이후의 발화는 추가 코스트와 세기에 반영하지 않는다.
  - 발화 중에는 서버 코스트를 바로 확정하지 않고 HUD 바와 숫자에 예약 차감이 실시간 표시된다. Azure가 최종적으로 `돌진`을 승인하면 서버가 같은 양을 확정 차감하고, 잡음·오인식·거절이면 예약 차감과 화살표만 사라져 실제 코스트는 보존된다.
  - 신버전 돌진은 큰 목소리일수록 **Maximum Initial Loudness Distance** 범위 안에서 시작 거리가 즉시 생기고, 그 지점부터 유효 발화 시간이 지나가는 동안 최대 거리까지 계속 충전된다. 실제 dB 음량은 이후 시간 충전 효율도 높인다. 발음 정확도는 기본 거리를 더하지 않고 계산된 충전량을 감점하는 보정치로 적용된다. **Duration/Loudness Weight**, 최소·최대 거리, 초기 음량 거리, **Pronunciation Weight**(0=거리 영향 없음, 1=발음 점수를 그대로 곱함), Azure 신뢰도 비중을 인스펙터에서 조절한다.
  - **Voice Charge Loudness Exponent**는 돌진 전용 음량 곡선이다. `1`은 선형이고, 값을 높이면 작은 목소리의 초기 거리와 시간 충전 효율을 더 강하게 낮추면서 최대 음량의 결과는 유지한다. 자동 음성 감지의 개인 **Sensitivity**는 듣기 시작·종료 기준이며 이 거리 곡선과는 별개다.
  - **Voice Charge Duration Exponent**는 화살표가 시간에 따라 자라는 감각을 정한다. `1`은 선형, `1`보다 작으면 초반에 빠르게 뻗고 끝으로 갈수록 완만해지며, `1`보다 크면 초반은 느리고 후반에 빨라진다. 기본값 `0.6`은 최대 충전 시간과 코스트 계산 상한을 바꾸지 않고 시각적·물리적 충전 반응을 빠르게 만든다.
  - `돌지이이이인`, `도오올지이인`, `돌ㄹㄹㄹ진ㄴㄴㄴ`처럼 늘이거나 반복한 발음은 한글 음소로 분해·중복 축약한 뒤 `돌진`과 비교한다. 최종 발음 점수에는 이 텍스트 음소 유사도와 Azure 음성 신뢰도가 함께 반영된다.
  - 발화 중 Azure 중간 인식, 현재 dB와 길이를 계속 합산해 선택 목록의 대표 기물 위에 예상 이동 거리 화살표를 갱신한다. 실제 승인 시에는 선택된 모든 기물이 각자 같은 충전 세기로 이동한다. **Voice Charge Arrow Width / Height / Head Length / Color**에서 모양을 바꿀 수 있다.
  - 돌진 목표를 정하는 레이캐스트 판정은 항상 작동한다. 명령 확정 순간 카메라에서 목표까지 보이던 별도의 레이저 선은 **Show Charge Raycast Laser**를 켰을 때만 표시되며 기본값은 꺼짐이다. 충전 거리 화살표와는 별개다.
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

## 에디터 1인 테스트

- 기본값으로 **Editor Solo Test > Enabled**가 켜져 있다. Unity 에디터에서 Play를 누르면 Relay와 로비를 건너뛰고 로컬 Host, 팀 참가, 경기 시작이 자동으로 진행된다.
- 실제 네트워크 게임 코드를 Host로 실행하므로 상대 플레이어가 없다는 점을 제외하면 일반 경기와 같은 기물, 명령, 코스트, 점령 및 승패 규칙을 사용한다.
- **Player Team**에서 테스트할 팀을 정한다.
- 교대 턴제에서는 숫자 패드 `+` 또는 일반 키보드의 `Shift` + `=`를 누르면 현재 팀과 관계없이 다음 턴으로 강제 진행한다.
- 백틱 `` ` `` 키를 누르면 현재 경기를 제한시간 종료와 동일하게 즉시 판정한다. 최종 점유 방식의 점령 점수도 누른 순간 다시 합산한 뒤 `Resolve Winner At Time Limit` 및 `Time Limit Resolution` 설정에 따라 승자를 정한다.
- 상대 팀에는 살아 있는 더미 플레이어 지휘관 한 명이 존재하는 것으로 서버 승패 규칙이 계산한다. 따라서 플레이어가 킹인 모드도 상대가 접속하지 않았다는 이유만으로 즉시 종료되지 않으며, 기물 제거나 점령 점수 같은 실제 종료 조건을 혼자 시험할 수 있다.
- MPPM이나 실제 로비 흐름을 다시 테스트하려면 **Editor Solo Test > Enabled**를 끈다. 이 기능은 에디터 전용이므로 빌드에는 포함되지 않는다.

모든 승패, 코스트, 턴, 점령 판정은 서버에서 수행되며 클라이언트 HUD에는 현재 턴, 코스트 또는 점령 점수가 네트워크 상태로 표시된다.
