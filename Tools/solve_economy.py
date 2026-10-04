"""40 分钟节奏数值求解器 —— 反解 36 个购买点的价格。

用法：改 SCHED 里的目标时刻，重跑，看价格表与总时长。
背景：2026-10-04 grill-me 定案 —— 分支2 选 B（T 改为 per-purchase 数组），
分支6 选 milestone 改全局倍率（加性、几何级数 d0=0.08 span=5x）。
"""
import statistics

BOOST = 1.10
PV = [1, 2, 3, 5, 8]
W_CAP = [5, 7, 9, 12, 15]
W_SEC = [10, 9, 8, 7, 6, 5]
S_SEC = [20, 18, 16, 15, 13, 12]
MP_BASE = 5
TP = [13, 31, 68, 134, 300, 625]
EMPTY = 0.10
POOLS = [
    [0, 5, 10, 20, 50],
    [0, 0, 25, 25, 50, 50, 75, 100],
    [0, 0, 30, 60, 90, 120, 180, 240],
    [0, 0, 0, 200, 200, 200, 600],
    [0, 0, 250, 250, 500, 750, 1000],
    [0, 0, 0, 700, 700, 1400, 2800],
]
DELTA = [0.08 * 5 ** (i / 5) for i in range(6)]
TAU_YELLOW = 3.0
TAU_PURPLE = 1.5
REPLENISH = 0.8
WIN_TARGET = 2_000_000


def mean_prize(pool):
    empty = sum(1 for x in pool if x <= 0)
    pos = [x for x in pool if x > 0]
    return (1 - max(0.0, empty / len(pool) - EMPTY)) * sum(pos) / len(pos)


MEAN = [mean_prize(p) for p in POOLS]

# (key, 目标秒数, 显示名)
SCHED = [
    ("sponge", 240, "PURPLE SPONGE"),
    ("multi", 300, "MULTIPLE PLATES"),
    ("pv1", 360, "PLATE VALUE 1-2"),
    ("pv2", 430, "PLATE VALUE 2-3"),
    ("gold", 600, "UNLOCK GOLD"),
    ("pv3", 660, "PLATE VALUE 3-5"),
    ("pv4", 720, "PLATE VALUE 5-8"),
    ("mp1", 780, "MULTI PLATES 5-10"),
    ("mp2", 840, "MULTI PLATES 10-15"),
    ("nova", 890, "UNLOCK NOVA"),
    ("mp3", 920, "MULTI PLATES 15-20"),
    ("washer", 900, "UNLOCK WASHER"),
    ("ws1", 960, "WASHER SPEED 10-9s"),
    ("wc1", 1020, "WASHER CAPACITY 5-7"),
    ("hm", 1080, "UNLOCK HEARTMATCH"),
    ("ws2", 1140, "WASHER SPEED 9-8s"),
    ("wc2", 1180, "WASHER CAPACITY 7-9"),
    ("scr", 1200, "UNLOCK SCRATCHER"),
    ("ws3", 1260, "WASHER SPEED 8-7s"),
    ("ss1", 1320, "SCRATCHER SPEED 20-18s"),
    ("cc", 1380, "UNLOCK CROSSCODE"),
    ("ss2", 1440, "SCRATCHER SPEED 18-16s"),
    ("wc3", 1500, "WASHER CAPACITY 9-12"),
    ("ss3", 1560, "SCRATCHER SPEED 16-15s"),
    ("sc1", 1620, "SCRATCHER QUEUE 2"),
    ("sc2", 1660, "SCRATCHER QUEUE 3"),
    ("auto", 1740, "AUTO FEED"),
    ("zz", 1800, "UNLOCK ZIGZAG"),
    ("ss4", 1860, "SCRATCHER SPEED 15-13s"),
    ("ss5", 1900, "SCRATCHER SPEED 13-12s"),
    ("sc3", 1980, "SCRATCHER QUEUE 4"),
    ("sc4", 2040, "SCRATCHER QUEUE 5"),
    ("sc5", 2100, "SCRATCHER QUEUE 6"),
    ("ws4", 2160, "WASHER SPEED 7-6s"),
    ("ws5", 2220, "WASHER SPEED 6-5s"),
    ("wc4", 2250, "WASHER CAPACITY 12-15"),
]
SCHED.sort(key=lambda x: x[1])
UNLOCK_KEYS = ["gold", "nova", "hm", "cc", "zz"]


def levels(s):
    return (
        (s["mp"] if s["multi"] else 0)
        + (1 if s["multi"] else 0)
        + s["pv"]
        + (s["ws"] + s["wc"] if s["washer"] else 0)
        + (s["ss"] + s["sc"] if s["scr"] else 0)
        + sum(1 for k in range(1, 6) if s["tk"][k])
        + (1 if s["sponge"] else 0)
        + (1 if s["auto"] else 0)
    )


def plate_capacity(s):
    return 1 if not s["multi"] else MP_BASE * (s["mp"] + 1)


def milestone_mult(s):
    return 1.0 + sum(DELTA[k] * min(3, s["cnt"][k]) for k in range(6))


def rate(s):
    mult = BOOST ** levels(s) * milestone_mult(s)
    pv = PV[s["pv"]]
    tau = TAU_PURPLE if s["sponge"] else TAU_YELLOW
    duty = 0.45 if (s["washer"] or s["scr"]) else 0.85
    r = min(plate_capacity(s) * pv * mult / tau, pv * mult / tau * duty)
    if s["washer"]:
        r += W_CAP[s["wc"]] * pv * mult / (W_SEC[s["ws"]] + REPLENISH)
    if s["scr"]:
        k = max([i for i in range(6) if s["tk"][i] or i == 0])
        while k > 0 and not s["tk"][k]:
            k -= 1
        r += max(0.0, MEAN[k] * mult - TP[k]) / S_SEC[s["ss"]]
    return r


def apply(s, key):
    if key == "sponge":
        s["sponge"] = True
    elif key == "multi":
        s["multi"] = True
    elif key == "auto":
        s["auto"] = True
    elif key == "washer":
        s["washer"] = True
    elif key == "scr":
        s["scr"] = True
    elif key.startswith("pv"):
        s["pv"] += 1
    elif key.startswith("mp"):
        s["mp"] += 1
    elif key.startswith("ws"):
        s["ws"] += 1
    elif key.startswith("wc"):
        s["wc"] += 1
    elif key.startswith("ss"):
        s["ss"] += 1
    elif key.startswith("sc"):
        s["sc"] += 1
    elif key in UNLOCK_KEYS:
        s["tk"][UNLOCK_KEYS.index(key) + 1] = True


def new_state():
    return dict(
        multi=False, mp=0, pv=0, washer=False, ws=0, wc=0,
        scr=False, ss=0, sc=0, sponge=False, auto=False,
        tk=[False] * 6, cnt=[0] * 6,
    )


DT = 0.25


def solve():
    s = new_state()
    cash = 0.0
    t = 0.0
    rows = []
    pending = [(k, tt, lbl) for k, tt, lbl in SCHED]
    while pending and t < SCHED[-1][1]:
        r = rate(s)
        cash += r * DT
        t += DT
        for k in range(1, 6):
            if s["tk"][k]:
                s["cnt"][k] += r * DT / (S_SEC[0] + 2.0) * (TP[k] / MEAN[k]) * 0.4
        if pending and t >= pending[0][1]:
            key, tt, label = pending.pop(0)
            r = rate(s)
            rows.append((tt, label, cash, cash / r if r > 0 else 0.0, r))
            cash = 0.0
            apply(s, key)
    return rows, s


def main():
    rows, s = solve()
    print("时刻   购买项                      价格         回本T      当时速率")
    for tt, label, cost, payback, r in rows:
        print(
            "%2d:%02d  %-26s %11s %8.0fs %10s/s"
            % (tt // 60, tt % 60, label, format(int(cost), ",d"), payback,
               format(int(r), ",d"))
        )
    total = sum(r[2] for r in rows)
    rf = rate(s)
    tail = WIN_TARGET / rf
    last = max(r[0] for r in rows)
    print("")
    print("内容总成本 = $%s" % format(int(total), ",d"))
    print("终局速率Rf = $%s/s   累计倍率 = %.1fx"
          % (format(int(rf), ",d"), BOOST ** levels(s) * milestone_mult(s)))
    print("里程碑 M_max = %.3fx" % milestone_mult(s))
    print("W = $%s 需积累段 = %.0fs = %.1f min"
          % (format(WIN_TARGET, ",d"), tail, tail / 60))
    print("总时长 = %.1f min(最后购买 %.1f min + 积累 %.1f min)"
          % ((last + tail) / 60, last / 60, tail / 60))


if __name__ == "__main__":
    main()