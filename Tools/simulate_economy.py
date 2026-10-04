# Snapshot of the approved initial economy. Update these tables and goals when retuning LotteryEconomy.cs.
import random, statistics, json
PRICES=[10,25,60,100,250,500]
UNLOCKS=[0,150,600,1800,5000,14000]
POOLS=[[0,5,10,20,50],[0,0,25,25,50,50,75,100],[0,0,30,60,90,120,180,240],[0,0,0,200,200,200,600],[0,0,250,250,500,750,1000],[0,0,0,700,700,1400,2800]]
BONUSES=[[14,21,28],[30,45,60],[60,90,120],[140,210,280],[280,420,560],[600,900,1200]]
W_SPEED=[60,100,170,280,450]; W_CAP=[90,160,280,480,800]; W_SLOTS=[5,8,12,18,27,40]
S_SPEED=[400,900,2200,5200,12000]; S_CAP=[80,200,500,1200,2800]
GOALS=[('sponge',50),('ticket1',150),('washer',250),('wcap',90),('wspeed',60),('multiple',75),('ticket2',600),('scratcher',1200),('scap',80),('sspeed',400),('ticket3',1800),('wcap',160),('wspeed',100),('sspeed',900),('ticket4',5000),('wcap',280),('wspeed',170),('scap',200),('sspeed',2200),('ticket5',14000),('wcap',480),('wspeed',280),('wcap',800),('wspeed',450),('sspeed',5200),('sspeed',12000),('scap',500),('scap',1200),('scap',2800)]
def simulate(seed,manual):
 r=random.Random(seed); cash=0; counts=[0]*6; tier=0; goal=0; washer=False; auto=False
 ws=wc=ss=sc=0; wt=0; ticket_time=0; prize=None; pending_kind=None; stages={}
 # Conservative active play: one ticket at a time, automatic machine retains serial processing.
 for sec in range(7201):
  if washer:
   wt+=1
   if wt >= 10-ws+.8: cash+=W_SLOTS[wc]; wt-=10-ws+.8
  if prize is not None and sec>=ticket_time:
   cash+=prize; counts[pending_kind]+=1
   if counts[pending_kind] in [10,25,50]: cash+=BONUSES[pending_kind][[10,25,50].index(counts[pending_kind])]
   prize=None
  if goal<len(GOALS):
   key,cost=GOALS[goal]
   future_tier=int(key[-1]) if key.startswith('ticket') else tier
   reserve=PRICES[future_tier]*3
   if cash>=cost+reserve:
    cash-=cost; goal+=1
    if key.startswith('ticket'): tier=int(key[-1]); stages[key]=sec/60
    elif key=='washer': washer=True; stages[key]=sec/60
    elif key=='scratcher': auto=True; stages[key]=sec/60
    elif key=='wcap': wc+=1
    elif key=='wspeed': ws+=1
    elif key=='sspeed': ss+=1
    elif key=='scap': sc+=1
    if goal==len(GOALS): stages['complete']=sec/60; return stages
  if prize is None:
   if cash>=PRICES[tier]:
    cash-=PRICES[tier]; pending_kind=tier; prize=r.choice(POOLS[tier]); ticket_time=sec+(8-ss+(.7 if sc==0 else .35) if auto else manual)
   elif sec%2==0: cash+=1 # Free manual dish = fallback; no extra dish income while scratching.
 return {'complete':120}
result={}
for manual in [4,6,10]:
 runs=[simulate(seed,manual) for seed in range(500)]
 rows={}
 for stage in ['ticket1','washer','ticket2','scratcher','ticket3','ticket4','ticket5','complete']:
  vals=sorted(x.get(stage,120) for x in runs)
  rows[stage]={'p10_min':round(vals[50],1),'median_min':round(statistics.median(vals),1),'p90_min':round(vals[450],1)}
 result[str(manual)+'s_manual']=rows
print(json.dumps({'ticket_net_means':[round(sum(p)/len(p)-PRICES[i],2) for i,p in enumerate(POOLS)],'runs_per_cadence':500,'results':result},ensure_ascii=False,indent=2))
