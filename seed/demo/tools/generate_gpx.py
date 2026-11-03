#!/usr/bin/env python3
"""Reproducible generator for the synthetic demo GPX files (stdlib only).

Usage: python3 seed/demo/tools/generate_gpx.py   (writes ../gpx/*.gpx and prints stats as JSON)

Everything is invented: hand-drawn loops in local metres around a start point, smoothed (Chaikin),
scaled to the target distance, with synthetic elevation and pacing. No third-party data is used.
"""
import json, math, os, random, datetime as dt

R = 6371008.8  # haversine Earth radius (m), mean radius
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "gpx")

def hav(a, b):
    p1, p2 = math.radians(a[0]), math.radians(b[0])
    dphi, dl = p2 - p1, math.radians(b[1] - a[1])
    h = math.sin(dphi / 2) ** 2 + math.cos(p1) * math.cos(p2) * math.sin(dl / 2) ** 2
    return 2 * R * math.asin(math.sqrt(h))

def chaikin(pts, n=2, closed=True):
    for _ in range(n):
        out = []
        m = len(pts) if closed else len(pts) - 1
        for i in range(m):
            p, q = pts[i], pts[(i + 1) % len(pts)]
            out += [(0.75 * p[0] + 0.25 * q[0], 0.75 * p[1] + 0.25 * q[1]),
                    (0.25 * p[0] + 0.75 * q[0], 0.25 * p[1] + 0.75 * q[1])]
        pts = out
    return pts

def to_latlon(origin, xy):
    lat0, lon0 = origin
    return (lat0 + math.degrees(xy[1] / R),
            lon0 + math.degrees(xy[0] / (R * math.cos(math.radians(lat0)))))

def densify(xy, step):
    out = []
    for i in range(len(xy)):
        p, q = xy[i], xy[(i + 1) % len(xy)]
        d = math.hypot(q[0] - p[0], q[1] - p[1])
        n = max(1, int(d / step))
        out += [(p[0] + (q[0] - p[0]) * k / n, p[1] + (q[1] - p[1]) * k / n) for k in range(n)]
    out.append(out[0])  # close the loop
    return out

def build_path(shape, origin, target, step, seed):
    rnd = random.Random(seed)
    ph = [rnd.uniform(0, 6.28) for _ in range(4)]
    base = chaikin(shape)
    scale = 1.0
    for _ in range(30):
        dense = densify([(x * scale, y * scale) for x, y in base], step)
        pts = []
        for i, (x, y) in enumerate(dense):  # slow GPS-like wander, ~2 m
            w = i * step
            x += 2.0 * math.sin(w / 120 + ph[0]) + 1.0 * math.sin(w / 47 + ph[1])
            y += 2.0 * math.sin(w / 110 + ph[2]) + 1.0 * math.sin(w / 53 + ph[3])
            pts.append(to_latlon(origin, (x, y)))
        length = sum(hav(pts[i], pts[i + 1]) for i in range(len(pts) - 1))
        if abs(length - target) / target < 0.002:
            break
        scale *= target / length
    return pts

def cum_dist(pts):
    c = [0.0]
    for i in range(len(pts) - 1):
        c.append(c[-1] + hav(pts[i], pts[i + 1]))
    return c

def elevation(cum, controls, seed, wiggle):
    """controls: [(fraction, metres)] cosine-interpolated; plus slow low-amplitude wiggle."""
    rnd = random.Random(seed)
    ph = [rnd.uniform(0, 6.28) for _ in range(2)]
    total = cum[-1]
    res = []
    for d in cum:
        f = d / total
        for (f0, e0), (f1, e1) in zip(controls, controls[1:]):
            if f0 <= f <= f1:
                t = (f - f0) / (f1 - f0) if f1 > f0 else 0
                e = e0 + (e1 - e0) * (1 - math.cos(math.pi * t)) / 2
                break
        e += wiggle * (math.sin(d / 90 + ph[0]) * 0.6 + math.sin(d / 37 + ph[1]) * 0.4)
        res.append(e)
    return res

def gain_loss(ele, thr=3.0):
    """Hysteresis: count a climb/descent only once it exceeds `thr` metres from the last pivot."""
    gain = loss = 0.0
    pivot = ele[0]
    for e in ele[1:]:
        if e - pivot >= thr:
            gain += e - pivot; pivot = e
        elif pivot - e >= thr:
            loss += pivot - e; pivot = e
        elif (e > pivot and e - pivot < 0) or False:
            pass
    return gain, loss

def times(cum, ele, total_seconds, start, seed):
    """Speed varies with grade, fatigue and slow noise; rescaled so the finish time is exact."""
    rnd = random.Random(seed)
    ph = rnd.uniform(0, 6.28)
    total = cum[-1]
    t = [0.0]
    for i in range(1, len(cum)):
        d = cum[i] - cum[i - 1]
        grade = (ele[i] - ele[i - 1]) / d if d > 0 else 0
        grade = max(-0.25, min(0.25, grade))
        v = 1.0 - 2.2 * grade if grade > 0 else 1.0 - 0.8 * grade
        v *= 1.0 - 0.06 * cum[i] / total            # fatigue
        v *= 1.0 + 0.03 * math.sin(cum[i] / 400 + ph)
        if cum[i] < 300: v *= 0.85 + 0.15 * cum[i] / 300  # start crowd
        t.append(t[-1] + d / v)
    k = total_seconds / t[-1]
    return [start + dt.timedelta(seconds=x * k) for x in t]

def write_gpx(name, title, desc, pts, ele, tms):
    lines = ['<?xml version="1.0" encoding="UTF-8"?>',
             '<gpx version="1.1" creator="Runners Diary synthetic demo generator" xmlns="http://www.topografix.com/GPX/1/1">',
             f'  <metadata><name>{title}</name><desc>{desc}</desc></metadata>',
             f'  <trk><name>{title}</name><trkseg>']
    for i, (la, lo) in enumerate(pts):
        tm = f'<time>{tms[i].strftime("%Y-%m-%dT%H:%M:%SZ")}</time>' if tms else ''
        lines.append(f'    <trkpt lat="{la:.6f}" lon="{lo:.6f}"><ele>{ele[i]:.1f}</ele>{tm}</trkpt>')
    lines += ['  </trkseg></trk>', '</gpx>', '']
    data = "\n".join(lines)
    path = os.path.join(OUT, name)
    with open(path, "w", encoding="utf-8") as f: f.write(data)
    d = cum_dist(pts)[-1]; g, l = gain_loss(ele)
    return {"file": name, "points": len(pts), "bytes": len(data.encode()), "distance_m": round(d),
            "gain_m": round(g), "loss_m": round(l),
            "duration_s": round((tms[-1] - tms[0]).total_seconds()) if tms else None}

# Hand-drawn loops in metres (x east, y north), scaled later to the exact target distance.
SHAPE_10K = [(0,0),(900,100),(1700,500),(2300,1200),(2500,2000),(2100,2700),(1300,2900),(600,2500),(-100,2100),(-700,1400),(-600,600)]
SHAPE_HALF = [(0,0),(1500,-200),(3000,300),(4200,1200),(5200,2500),(5000,3800),(3900,4600),(2500,4800),(1200,4300),(300,3300),(-900,2800),(-1500,1700),(-1200,600)]
SHAPE_TRAIL = [(0,0),(700,500),(1300,1300),(1200,2200),(1900,3000),(2900,3300),(3700,2700),(4000,1700),(3500,800),(2800,200),(2000,-500),(1000,-600)]
SHAPE_CROSS = [(0,0),(500,150),(800,600),(600,1100),(100,1250),(-300,900),(-250,400)]

def reverse(s): return [s[0]] + s[:0:-1]

GLIWICE = (50.2945, 18.6714)
LABEDY = (50.3300, 18.7300)
USTRON = (49.7195, 18.8130)
SILESIA_PARK = (50.2866, 18.6640)

def recorded(name, title, desc, shape, origin, target, step, seed, controls, wiggle, secs, start):
    pts = build_path(shape, origin, target, step, seed)
    cum = cum_dist(pts)
    ele = elevation(cum, controls, seed, wiggle)
    tms = times(cum, ele, secs, start, seed)
    return write_gpx(name, title, desc, pts, ele, tms)

def planned(name, title, desc, shape, origin, target, step, seed, controls, wiggle):
    pts = build_path(shape, origin, target, step, seed)
    cum = cum_dist(pts)
    return write_gpx(name, title, desc, pts, elevation(cum, controls, seed, wiggle), None)

def main():
    os.makedirs(OUT, exist_ok=True)
    D = dt.datetime
    flat = [(0,214),(0.2,222),(0.4,231),(0.55,226),(0.8,218),(1,214)]
    r = []
    r.append(recorded("gliwice-10k.gpx", "Gliwice 10K (synthetic)", "Synthetic 10 km road race loop, Gliwice", SHAPE_10K, GLIWICE, 10000, 11, 1, flat, 0.4, 2970, D(2025,9,14,7,0,0)))
    r.append(recorded("gliwice-half-marathon.gpx", "Gliwice Half Marathon (synthetic)", "Synthetic half marathon loop, Gliwice / Labedy", SHAPE_HALF, LABEDY, 21097.5, 17, 2,
        [(0,224),(0.15,236),(0.3,248),(0.45,232),(0.6,221),(0.8,240),(0.92,230),(1,224)], 0.4, 6820, D(2025,10,12,8,0,0)))
    r.append(recorded("ustron-trail-16k.gpx", "Ustron Trail 16K (synthetic)", "Synthetic hilly trail race, Silesian Beskids near Ustron", SHAPE_TRAIL, USTRON, 16000, 14, 3,
        [(0,360),(0.12,520),(0.22,690),(0.3,860),(0.38,760),(0.48,940),(0.58,1010),(0.68,820),(0.78,640),(0.88,470),(1,360)], 1.2, 8640, D(2025,6,21,9,0,0)))
    r.append(recorded("gliwice-cross-6k.gpx", "Gliwice Cross 6K (synthetic)", "Synthetic cross-country race, park near Gliwice", SHAPE_CROSS, SILESIA_PARK, 6000, 9, 4,
        [(0,231),(0.3,238),(0.55,244),(0.8,236),(1,231)], 0.8, 1980, D(2025,11,2,10,0,0)))
    r.append(planned("gliwice-10k-course.gpx", "Gliwice 10K course (synthetic, planned)", "Synthetic planned 10 km course (reverse direction of the recorded loop)", reverse(SHAPE_10K), GLIWICE, 10000, 11, 5, flat, 0.4))
    r.append(planned("gliwice-half-marathon-course.gpx", "Gliwice Half Marathon course (synthetic, planned)", "Synthetic planned half marathon course (reverse direction)", reverse(SHAPE_HALF), LABEDY, 21097.5, 17, 6,
        [(0,224),(0.15,236),(0.3,248),(0.45,232),(0.6,221),(0.8,240),(0.92,230),(1,224)], 0.4))
    print(json.dumps(r, indent=1))

if __name__ == "__main__":
    main()
