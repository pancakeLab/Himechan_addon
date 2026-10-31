namespace RotationSolver.Himechan;

/// <summary>
/// Korean dictionary part 3: strings added or reworded by upstream 7.5.7.5-7.5.7.13 (settings reorganisation in 7.5.7.7).
/// Placeholder ids as in part 2, plus actions 7514 Vercure, 7542 Bloodbath (verified on xivapi 2026-10-31).
/// Entries whose English only changed trivially reuse the part 2 wording.
/// </summary>
internal static partial class HimechanKoreanStrings
{
	private static void AddUpstream7513()
	{
		// Section / group headings
		Add("Commands", "명령");
		Add("Timing", "타이밍");
		Add("Tweaks", "보정");
		Add("Hotbars", "핫바");
		Add("Customize UI", "UI 꾸미기");
		Add("Action Interception", "기술 가로채기");
		Add("Raise Usage and Control", "부활 사용과 제어");
		Add("Automatic healing thresholds", "자동 회복 기준값");
		Add("AoE usage", "광역 기술 사용");
		Add("Tincture usage", "영약 사용");
		Add("Debug mode", "디버그 모드");
		Add("Who to raise", "누구를 부활시킬지");
		Add("Window size", "창 크기");

		// Commands
		Add("/rotation Cycle behavior", "/rotation Cycle 동작");
		Add("Make /rotation Auto a toggle", "/rotation Auto를 토글로");
		Add("Make /rotation Manual a toggle", "/rotation Manual을 토글로");
		Add("When off, using /rotation Auto again cycles to your next targeting type.", "끄면 /rotation Auto를 다시 썼을 때 다음 대상 지정 방식으로 넘어갑니다.");
		Add("Show changed settings in chat", "바뀐 설정을 채팅창에 표시");
		Add("Prints the setting and its new value when a /rotation Settings command changes it.", "/rotation Settings 명령으로 설정이 바뀌면 설정 이름과 새 값을 출력합니다.");
		Add("Show a toast when a command queues an action", "명령이 기술을 대기열에 넣으면 알림 표시");
		Add("Confirms each action that a /rotation DoAction command adds to the queue.", "/rotation DoAction 명령이 대기열에 넣는 기술마다 확인 알림을 띄웁니다.");
		Add("Special state duration", "특수 상태 지속 시간");
		Add("How long a special state, such as Heal Area or Burst, lasts when a /rotation Special command turns it on.", "/rotation Special 명령으로 켠 광역 회복·버스트 같은 특수 상태가 유지되는 시간입니다.");

		// Auto on / off
		Add("Let RSR turn itself on", "RSR이 스스로 켜지게 허용");
		Add("Needed for the settings below. They turn RSR's autorotation on without any input from you.", "아래 설정에 필요합니다. 아무 입력 없이도 RSR 자동 로테이션이 켜집니다.");
		Add("Turn on Auto mode when a countdown starts", "카운트다운이 시작되면 자동 모드 켜기");
		Add("Turn on Auto mode when your party is in combat", "파티가 전투 중이면 자동 모드 켜기");
		Add("Turn on Auto mode when your alliance is in combat", "얼라이언스가 전투 중이면 자동 모드 켜기");
		Add("Turn on Auto mode in combat in field operations", "필드 작전(보즈야·에우레카·{t:38}) 전투 중이면 자동 모드 켜기");
		Add("Bozja, Eureka, and Occult Crescent FATEs and critical engagements.", "보즈야·에우레카·{t:38}의 FATE와 긴급 교전.");
		Add("Turn on Manual mode when you're attacked", "공격받으면 수동 모드 켜기");
		Add("Use Manual mode instead", "자동 대신 수동 모드로");
		Add("Turn on when a PvP match starts", "PvP 경기가 시작되면 켜기");
		Add("Use the PvP state when turning RSR on in PvP", "PvP에서 RSR을 켤 때 PvP 상태 사용");
		Add("Turn off after combat ends", "전투가 끝나면 끄기");
		Add("Turn off after this long out of combat", "비전투가 이 시간 이상 이어지면 끄기");
		Add("Turn off during area transitions", "지역 이동 중에는 끄기");
		Add("Turn off during cutscenes", "컷신 중에는 끄기");
		Add("Turn off if combat starts before the countdown ends", "카운트다운이 끝나기 전에 전투가 시작되면 끄기");
		Add("Turn off when a PvP match ends", "PvP 경기가 끝나면 끄기");
		Add("Turn off when a duty is completed", "임무가 끝나면 끄기");
		Add("Turn off when you change jobs", "직업을 바꾸면 끄기");
		Add("Turn off when you die", "사망하면 끄기");
		Add("Turn off when you die in PvP", "PvP에서 사망하면 끄기");

		// Targeting
		Add("Which enemies RSR attacks.", "RSR이 공격할 적.");
		Add("Target what the rotation suggests in combat", "전투 중 로테이션이 제안하는 대상을 선택");
		Add("Switches your target to the suggested action's target. Useful for tanks and healers, whose best target can differ from the one you have selected.", "내 대상을 제안된 기술의 대상으로 바꿉니다. 최적 대상이 지금 선택한 대상과 다를 수 있는 탱커·힐러에게 유용합니다.");
		Add("Target the nearest enemy when you have no target", "대상이 없으면 가장 가까운 적을 선택");
		Add("In combat, when nothing is targeted and there's no valid action target nearby. Works in Manual mode too.", "전투 중 대상이 없고 근처에 유효한 기술 대상도 없을 때. 수동 모드에서도 동작합니다.");
		Add("Delay auto-targeting", "자동 대상 지정 지연");
		Add("Auto-target delay range", "자동 대상 지정 지연 범위");
		Add("How long RSR waits before locking onto a newly aggroed or new target. Don't set it too low, or you can pull fresh dungeon mobs off the tank.", "새로 어그로가 끌리거나 새로 나타난 대상을 잡기까지 기다리는 시간입니다. 너무 낮으면 던전에서 갓 끌린 몬스터를 탱커에게서 빼앗을 수 있습니다.");
		Add("Only attack targets on screen", "화면 안의 대상만 공격");
		Add("Only attack targets in your vision cone", "시야 원뿔 안의 대상만 공격");
		Add("Vision cone angle", "시야 원뿔 각도");
		Add("Hard target for all actions", "모든 기술에 직접 선택한 대상 사용");
		Add("When off, RSR only uses the game's soft targeting on allies for heals, shields and the like.", "끄면 회복·보호막 같은 아군 대상 기술에만 게임의 소프트 타겟을 씁니다.");
		Add("Don't target other players' quest enemies", "다른 플레이어의 퀘스트 적은 대상으로 삼지 않음");
		Add("Prioritize quest targets", "퀘스트 대상 우선");
		Add("Prioritize hunt, relic and leve targets", "마물·유물·길드 의뢰 대상 우선");
		Add("Relic targeting is known to be buggy.", "유물 대상 지정은 불안정한 것으로 알려져 있습니다.");
		Add("Prioritize targets with attack markers", "공격 표식이 있는 대상 우선");
		Add("Use AoE on marked targets", "표식 대상에게 광역 기술 사용");
		Add("AoE actions may hit targets with attack markers.", "광역 기술이 공격 표식 대상을 맞혀도 됩니다.");
		Add("Never attack targets with stop markers", "정지 표식 대상은 절대 공격하지 않음");
		Add("Prefer lower-HP targets with the Big targeting type", "'큰 대상' 지정 방식에서 HP가 낮은 쪽 우선");
		Add("When several Big targets are present, pick the one with the lowest HP instead of the highest.", "큰 대상이 여럿이면 HP가 가장 많은 쪽 대신 가장 적은 쪽을 고릅니다.");
		Add("Prefer lower-HP targets with the Small targeting type", "'작은 대상' 지정 방식에서 HP가 낮은 쪽 우선");
		Add("When several Small targets are present, pick the one with the lowest HP instead of the highest.", "작은 대상이 여럿이면 HP가 가장 많은 쪽 대신 가장 적은 쪽을 고릅니다.");
		Add("Prioritize FATE targets", "FATE 대상 우선");
		Add("Only attack FATE enemies in FATEs", "FATE 중에는 FATE 적만 공격");
		Add("In a FATE, only its enemies are targeted. Outside a FATE, FATE enemies are ignored.", "FATE 중에는 그 FATE의 적만, FATE 밖에서는 FATE 적을 무시합니다.");
		Add("Focus Forlorns in FATEs", "FATE에서 Forlorn 집중 공격");
		Add("Ignores every other FATE target while a Forlorn is up.", "Forlorn이 있는 동안 다른 FATE 대상은 모두 무시합니다.");
		Add("Treat your focus target as a party member in alliance raids (Experimental)", "얼라이언스 레이드에서 주시 대상을 파티원으로 취급(실험적)");
		Add("Treat 1 HP targets as invincible", "HP 1인 대상을 무적으로 취급");
		Add("For the rare targets that are invincible without a status saying so.", "상태 표시 없이 무적인 드문 대상을 위한 설정입니다.");
		Add("Ignore invincibility in PvP", "PvP에서 무적 무시");
		Add("Treat target dummies as bosses", "나무인형을 보스로 취급");
		Add("Such as Titan's Heart.", "{n:1802} 같은 부위.");
		Add("Don't apply statuses to status-capped enemies", "상태 효과가 가득 찬 적에게는 상태 효과를 걸지 않음");
		Add("Don't pull new enemies with AoE", "광역 기술로 새 적을 끌어오지 않음");
		Add("Never use an AoE action that could hit enemies that aren't already hostile targets.", "아직 적대 대상이 아닌 적을 맞힐 수 있는 광역 기술은 쓰지 않습니다.");
		Add("Only use actions of an aspect the enemy is weak to", "적이 약한 속성의 기술만 사용");
		Add("Still allow unaspected actions", "무속성 기술은 계속 허용");
		Add("Don't use aspected actions on enemies immune to that aspect", "속성 기술을 그 속성에 면역인 적에게 쓰지 않음");
		Add("Don't use flat damage or death actions on immune enemies", "고정 피해·즉사 기술을 면역인 적에게 쓰지 않음");
		Add("Don't use Stun actions on immune enemies", "{s:2} 기술을 면역인 적에게 쓰지 않음");
		Add("Don't use Sleep actions on immune enemies", "수면 기술을 면역인 적에게 쓰지 않음");
		Add("Don't use Bind actions on immune enemies", "{s:13} 기술을 면역인 적에게 쓰지 않음");
		Add("Don't use Heavy actions on immune enemies", "{s:14} 기술을 면역인 적에게 쓰지 않음");
		Add("Don't use Blind actions on immune enemies", "실명 기술을 면역인 적에게 쓰지 않음");
		Add("Don't use Paralysis actions on immune enemies", "마비 기술을 면역인 적에게 쓰지 않음");
		Add("Don't use Petrification actions on immune enemies", "석화 기술을 면역인 적에게 쓰지 않음");
		Add("Don't use Slow actions on immune enemies", "둔화 기술을 면역인 적에게 쓰지 않음");
		Add("Don't use Interrupt actions on immune enemies", "시전 방해 기술을 면역인 적에게 쓰지 않음");
		Add("Melee ranged action offset", "근접 직업 원거리 기술 여유 거리");
		Add("Melee jobs use their ranged attack only when the target is this much farther than melee range.", "근접 직업은 대상이 근접 사거리보다 이만큼 더 멀 때만 원거리 공격을 씁니다.");
		Add("Only use damaging gap closers within this distance", "피해를 주는 돌진 기술은 이 거리 안에서만 사용");
		Add("Gap closers that deal damage are only used on targets closer than this.", "피해를 주는 돌진 기술은 이 거리보다 가까운 대상에게만 씁니다.");
		Add("Movement target angle", "이동 기술 대상 각도");
		Add("How wide the cone is that movement actions pick their target from: in front of your character, or upward from your character on screen when aiming at the screen center.", "이동 기술이 대상을 고르는 원뿔의 폭입니다. 캐릭터 정면 기준이며, 화면 중앙 조준을 켜면 화면에서 캐릭터 위쪽 기준입니다.");
		Add("Aim movement actions at the screen center", "이동 기술을 화면 중앙으로 조준");
		Add("When off, movement actions aim at what your character is facing.", "끄면 이동 기술이 캐릭터가 바라보는 방향을 조준합니다.");
		Add("Move as far as possible with ground-targeted movement actions", "지면 지정 이동 기술은 최대한 멀리");
		Add("Skips RSR's usual placement logic.", "RSR의 평소 위치 선정 논리를 건너뜁니다.");
		Add("Check movement actions with BossModReborn (Experimental)", "이동 기술을 BossModReborn으로 확인(실험적)");
		Add("Before RSR uses a movement action, or an action that moves you, on its own, ask BossModReborn whether the destination is safe.", "RSR이 스스로 이동 기술이나 몸을 옮기는 기술을 쓰기 전에 도착 지점이 안전한지 BossModReborn에 묻습니다.");
		Add("Check intercepted movement actions with BossModReborn (Experimental)", "가로챈 이동 기술을 BossModReborn으로 확인(실험적)");
		Add("Before RSR uses an intercepted movement action, or an action that moves you, ask BossModReborn whether the destination is safe.", "가로챈 이동 기술이나 몸을 옮기는 기술을 쓰기 전에 도착 지점이 안전한지 BossModReborn에 묻습니다.");

		// Time to kill / dying
		Add("Boss time to kill", "보스 판정 처치 예상 시간");
		Add("Targets with a longer time to kill than this count as bosses.", "처치 예상 시간이 이보다 길면 보스로 봅니다.");
		Add("Dying time to kill", "빈사 판정 처치 예상 시간");
		Add("Targets with a shorter time to kill than this count as dying.", "처치 예상 시간이 이보다 짧으면 빈사로 봅니다.");
		Add("Dying HP", "빈사 판정 HP");
		Add("Targets below this HP count as dying.", "HP가 이보다 낮으면 빈사로 봅니다.");
		Add("Ignore time to kill in PvP", "PvP에서 처치 예상 시간 무시");
		Add("Stop healing when enemies will die within", "적이 이 시간 안에 죽으면 회복 중지");
		Add("In combat, automatic healing stops when the enemies' average time to kill is below this.", "전투 중 적들의 평균 처치 예상 시간이 이보다 짧으면 자동 회복을 멈춥니다.");
		Add("Time DoT refreshes from DoT ticks and time to kill (Experimental)", "지속 피해 갱신을 틱과 처치 예상 시간으로 계산(실험적)");
		Add("On: DoTs with potency data are refreshed on the last GCD that loses no tick, and skipped when the target will die before they out-damage your filler.\nOff: DoTs are refreshed a set number of GCDs before they run out.", "켬: 위력 데이터가 있는 지속 피해는 틱 손실이 없는 마지막 GCD에 갱신하고, 필러보다 피해가 커지기 전에 대상이 죽으면 건너뜁니다.\n끔: 지속 피해가 끝나기 정해진 GCD 수 전에 갱신합니다.");

		// Healing
		Add("Heal yourself first below this HP", "내 HP가 이보다 낮으면 나부터 회복");
		Add("Heal tanks first below this HP", "탱커 HP가 이보다 낮으면 탱커부터 회복");
		Add("Heal healers first below this HP", "힐러 HP가 이보다 낮으면 힐러부터 회복");
		Add("For the second healer in a light party.", "라이트 파티의 두 번째 힐러를 위한 설정입니다.");
		Add("When the lowest party HP is below", "파티 최저 HP가 이보다 낮을 때");
		Add("AoE heal HP spread", "광역 회복 HP 편차");
		Add("The largest standard deviation of party HP at which RSR still uses AoE heals instead of single-target heals. Lower values need the party's HP to be closer together. Adjust only if you want to fine-tune AoE heal behavior.", "RSR이 단일 회복 대신 광역 회복을 쓰는 파티 HP 표준편차의 상한입니다. 낮을수록 파티원 HP가 서로 비슷해야 합니다. 광역 회복을 세밀히 조정할 때만 바꾸세요.");
		Add("Heal party members out of combat", "비전투 중 파티원 회복");
		Add("Heal and raise party NPCs", "파티 NPC 회복·부활");
		Add("Heal solo instance NPCs (Experimental)", "솔로 인스턴스 NPC 회복(실험적)");
		Add("Use GCD heals when there's nothing else to do", "할 일이 없을 때 GCD 회복 사용");
		Add("Hold GCDs for heals below 50% HP (Experimental)", "HP 50% 미만이면 회복을 위해 GCD 보류(실험적)");
		Add("Stops the GCD rotation while you're below 50% HP and have over 2000 MP, so the MP goes to heals.", "HP가 50% 미만이고 MP가 2000을 넘는 동안 GCD 로테이션을 멈춰 MP를 회복에 씁니다.");
		Add("Stop single-target GCD heals once the target is above the threshold (Experimental)", "대상이 기준값을 넘으면 단일 GCD 회복 중단(실험적)");
		Add("If another healer's healing puts the target above the healing threshold while you're casting, the cast is interrupted so you don't waste MP.", "시전 중 다른 힐러의 회복으로 대상이 기준값을 넘으면 시전을 끊어 MP 낭비를 막습니다.");
		Add("Use healing abilities as a non-healer", "힐러가 아닐 때 회복 능력 사용");
		Add("Lets tanks and DPS heal with abilities such as Vercure and Bloodbath.", "탱커·딜러가 {a:7514}·{a:7542} 같은 능력으로 회복하게 합니다.");
		Add("Only heal yourself as a non-healer", "힐러가 아닐 때는 나만 회복");
		Add("Only heal as a non-healer when no healer is alive", "힐러가 아닐 때는 살아 있는 힐러가 없을 때만 회복");
		Add("Tanks and DPS only use healing abilities when the party has no healer, or every healer is dead.", "탱커·딜러는 파티에 힐러가 없거나 힐러가 모두 죽었을 때만 회복 능력을 씁니다.");
		Add("Use Lucid Dreaming below this MP", "MP가 이보다 낮으면 {a:7562} 사용");
		Add("Use HP potions", "HP 물약 사용");
		Add("Use HP potions below this HP", "HP가 이보다 낮으면 HP 물약 사용");
		Add("Use MP potions", "MP 물약 사용");
		Add("Use MP potions below this MP", "MP가 이보다 낮으면 MP 물약 사용");
		Add("When to use gemdraughts and tinctures. Only rotations that support automatic tincture usage follow this.", "영약을 언제 쓸지 정합니다. 영약 자동 사용을 지원하는 로테이션만 따릅니다.");

		// Raise
		Add("When and how RSR should raise dead party members", "죽은 파티원을 언제 어떻게 부활시킬지");
		Add("Raise with Swiftcast or Dualcast", "{a:7561}·{s:1249}로 부활");
		Add("If this is off, RSR never uses Swiftcast or Dualcast to raise.", "끄면 부활에 {a:7561}·{s:1249}를 쓰지 않습니다.");
		Add("Hard-cast raise", "부활 하드캐스트");
		Add("Swiftcast raise lead time", "{a:7561} 부활 선행 시간");
		Add("How long before your next GCD RSR uses Swiftcast to raise. Waiting gives a co-healer time to raise first, so your Swiftcast isn't wasted.", "다음 GCD 몇 초 전에 {a:7561} 부활을 쓸지입니다. 기다리면 동료 힐러가 먼저 부활시킬 시간이 생겨 {a:7561}가 낭비되지 않습니다.");
		Add("Raise before healing or mitigating", "회복·경감보다 부활 먼저");
		Add("Don't raise below this MP", "MP가 이보다 낮으면 부활하지 않음");
		Add("Raise delay range", "부활 지연 범위");
		Add("RSR waits a random time in this range before raising.", "부활 전에 이 범위의 무작위 시간을 기다립니다.");
		Add("Raise players with Brink of Death", "{s:44} 상태인 플레이어도 부활");
		Add("Raise non-healers from the bottom of the party list up", "힐러가 아닌 파티원은 파티 목록 아래부터 부활");
		Add("Raise Red Mages and Summoners first when no tanks or healers are dead", "탱커·힐러가 죽지 않았으면 적마도사·소환사부터 부활");
		Add("Use Phoenix Down (Experimental)", "{i:4570} 사용(실험적)");
		Add("Only when no one who can raise is alive", "부활 가능한 직업이 아무도 살아 있지 않을 때만");

		// Action usage
		Add("Use oGCD abilities", "능력(oGCD) 사용");
		Add("Burst automatically", "버스트 자동 사용");
		Add("RSR keeps the Burst state on, so rotations use their burst abilities as soon as they're ready. Turn this off to time burst yourself with the Burst special state.", "버스트 상태를 항상 켜 두어 버스트 기술을 준비되는 대로 씁니다. 버스트 특수 상태로 직접 타이밍을 맞추려면 끄세요.");
		Add("Use single-target defensive actions", "단일 대상 방어 기술 사용");
		Add("Use AoE defensive actions", "광역 방어 기술 사용");
		Add("Turn this off if you'd rather plan defensive actions yourself, for example in raids.", "레이드처럼 방어 기술을 직접 계획하려면 끄세요.");
		Add("Give tankbuster mitigation to the lower-HP tank", "탱커버스터 경감을 HP가 낮은 탱커에게");
		Add("Use BossModReborn's timeline for proactive mitigation", "BossModReborn 타임라인으로 선제 경감");
		Add("When BossModReborn is loaded, RSR uses its timeline to use defensive actions before raidwides and tankbusters hit.", "BossModReborn이 켜져 있으면 그 타임라인을 보고 광역기·탱커버스터가 오기 전에 방어 기술을 씁니다.");
		Add("Tankbuster mitigation lead time", "탱커버스터 경감 선행 시간");
		Add("How long before a tankbuster to use single-target mitigation.", "탱커버스터 몇 초 전에 단일 경감을 쓸지입니다.");
		Add("Area mitigation lead time", "광역 경감 선행 시간");
		Add("How long before a raidwide to use area mitigation.", "광역기 몇 초 전에 광역 경감을 쓸지입니다.");
		Add("Use anti-knockback actions", "넉백 방지 기술 사용");
		Add("Use Arm's Length and Surecast when an enemy casts an action on the Knockback list.", "적이 넉백 목록의 기술을 시전하면 {a:7548}·{a:7559}를 씁니다.");
		Add("Anti-knockback lead time", "넉백 방지 선행 시간");
		Add("How long before a knockback to use anti-knockback actions.", "넉백 몇 초 전에 넉백 방지 기술을 쓸지입니다.");
		Add("Record enemy knockback actions", "적의 넉백 기술 기록");
		Add("Record enemy AoE actions", "적의 광역 기술 기록");
		Add("Stop acting while an enemy casts a Gaze/Stop action (Experimental)", "적이 시선·정지 기술을 시전하는 동안 행동 중지(실험적)");
		Add("Uses the actions on the Gaze/Stop list.", "시선/정지 목록의 기술을 기준으로 합니다.");
		Add("Interrupt enemy casts", "적 시전 방해");
		Add("Interrupt delay range", "시전 방해 지연 범위");
		Add("Dispel every dispellable debuff", "해제 가능한 디버프 전부 해제");
		Add("Otherwise, while enemies are nearby, RSR focuses on the debuffs in the Dispellable debuffs list.", "끄면 적이 근처에 있는 동안 해제 가능 디버프 목록의 디버프에만 집중합니다.");
		Add("Dispel delay range", "디버프 해제 지연 범위");
		Add("RSR waits a random time in this range before dispelling.", "디버프 해제 전에 이 범위의 무작위 시간을 기다립니다.");
		Add("Use tank stance", "탱커 자세 사용");
		Add("Provoke when there is another tank in the party", "파티에 다른 탱커가 있으면 {a:7533}");
		Add("Provoke an enemy that is attacking a non-tank party member, while there is more than one tank in the party.", "파티에 탱커가 둘 이상일 때 탱커가 아닌 파티원을 공격하는 적에게 {a:7533}를 씁니다.");
		Add("Provoke any enemy not on the Don't provoke list", "{a:7533} 금지 목록에 없는 모든 적에게 {a:7533}");
		Add("Skips RSR's usual checks for whether an enemy is worth provoking.", "{a:7533}할 가치가 있는지 따지는 RSR의 평소 검사를 건너뜁니다.");
		Add("Provoke delay range", "{a:7533} 지연 범위");
		Add("Use tank mitigation below this HP", "HP가 이보다 낮으면 탱커 경감 사용");
		Add("Use tank mitigation when this many enemies target you", "나를 노리는 적이 이 수 이상이면 탱커 경감 사용");
		Add("Use invulnerability below this HP", "HP가 이보다 낮으면 무적기 사용");
		Add("Use True North", "{a:7546} 사용");
		Add("Use movement speed abilities out of combat in duties", "임무 안 비전투 중 이동 속도 능력 사용");
		Add("Use movement speed abilities out of combat outside duties", "임무 밖 비전투 중 이동 속도 능력 사용");
		Add("Sprint when a party tank sprints (non-tanks only)", "파티 탱커가 {a:3} 중이면 함께 {a:3}(탱커 외)");
		Add("Sprint with no target, even near enemies (Experimental)", "대상이 없으면 적 근처에서도 {a:3}(실험적)");
		Add("Use duty actions", "임무 기술 사용");
		Add("Allow the duty rotation to use duty actions, such as Bozja Lost Actions, Occult Crescent Phantom actions and Variant actions. Manually used or intercepted duty actions are unaffected.", "임무 로테이션이 보즈야 로스트 액션, {t:38} 팬텀 기술, 변형 던전 기술 같은 임무 기술을 쓰게 합니다. 직접 쓰거나 가로챈 임무 기술에는 영향이 없습니다.");
		Add("Use them while moving", "이동 중에도 사용");
		Add("Always place them on yourself", "항상 내 위치에 설치");
		Add("Place them on the party tank when there is one", "파티 탱커가 있으면 탱커 위치에 설치");
		Add("Don't break Passage of Arms", "{a:7385} 유지");
		Add("While Passage of Arms is up for AoE mitigation, RSR won't use actions that would end it.", "광역 경감용 {a:7385} 중에는 그것을 끝내는 기술을 쓰지 않습니다.");
		Add("Don't break Collective Unconscious", "{a:3613} 유지");
		Add("While Collective Unconscious is up for AoE mitigation, RSR won't use actions that would end it.", "광역 경감용 {a:3613} 중에는 그것을 끝내는 기술을 쓰지 않습니다.");
		Add("Don't break Phantom Flurry", "{a:23288} 유지");
		Add("While Phantom Flurry is channeling, RSR won't use actions that would end it.", "{a:23288} 시전 중에는 그것을 끝내는 기술을 쓰지 않습니다.");

		// PvP
		Add("Use Guard below this HP", "HP가 이보다 낮으면 {a:29054} 사용");
		Add("Don't use actions while Guard is up", "{a:29054} 중에는 기술을 쓰지 않음");
		Add("Cancel casts when the target gains Guard", "대상이 {s:3054} 상태가 되면 시전 취소");
		Add("Actions that ignore Guard keep casting.", "{s:3054} 무시 기술은 시전을 계속합니다.");
		Add("Use Purify on Stun", "{s:2}에 {a:29056} 사용");
		Add("Use Purify on Heavy", "{s:14}에 {a:29056} 사용");
		Add("Use Purify on Bind", "{s:13}에 {a:29056} 사용");
		Add("Use Purify on Silence", "{s:7}에 {a:29056} 사용");
		Add("Use Purify on Deep Freeze", "{s:487}에 {a:29056} 사용");
		Add("Use Purify on Miracle of Nature", "{s:3085}에 {a:29056} 사용");

		// Interception
		Add("Intercept your action presses (PvE only)", "내가 누른 기술 가로채기(PvE 전용)");
		Add("Let RSR take the actions you press and use them at the right moment", "내가 누른 기술을 RSR이 받아 적절한 순간에 사용");
		Add("Actions you press are queued, and RSR uses them at the right moment in its rotation.", "누른 기술은 대기열에 들어가고, RSR이 로테이션의 알맞은 순간에 씁니다.");
		Add("Intercept weaponskills", "무기 기술 가로채기");
		Add("Intercept spells", "마법 가로채기");
		Add("Intercept abilities", "능력 가로채기");
		Add("Intercept actions in macros", "매크로 안의 기술 가로채기");
		Add("Intercept actions that are on cooldown", "재사용 대기 중인 기술도 가로채기");
		Add("How long RSR keeps trying an intercepted action", "가로챈 기술을 얼마나 오래 시도할지");
		Add("Also pass your press to the game", "누른 입력을 게임에도 전달");
		Add("The game handles your press as well as RSR. This causes clipping.", "게임과 RSR이 둘 다 입력을 처리합니다. 클리핑이 생깁니다.");
		Add("Let BossModReborn Cooldown Planner actions through (Experimental)", "BossModReborn 재사용 계획표 기술은 통과(실험적)");
		Add("When BossModReborn is loaded, actions planned in its Cooldown Planner pass through the intercept system while their window is active.", "BossModReborn이 켜져 있으면 재사용 계획표에 잡힌 기술은 그 창이 열린 동안 가로채기를 통과합니다.");
		Add("Cooldown Planner look-ahead", "재사용 계획표 선행 확인 시간");
		Add("How far ahead to look for upcoming Cooldown Planner actions.", "다가오는 재사용 계획표 기술을 얼마나 앞서 볼지입니다.");
		Add("PvE only. The PvP setting is under Duty > PvP.", "PvE 전용. PvP 설정은 임무 > PvP에 있습니다.");
		Add("Only enable as needed.", "필요할 때만 켜세요.");
		Add("Overrides the engage settings.", "교전 설정보다 우선합니다.");

		// Timing
		Add("Action ahead", "선입력 시점");
		Add("How much of the GCD is left when RSR starts queuing the next GCD. This controls how many oGCDs RSR tries to fit in a GCD window: lower values fit more oGCDs, but can clip the GCD.", "남은 GCD가 얼마일 때 다음 GCD를 대기열에 넣기 시작할지입니다. GCD 틈에 능력을 몇 개 넣으려 할지가 여기에 달렸습니다. 낮을수록 능력이 더 들어가지만 GCD가 밀릴 수 있습니다.");
		Add("Delay between clicks", "클릭 사이 지연");
		Add("RSR waits a random time in this range between using actions. It also spaces out the simulated hotbar presses.", "기술 사용 사이에 이 범위의 무작위 시간을 기다립니다. 모의 핫바 입력 간격에도 적용됩니다.");
		Add("Countdown lead time", "카운트다운 선행 시간");
		Add("How long before the countdown ends RSR starts casting or attacking.", "카운트다운이 끝나기 몇 초 전에 시전이나 공격을 시작할지입니다.");
		Add("Stop casting delay range", "시전 중단 지연 범위");
		Add("Stop casting when the target dies", "대상이 죽으면 시전 중단");
		Add("Stop for the whole cast", "시전 전체 동안 정지");
		Add("Stop this long before the cast finishes", "시전이 끝나기 이만큼 전까지 정지");
		Add("Out of combat delay range", "비전투 지연 범위");
		Add("How long after combat ends RSR waits before using out-of-combat actions, such as speed buffs.", "전투가 끝난 뒤 이동 속도 버프 같은 비전투 기술을 쓰기까지 기다리는 시간입니다.");
		Add("Downtime healing delay range", "휴식 구간 회복 지연 범위");
		Add("Minimum update interval", "최소 갱신 간격");
		Add("The minimum time between RSR's updates. Raising it can help your framerate but can hurt rotation performance.", "RSR 갱신 사이의 최소 시간입니다. 올리면 프레임에는 도움이 되지만 로테이션 성능이 떨어질 수 있습니다.");

		// Tweaks
		Add("Remove lag-induced animation lock delay from instant casts (read tooltip!)", "즉시 시전의 지연 유발 애니메이션 잠금 제거(도움말을 꼭 읽으세요!)");
		Add("Max simulated animation lock delay (ms) (read tooltip!)", "모의 애니메이션 잠금 지연 최대값(ms)(도움말을 꼭 읽으세요!)");
		Add("Delay smoothing factor", "지연 평활 계수");
		Add("Remove framerate-induced cooldown delay", "프레임 때문에 생기는 재사용 대기 지연 제거");
		Add("Dynamically adjusts cooldown and animation locks to ensure queued actions resolve immediately regardless of framerate limitations.", "재사용 대기시간과 애니메이션 잠금을 동적으로 보정해 프레임 제한과 상관없이 대기열의 기술이 바로 나가게 합니다.");
		Add("Lock movement while casting", "시전 중 이동 잠금");
		Add("Stops your character from moving while casting, and while using the actions ticked below.", "시전 중과 아래에서 체크한 기술을 쓰는 동안 캐릭터가 움직이지 못하게 합니다.");
		Add("Modifier key that unlocks movement", "이동 잠금을 푸는 보조 키");
		Add("RB is for gamepad players.", "RB는 게임패드용입니다.");
		Add("Unlock movement while holding both mouse buttons", "마우스 양쪽 버튼을 함께 누르면 이동 잠금 해제");
		Add("Occult Crescent - Track elemental weaknesses", "{t:38} - 속성 약점 추적");
		Add("Tracks enemies' elemental weaknesses so you can report them to the developers. See Debug > Occult Crescent Weaknesses.", "개발자에게 보고할 수 있도록 적의 속성 약점을 기록합니다. 디버그 > {t:38} 약점 참조.");
		Add("Enable the action tracer (only at a developer's request)", "기술 추적기 켜기(개발자가 요청할 때만)");
		Add("Mirror tracer output to the Dalamud log", "추적기 출력을 Dalamud 로그에도 기록");
		Add("Count RSR's action clicks", "RSR의 기술 클릭 횟수 세기");
		Add("Shows how many times RSR has clicked actions for you on the About page.", "RSR이 대신 기술을 클릭한 횟수를 정보 페이지에 보여 줍니다.");

		// UI
		Add("Show Control window", "제어 창 표시");
		Add("Show Next Action window", "다음 기술 창 표시");
		Add("Show Intercepted Action window", "가로챈 기술 창 표시");
		Add("Shows the action you pressed that RSR is waiting to use.", "내가 눌러 RSR이 쓰기를 기다리는 기술을 보여 줍니다.");
		Add("Show Action Timeline window", "기술 타임라인 창 표시");
		Add("Show State window", "상태 창 표시");
		Add("Show RSR's state in the server info bar", "서버 정보 표시줄에 RSR 상태 표시");
		Add("Server info bar style", "서버 정보 표시줄 양식");
		Add("Show a toast when RSR's state changes", "RSR 상태가 바뀌면 알림 표시");
		Add("Show the changelog after an update", "업데이트 뒤 변경 내역 표시");
		Add("Show tips at the top of the settings window", "설정 창 위쪽에 팁 표시");
		Add("A different tip shows every 7 seconds.", "7초마다 다른 팁이 나옵니다.");
		Add("Only show RSR windows in duties or near enemies", "임무 안이거나 적이 근처에 있을 때만 RSR 창 표시");
		Add("Applies to the Control, Next Action, Intercepted Action and Action Timeline windows, and to teaching mode. Enemies count as near within 25 yalms.", "제어·다음 기술·가로챈 기술·기술 타임라인 창과 교육 모드에 적용됩니다. 25m 안의 적을 근처로 봅니다.");
		Add("Lock info windows in place", "정보 창 위치 고정");
		Add("Applies to the Control and Next Action windows.", "제어 창과 다음 기술 창에 적용됩니다.");
		Add("Make info windows click-through", "정보 창 클릭 통과");
		Add("Applies to the Next Action, Intercepted Action and Action Timeline windows.", "다음 기술·가로챈 기술·기술 타임라인 창에 적용됩니다.");
		Add("Applies to the Next Action and Intercepted Action windows.", "다음 기술 창과 가로챈 기술 창에 적용됩니다.");
		Add("Info window background", "정보 창 배경");
		Add("Background when unlocked", "잠금 해제 시 배경");
		Add("Background when locked", "잠금 시 배경");
		Add("GCD bar height", "GCD 막대 높이");
		Add("Always show cooldowns on action icons", "기술 아이콘에 재사용 대기시간 항상 표시");
		Add("Draws cooldowns on the Control window's action icons the way the game's hotbars do.", "제어 창의 기술 아이콘에 게임 핫바처럼 재사용 대기시간을 그립니다.");
		Add("Use the game's cooldown overlay", "게임의 재사용 대기 표시 사용");
		Add("Show keybind (Experimental)", "단축키 표시(실험적)");
		Add("Shows the suggested target's name below the Next Action icon: orange if you don't have it selected, green if you do.", "다음 기술 아이콘 아래에 제안 대상의 이름을 보여 줍니다. 선택하지 않았으면 주황, 선택했으면 초록입니다.");
		Add("Lock Control window", "제어 창 잠금");
		Add("Show button presses on hotbars", "핫바에 누름 효과 표시");
		Add("Plays the pressed effect on the hotbar slot when RSR uses an action.", "RSR이 기술을 쓰면 해당 핫바 칸에 누름 효과를 재생합니다.");
		Add("Highlights the action RSR would use next on your hotbars.", "RSR이 다음에 쓸 기술을 핫바에서 강조합니다.");
		Add("Highlight color", "강조 색");
		Add("Tint disabled actions on hotbars", "핫바에서 비활성 기술 색 입히기");
		Add("Actions you have disabled in RSR are tinted on the game's hotbars.", "RSR에서 끈 기술을 게임 핫바에서 색으로 표시합니다.");
		Add("Tint color", "색 입히기 색상");
		Add("Add Enable/Disable to the action right-click menu", "기술 우클릭 메뉴에 켜기/끄기 추가");
		Add("Adds an entry to the game's action context menu that enables or disables the action in RSR.", "게임의 기술 메뉴에 RSR에서 그 기술을 켜거나 끄는 항목을 추가합니다.");
		Add("Includes chaotic alliance raids.", "혼돈 얼라이언스 레이드를 포함합니다.");

		// Duty logic (names: part 2 ids)
		Add("Dohn Mheg - Liar's Lyre logic", "{c:649} - {n:8958} 로직");
		Add("Crucible of the Unbroken - Siren logic", "마수 시련장 - 세이렌 로직");
		Add("Forked Tower: Blood - Dead Stars logic", "포크 타워: 피의 탑 - 죽은 별 로직");
		Add("Forked Tower: Magic - First boss Hero/Villain logic", "포크 타워: 마의 탑 - 1보스 영웅/악당 로직");
		Add("North Horn CE - Tiny Mage targeting logic", "{t:38} 북부편 CE - 꼬마 마도사 대상 로직");
		Add("The Ghimlyt Dark - Colossus Rubricatus logic", "{c:611} - 콜로서스 루브리카투스 로직");
		Add("The Meso Terminal - Jailer logic", "{c:1028} - 간수 로직");
		Add("The Sil'dihn Subterrane - Drake ads logic", "{c:868} - 드레이크 쫄 로직");
		Add("Prioritize A-tier tomeliths in Shatter", "쉐터에서 A급 토멜리스 우선");
		Add("Prioritize B-tier tomeliths in Shatter", "쉐터에서 B급 토멜리스 우선");
		Add("Prioritize Crawling Pieces, which bind you in place.", "제자리에 묶는 기어다니는 조각을 우선합니다.");
		Add("Prioritize Red Hot if you have the firesnaking buff, and Deep Blue if you have the watersnaking buff (also applies to Normal).", "{s:4974} 버프가 있으면 {n:14370} 우선, {s:4975} 버프가 있으면 {n:14369} 우선(일반 난이도에도 적용).");
		Add("Prioritize the Sahagin Piece until it has Paralyzing Spikes, then the Ymir Shell, then the Ymir Piece once its shell is gone. Ymir is treated as immune while it has Vulnerability Down, and the spiked Sahagin while Ymir is up.", "사하긴 조각에 마비 가시가 생길 때까지 사하긴 조각을, 다음은 이미르 껍질을, 껍질이 사라지면 이미르 조각을 우선합니다. 이미르는 받는 피해 감소가 있는 동안, 가시 돋은 사하긴은 이미르가 살아 있는 동안 면역으로 봅니다.");
		Add("Prioritizes limited-time mobs in treasure dungeons to resolve the extra loot mechanic.", "보물 던전에서 추가 보상 기믹을 위해 시간제한 몬스터를 우선합니다.");
		Add("Sets mobs with number plates as top priority, from lowest to highest, to resolve the extra loot mechanic.", "추가 보상 기믹을 위해 번호판 몬스터를 낮은 번호부터 최우선으로 둡니다.");
		Add("Treat Bismarck Shell/Bismarck Corona as immune if you don't have the Whaleback status.", "{s:719} 상태가 없으면 비스마르크 껍질/비스마르크 코로나를 면역으로 취급.");
		Add("Treat Cloud of Darkness/Stygian as immune if you don't have the corresponding buff.", "대응하는 버프가 없으면 어둠의 구름/스티기안을 면역으로 취급.");
		Add("Treat Colossus Rubricatus as immune while it's casting the scripted action that leads to its death.", "콜로서스 루브리카투스가 죽음으로 이어지는 연출 기술을 시전하는 동안 면역으로 취급.");
		Add("Treat Eminent Grief as immune if you don't have the Light Vengeance buff, and treat Devoured Eater as immune if you don't have the Dark Vengeance buff.", "{s:4560} 버프가 없으면 {n:14037}, {s:4559} 버프가 없으면 {n:14038}를 면역으로 취급.");
		Add("Treat Irminsul and Sawtooth as immune if you don't have the corresponding buff.", "대응하는 버프가 없으면 {n:4623}·{n:4624} 모두 면역으로 취급.");
		Add("Treat Jailers in the second boss fight as immune if you don't have the corresponding buff.", "2보스전에서 대응하는 버프가 없으면 간수를 면역으로 취급.");
		Add("Treat Liar's Lyre as immune if you don't have the Unfooled status.", "{s:386} 상태가 없으면 {n:8958} 면역으로 취급.");
		Add("Treat Thanatos as immune if you don't have the Astral Realignment buff.", "{s:398} 버프가 없으면 {n:710} 면역으로 취급.");
		Add("Treat certain drakes as immune so they die in the order needed for Variant path 12.", "변형 던전 12번 경로에 필요한 순서로 죽도록 특정 드레이크를 면역으로 취급.");
		Add("Treat each Ark Angel as immune if you don't have the corresponding buff.", "대응하는 버프가 없으면 각 아크 엔젤을 면역으로 취급.");
		Add("Treat each Superior Flight Unit as immune if you don't have the corresponding buff.", "대응하는 버프가 없으면 각 상위 비행 유닛을 면역으로 취급.");

		// Entries whose English changed only trivially (matched to part 2 automatically)
		Add("Cinder Drift Extreme - Pall targeting logic", "{c:718} - 장막(Pall) 대상 로직");
		Add("Cloud of Darkness - Ads phase logic", "{c:1010} - 쫄 페이즈 로직");
		Add("Crucible of the Unbroken - Ymir logic", "마수 시련장 - 이미르 로직");
		Add("Dancing Mad (Ultimate) - Hero/Villain logic", "{c:1094} - 영웅/악당 로직");
		Add("Futures Rewritten (Ultimate) - Crystal of Darkness logic", "{c:1006} - {n:13556} 로직");
		Add("Jeuno: The First Walk - The Ark Angels logic", "{c:1015} - 아크 엔젤 로직");
		Add("Limitless Blue Extreme - Whaleback logic", "{c:89} - {s:719} 로직");
		Add("M10S - Firesnaking/Watersnaking targeting logic", "M10S - {s:4974}/{s:4975} 대상 로직");
		Add("M8S - Wolf Pack/Stone Pack logic", "M8S - {n:13846}/{n:13847} 로직");
		Add("M9S - Ads targeting logic", "M9S - 쫄 대상 로직");
		Add("M9S - Cell targeting logic", "M9S - 세포 대상 로직");
		Add("M9S - Only use cleave logic", "M9S - 범위 공격만 사용 로직");
		Add("O12S - Packet Filter logic", "O12S - {s:1660}/{s:1661} 로직");
		Add("Pilgrim's Traverse - Eminent Grief logic", "노르브란트 순례길 - {n:14037} 로직");
		Add("The Epic of Alexander (Ultimate) - Jagd Doll logic", "{c:694} - {n:3759} 로직");
		Add("The Epic of Alexander (Ultimate) - True Heart logic", "{c:694} - {n:4694} 로직");
		Add("The Labyrinth of the Ancients - Thanatos logic", "{c:92} - {n:710} 로직");
		Add("The Omega Protocol (Ultimate) - Packet Filter logic", "{c:908} - {s:1660}/{s:1661} 로직");
		Add("The Puppets' Bunker - Superior Flight Unit logic", "{c:736} - 상위 비행 유닛 로직");
		Add("The Tower at Paradigm's Breach - Hansel and Gretel logic", "{c:779} - {n:9988}·{n:9989} 로직");
		Add("The Unmaking Extreme - Shadow logic", "{c:1116} - 그림자 로직");
		Add("The Void Ark - Irminsul and Sawtooth logic", "{c:120} - {n:4623}·{n:4624} 로직");
		Add("Treasure dungeons - Numbered mob logic", "보물 던전 - 번호 몬스터 로직");
		Add("Treasure dungeons - Timed mob logic", "보물 던전 - 시간제한 몬스터 로직");
		Add("Windurst: The Third Walk - Alexander Resurrected logic", "{c:1117} - 부활한 {n:5174} 로직");
		Add("Treat Shadow as immune if you don't have the corresponding status.", "대응하는 상태가 없으면 그림자를 면역으로 취급.");
		Add("Treat Crystal of Darkness ad as immune.", "{n:13556} 쫄을 면역으로 취급.");
	}
}
