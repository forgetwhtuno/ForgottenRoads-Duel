#!/usr/bin/env python3
from pathlib import Path
import sys
ROOT=Path(__file__).resolve().parents[1]
read=lambda p:(ROOT/p).read_text(encoding='utf-8')
api=read('src/PracticeDuelIntegrationApi.cs'); old=read('src/DuelControlApi.cs')
events=read('src/DuelEventContract.cs'); plugin=read('src/ErenshorDuelPlugin.cs'); ctl=read('src/DuelController.cs')
checks=[]
def ck(n,v): checks.append((n,bool(v)))
ck('legacy DuelControlApi stays v1', 'public const int ApiVersion = 1;' in old and 'TryChallenge(string simName)' in old)
ck('separate integration API v1', 'public static class PracticeDuelIntegrationApi' in api and 'ContractVersion = 1' in api)
ck('autonomous request primitive result', all(x in api for x in ['RequestId','Status','ReasonToken','RequestAutonomousChallenge']))
ck('event contract additive v3', 'ContractVersion = 3' in events and all(('public string '+x) in events for x in ['RequestId','Origin','Source']))
ck('mechanical rejection terminal event', 'duel_request_rejected' in events and 'RejectAutonomousRequest' in ctl)
ck('autonomous origin mapped by plugin', 'DuelRequestOrigin.Autonomous, request.RequestId, request.Source' in plugin)
ck('explicit control remains explicit', 'DuelController.Start(sim, DuelRequestOrigin.ExplicitPlayer)' in plugin)
ck('autonomous presentation is not player command', 'proposes a practice duel' in ctl and 'You challenge " + simName' in ctl)
ck('plugin shutdown terminal token', 'plugin_shutdown' in events and 'StopForShutdown' in ctl and 'RejectPendingAutonomous("plugin_shutdown"' in plugin)
ck('correlation flows challenge', 'DuelEventFactory.Challenge(simName, partySim, requestContext)' in ctl)
ck('correlation flows decline', 'DuelEventFactory.Declined(simName, partySim, token, requestContext)' in ctl)
ck('correlation flows terminal', 'DuelEventFactory.Completed' in ctl and '_eventContext' in ctl and 'DuelEventFactory.Cancelled' in ctl)
ck('social cooldown distinction preserved', 'DuelRequestOrigin.Autonomous' in ctl and 'DuelRequestOrigin.ExplicitPlayer' in ctl)
for name,ok in checks: print(('PASS ' if ok else 'FAIL ')+name)
f=[n for n,o in checks if not o]
print(f'Practice Duel Nemesis integration source acceptance: {len(checks)-len(f)}/{len(checks)} pass')
sys.exit(1 if f else 0)
